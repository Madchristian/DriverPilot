"""Checks that the remote diagnosis contract is internally consistent.

Validates the OpenAPI document, every JSON schema, the error catalogue, the
state machine and all fixtures, including the rules that JSON Schema cannot
express (rules.json). Exits non-zero and lists every problem found.

Usage: python validate_contract.py [contract_dir]
"""

from __future__ import annotations

import hashlib
import json
import re
import sys
from datetime import datetime, timedelta
from pathlib import Path

import yaml
from jsonschema import Draft202012Validator
from openapi_spec_validator import validate as validate_openapi
from referencing import Registry, Resource

DEFAULT_CONTRACT_DIR = Path(__file__).resolve().parent / "v1"
REPORT_SCHEMA = "case-create-request"


class ContractError(Exception):
    """A request body that is not acceptable JSON under the contract."""


def parse_strict_json(data: bytes) -> dict:
    """Parses a body the way the contract demands: UTF-8, no BOM, no duplicate keys, object root."""
    if data.startswith(b"\xef\xbb\xbf"):
        raise ContractError("byte order mark")

    def reject_duplicates(pairs):
        keys = [key for key, _ in pairs]
        if len(keys) != len(set(keys)):
            raise ContractError("duplicate key")
        return dict(pairs)

    try:
        value = json.loads(data.decode("utf-8"), object_pairs_hook=reject_duplicates)
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ContractError(str(exc)) from exc
    if not isinstance(value, dict):
        raise ContractError("root is not an object")
    return value


def parse_time(value: str) -> datetime:
    return datetime.fromisoformat(value)


def resolve_pointer(document, pointer: str):
    for part in pointer.strip("/").split("/"):
        document = document.get(part) if isinstance(document, dict) else None
    return document


class SchemaSet:
    """All schema files of one contract version, addressable by short name."""

    def __init__(self, schema_dir: Path):
        self.documents = {
            path.name.removesuffix(".schema.json"): json.loads(path.read_text("utf-8"))
            for path in sorted(schema_dir.glob("*.schema.json"))
        }
        self.base_uri = schema_dir.resolve().as_uri() + "/"
        self.registry = Registry().with_resources(
            (self.base_uri + f"{name}.schema.json", Resource.from_contents(document))
            for name, document in self.documents.items()
        )

    def violations(self, name: str, instance) -> list[str]:
        validator = Draft202012Validator(
            {"$ref": self.base_uri + f"{name}.schema.json"}, registry=self.registry
        )
        return [
            f"{'/'.join(map(str, error.absolute_path)) or '<root>'}: {error.message[:160]}"
            for error in validator.iter_errors(instance)
        ]


class Contract:
    def __init__(self, root: Path):
        self.root = root
        self.schemas = SchemaSet(root / "schemas")
        self.errors = self._load("errors.json")["errors"]
        self.states = self._load("states.json")
        self.rules = self._load("rules.json")
        self.scenario = self._load("fixtures/scenario.json")
        self.openapi = yaml.safe_load((root / "openapi.yaml").read_text("utf-8"))
        self.capabilities = self._load("fixtures/valid/capabilities/pilot.json")
        self.forbidden_text = [
            (entry["id"], re.compile(entry["pattern"], re.IGNORECASE))
            for entry in self.rules["forbidden_text"]["patterns"]
        ]

    def _load(self, relative: str):
        return json.loads((self.root / relative).read_text("utf-8"))

    # --- reference implementation of the acceptance rules -----------------

    def schema_error_code(self, name: str, instance: dict) -> str | None:
        """Error code for a body that fails its schema, or None if it conforms."""
        if not self.schemas.violations(name, instance):
            return None
        if name == REPORT_SCHEMA:
            if instance.get("schema_version") not in self.capabilities["report_schema_versions"]:
                return "unsupported_schema_version"
            consent = instance.get("consent")
            if not isinstance(consent, dict) or consent.get("upload_approved") is not True:
                return "consent_required"
        return "schema_violation"

    def semantic_error_code(self, name: str, instance: dict) -> str | None:
        """Error code for a schema-valid body that breaks rules.json, or None if acceptable."""
        code = self._report_error_code(instance) if name == REPORT_SCHEMA else None
        return code or self._text_error_code(name, instance)

    def _report_error_code(self, report: dict) -> str | None:
        limits = self.rules["limits"]
        now = parse_time(self.scenario["now"])
        if report["consent"]["privacy_notice_version"] != self.capabilities["privacy_notice"]["version"]:
            return "privacy_notice_outdated"

        scanned = parse_time(report["scanned_at"])
        start, end = parse_time(report["observation_start"]), parse_time(report["observation_end"])
        accepted = parse_time(report["consent"]["accepted_at"])
        occurred = report["symptom"]["occurred_at"]
        findings = [(parse_time(f["first_seen"]), parse_time(f["last_seen"])) for f in report["findings"]]
        moments = [scanned, start, end, accepted, *(moment for pair in findings for moment in pair)]
        if occurred:
            moments.append(parse_time(occurred))

        if max(moments) > now + timedelta(seconds=limits["max_future_skew_seconds"]):
            return "clock_skew"
        if now - scanned > timedelta(seconds=limits["max_scan_age_seconds"]):
            return "scan_too_old"
        ordered = start <= end <= scanned <= accepted
        window_ok = end - start <= timedelta(seconds=limits["max_observation_window_seconds"])
        findings_ok = all(start <= first <= last <= end for first, last in findings)
        occurred_ok = not occurred or parse_time(occurred) <= scanned
        if not (ordered and window_ok and findings_ok and occurred_ok):
            return "time_range_invalid"

        device_ids = [device["id"] for device in report["devices"]]
        finding_ids = [finding["id"] for finding in report["findings"]]
        refs = {finding["device_ref"] for finding in report["findings"]} - {None}
        unique = len(set(device_ids)) == len(device_ids) and len(set(finding_ids)) == len(finding_ids)
        if not unique or not refs <= set(device_ids):
            return "reference_invalid"
        return None

    def _text_error_code(self, name: str, instance: dict) -> str | None:
        for pointer in self.rules["forbidden_text"]["applies_to"].get(name, []):
            value = resolve_pointer(instance, pointer)
            if isinstance(value, str) and any(pattern.search(value) for _, pattern in self.forbidden_text):
                return "text_rejected"
        return None


# --- checks: each returns a list of problems ------------------------------


def check_schemas(contract: Contract) -> list[str]:
    problems = []
    for name, document in contract.schemas.documents.items():
        try:
            Draft202012Validator.check_schema(document)
        except Exception as exc:  # jsonschema raises SchemaError with a long message
            problems.append(f"schema {name}: {str(exc)[:200]}")
    return problems


def check_openapi(contract: Contract) -> list[str]:
    problems = []
    try:
        validate_openapi(contract.openapi, base_uri=(contract.root / "openapi.yaml").resolve().as_uri())
    except Exception as exc:
        problems.append(f"openapi.yaml: {str(exc)[:300]}")

    text = (contract.root / "openapi.yaml").read_text("utf-8")
    referenced = set(re.findall(r"schemas/([a-z-]+)\.schema\.json", text))
    known = set(contract.schemas.documents)
    problems += [f"openapi.yaml references missing schema {name}" for name in sorted(referenced - known)]
    embedded = {"result"}  # only reachable through case.schema.json
    problems += [f"schema {name} is not used by openapi.yaml" for name in sorted(known - referenced - embedded)]
    return problems


def error_responses(openapi: dict):
    """Yields (location, status, codes) for every error response, following component refs."""
    components = openapi["components"]["responses"]
    for path, item in openapi["paths"].items():
        for method, operation in item.items():
            if method == "parameters":
                continue
            for status, response in operation["responses"].items():
                if "$ref" in response:
                    response = components[response["$ref"].rsplit("/", 1)[1]]
                if int(status) >= 400:
                    yield f"{method.upper()} {path} {status}", int(status), response.get("x-error-codes", [])


def check_error_catalog(contract: Contract) -> list[str]:
    problems = []
    catalog = {entry["code"]: entry["status"] for entry in contract.errors}
    schema_codes = contract.schemas.documents["error"]["properties"]["error"]["properties"]["code"]["enum"]
    if len(catalog) != len(contract.errors):
        problems.append("errors.json contains duplicate codes")
    if set(schema_codes) != set(catalog):
        problems.append(f"error.schema.json and errors.json differ: {sorted(set(schema_codes) ^ set(catalog))}")

    used = set()
    for location, status, codes in error_responses(contract.openapi):
        if not codes:
            problems.append(f"{location}: no x-error-codes")
        for code in codes:
            used.add(code)
            if catalog.get(code) != status:
                problems.append(f"{location}: code {code} has status {catalog.get(code)} in errors.json")
    problems += [f"error code {code} is not used by any operation" for code in sorted(set(catalog) - used)]
    return problems


def check_states(contract: Contract) -> list[str]:
    known = set(contract.schemas.documents["common"]["$defs"]["case_status"]["enum"])
    states = contract.states
    mentioned = (
        {entry["to"] for entry in states["initial"]}
        | {state for entry in states["transitions"] for state in (entry["from"], entry["to"])}
        | set(states["polling_ends_in"])
    )
    problems = [f"states.json uses unknown state {state}" for state in sorted(mentioned - known)]
    reachable = {entry["to"] for entry in states["initial"]} | {entry["to"] for entry in states["transitions"]}
    problems += [f"state {state} is unreachable" for state in sorted(known - reachable)]
    return problems


def check_rules(contract: Contract) -> list[str]:
    problems = []
    limits, capabilities = contract.rules["limits"], contract.capabilities
    for key in ("max_upload_bytes", "max_scan_age_seconds", "max_future_skew_seconds", "case_retention_days"):
        if capabilities[key] != limits[key]:
            problems.append(f"capabilities fixture and rules.json disagree on {key}")
    for name, pointers in contract.rules["forbidden_text"]["applies_to"].items():
        if name not in contract.schemas.documents:
            problems.append(f"rules.json applies text rules to unknown schema {name}")
        for pointer in pointers:
            schema = contract.schemas.documents.get(name, {})
            for part in pointer.strip("/").split("/"):
                schema = schema.get("properties", {}).get(part) or schema.get("$defs", {}).get(part, {})
                if "$ref" in schema and schema["$ref"].startswith("#/$defs/"):
                    schema = contract.schemas.documents[name]["$defs"][schema["$ref"].rsplit("/", 1)[1]]
            if not schema:
                problems.append(f"rules.json pointer {pointer} does not exist in schema {name}")
    return problems


def check_valid_fixtures(contract: Contract) -> list[str]:
    problems = []
    max_bytes = contract.rules["limits"]["max_upload_bytes"]
    for path in sorted((contract.root / "fixtures" / "valid").glob("*/*.json")):
        name, label = path.parent.name, f"valid/{path.parent.name}/{path.name}"
        if name not in contract.schemas.documents:
            problems.append(f"{label}: no schema named {name}")
            continue
        data = path.read_bytes()
        try:
            instance = parse_strict_json(data)
        except ContractError as exc:
            problems.append(f"{label}: {exc}")
            continue
        problems += [f"{label}: {violation}" for violation in contract.schemas.violations(name, instance)]
        if name in contract.rules["forbidden_text"]["applies_to"] and not contract.schemas.violations(name, instance):
            code = contract.semantic_error_code(name, instance)
            if code:
                problems.append(f"{label}: would be rejected with {code}")
            if len(data) > max_bytes:
                problems.append(f"{label}: larger than {max_bytes} bytes")
        digest = path.with_suffix(".sha256")
        if digest.exists() and digest.read_text("ascii").strip() != hashlib.sha256(data).hexdigest():
            problems.append(f"{label}: bytes do not match {digest.name}")
    return problems


def check_invalid_fixtures(contract: Contract) -> list[str]:
    problems = []
    expectations = contract.scenario["invalid"]
    directory = contract.root / "fixtures" / "invalid"
    on_disk = {f"{path.parent.name}/{path.name}" for path in directory.glob("*/*.json")}
    problems += [f"invalid/{key}: listed in scenario.json but missing" for key in sorted(set(expectations) - on_disk)]
    problems += [f"invalid/{key}: not listed in scenario.json" for key in sorted(on_disk - set(expectations))]
    catalog = {entry["code"]: entry["status"] for entry in contract.errors}

    for key in sorted(on_disk & set(expectations)):
        expected, name = expectations[key], key.split("/")[0]
        if name not in contract.schemas.documents:
            problems.append(f"invalid/{key}: no schema named {name}")
            continue
        if expected["code"] is not None and catalog.get(expected["code"]) != expected["status"]:
            problems.append(f"invalid/{key}: expected status does not match errors.json")
        try:
            instance = parse_strict_json((directory / key).read_bytes())
        except ContractError:
            actual = ("malformed_json", "invalid_json")
        else:
            schema_code = contract.schema_error_code(name, instance)
            if schema_code:
                # Results are server output: there is no error code, the client just refuses them.
                actual = ("schema", schema_code if expected["code"] else None)
            else:
                actual = ("semantic", contract.semantic_error_code(name, instance))
        if actual != (expected["kind"], expected["code"]):
            problems.append(f"invalid/{key}: expected {expected['kind']}/{expected['code']}, got {actual[0]}/{actual[1]}")
    return problems


def check_scenario(contract: Contract) -> list[str]:
    """The valid fixtures tell one consistent story about a single case."""
    problems = []
    fixtures = contract.root / "fixtures"
    report_bytes = (fixtures / contract.scenario["report"]).read_bytes()
    report, digest = json.loads(report_bytes), hashlib.sha256(report_bytes).hexdigest()
    accepted = json.loads((fixtures / "valid/case-accepted/queued.json").read_text("utf-8"))
    result = json.loads((fixtures / "valid/result/ai-assisted.json").read_text("utf-8"))

    if accepted["received_at"] != contract.scenario["now"]:
        problems.append("scenario now differs from received_at of the accepted case")
    retention = timedelta(days=contract.rules["limits"]["case_retention_days"])
    if parse_time(accepted["expires_at"]) - parse_time(accepted["received_at"]) != retention:
        problems.append("expires_at is not received_at plus the retention period")

    bound = [("case-accepted/queued.json", accepted), ("result/ai-assisted.json", result)]
    bound += [(f"case/{p.name}", json.loads(p.read_text("utf-8"))) for p in sorted((fixtures / "valid/case").glob("*.json"))]
    for label, document in bound:
        if document["report_sha256"] != digest:
            problems.append(f"valid/{label}: report_sha256 is not the hash of the report bytes")
        if document["case_id"] != accepted["case_id"]:
            problems.append(f"valid/{label}: case_id differs from the accepted case")
        if document.get("scan_id", report["scan_id"]) != report["scan_id"]:
            problems.append(f"valid/{label}: scan_id differs from the report")

    known = {"finding": {f["id"] for f in report["findings"]}, "device": {d["id"] for d in report["devices"]}}
    fact_ids = {fact["id"] for fact in result["facts"]}
    for fact in result["facts"]:
        for reference in fact["references"]:
            if reference["kind"] == "report_field":
                if resolve_pointer(report, reference["pointer"]) is None:
                    problems.append(f"result fact {fact['id']}: pointer {reference['pointer']} not in report")
            elif reference["id"] not in known[reference["kind"]]:
                problems.append(f"result fact {fact['id']}: unknown {reference['kind']} {reference['id']}")
    for hypothesis in result["hypotheses"]:
        problems += [f"result hypothesis: unknown fact {ref}" for ref in set(hypothesis["fact_refs"]) - fact_ids]
    size = len(json.dumps(result, ensure_ascii=False).encode("utf-8"))
    if size > contract.rules["limits"]["max_result_bytes"]:
        problems.append("result fixture exceeds max_result_bytes")
    return problems


CHECKS = [
    check_schemas,
    check_openapi,
    check_error_catalog,
    check_states,
    check_rules,
    check_valid_fixtures,
    check_invalid_fixtures,
    check_scenario,
]


def main(argv: list[str]) -> int:
    contract = Contract(Path(argv[1]) if len(argv) > 1 else DEFAULT_CONTRACT_DIR)
    problems = [problem for check in CHECKS for problem in check(contract)]
    for problem in problems:
        print(f"FAIL {problem}")
    if problems:
        print(f"{len(problems)} problem(s) in {contract.root}")
        return 1
    valid = len(list((contract.root / "fixtures" / "valid").glob("*/*.json")))
    print(
        f"OK {contract.root.name}: {len(contract.schemas.documents)} schemas, {len(contract.errors)} error codes, "
        f"{valid} valid and {len(contract.scenario['invalid'])} invalid fixtures"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
