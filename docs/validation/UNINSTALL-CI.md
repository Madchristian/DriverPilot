# Approval-gated signed branch uninstall validation

## Scope and current status

`.github/workflows/uninstall-validation.yml` is a **manual-only**, own-repository, non-main branch workflow. It builds a signed application and real Inno installer using the existing build/signing helpers, then exercises the uninstall lifecycle on a disposable elevated GitHub-hosted Windows runner. It does not publish a GitHub Release, create tags, push commits, or upload distributable installer/ZIP files. Only narrowly selected test evidence and public checksums are uploaded for 14 days.

Local workflow-contract checks do not establish that installation/uninstallation works. A successful, inspected remote run for the exact reviewed SHA is still required. Do not run `Test-UninstallLifecycle.ps1` on a developer PC, spoof runner variables, or import production signing keys locally.

**Signing policy:** `build/Initialize-CISigning.ps1` now includes the narrow SHA-bound manual branch predicate below, with isolated positive/negative tests in `tests/Test-CISigningAuthorization.ps1`. The existing release-tag and manual-main paths remain available. This local implementation does not configure or verify GitHub environment protection.

## GitHub setup required before dispatch (separate approval)

1. In **Madchristian/DriverPilot**, create the exact environment **`uninstall-validation`**. Merely naming an environment in YAML does not protect it: GitHub can auto-create an unprotected environment.
2. Configure required reviewers; enable prevention of self-review and disallow administrator bypass where supported. Restrict deployment branches to the specifically reviewed branch(es), not all branches and not tags. Verify these protections actually apply on the repository's visibility and GitHub plan. If required-reviewer protection is unavailable, do not dispatch this production-key workflow; a YAML environment name is not an equivalent control.
3. Have the key custodian configure environment-scoped secrets `DRIVERPILOT_SIGNING_PFX` and `DRIVERPILOT_SIGNING_PASSWORD` using the established signing custody process. Never export, print, attach, or ask an agent to handle the production private key. Prefer environment-scoped secrets over repository-wide signing secrets. Existing repository-wide secrets are a separate exposure: this file cannot prevent another workflow from using them. Any migration affecting release signing needs separate approval.
4. Review the **entire selected commit**, including this workflow, signing helpers, build scripts, tests, application source, dependency/build hooks, and cleanup helper. After import, arbitrary code in that commit can access the runner's non-exportable signing key and sign content; environment approval is a real trust decision, not a sandbox. Non-exportability is not protection against malicious approved code.
5. GitHub requires the `workflow_dispatch` workflow to exist on the default branch for normal manual dispatch discovery. Adding it only to a feature branch may therefore not make it dispatchable. Establishing it on the default branch is a separately approved repository change, not something this local implementation performs. Dispatch must still select the reviewed **non-main** branch.

## Narrow signing-helper policy

The additional manual uninstall-validation case requires all of:

- Existing `GITHUB_ACTIONS=true` and `RUNNER_ENVIRONMENT=github-hosted` restrictions.
- Exact repository `Madchristian/DriverPilot`.
- `GITHUB_EVENT_NAME=workflow_dispatch`, `GITHUB_REF` under `refs/heads/`.
- `DRIVERPILOT_SIGN_UNINSTALL_TEST=true` and `DRIVERPILOT_SIGN_RELEASE` **not** `true`.
- `DRIVERPILOT_REVIEWED_SHA` is exactly lowercase 40-hex and equals `GITHUB_SHA` case-sensitively.
- Successful `git rev-parse HEAD` equals that reviewed SHA case-sensitively.
- `GITHUB_WORKFLOW_REF` equals `"$GITHUB_REPOSITORY/.github/workflows/uninstall-validation.yml@$GITHUB_REF"` exactly.

The workflow additionally rejects forks, tags and main, and scopes both secrets to the import step. GitHub environment protection must be verified in repository settings; environment variables cannot prove reviewer approval. Do not enable generic branch signing, change `GITHUB_REF` to main, or set the release-signing switch to make this job work.

## Approval and dispatch checklist

1. Complete local review and safe tests; obtain explicit user approval for **commit**, **push**, and **manual CI dispatch** separately from approval for local implementation. None is performed by these files.
2. Record the full lowercase 40-character reviewed commit SHA. The branch selected in GitHub's **Run workflow** must point to exactly that commit when dispatch is created. Enter that exact value as `reviewed_sha`; abbreviated SHA, a tag, main, or a different branch commit is rejected. Do not paste expressions or commands into inputs.
3. Inspect the pending environment deployment and run name. Confirm repository, selected branch, full SHA, workflow version, review evidence and intended secret access before approval. If the branch has changed, review the new commit and start a new run; do not reuse approval for changed code. SHA checks bind both dispatch identity and checkout, preventing branch-head drift from silently testing a different revision.
4. Approve the environment only after confirming its protections and secret custody. The job imports signing material solely through `Initialize-CISigning.ps1`, builds/packages with `-Sign`, and runs pipeline, signature/tampering and lifecycle tests in separate Windows PowerShell processes to avoid cached `Add-Type` verifier state.
5. Download diagnostics for that exact SHA/run attempt and inspect `provenance.json`, lifecycle case results/state hashes/logs, and public package checksums. Confirm lifecycle success **and** signing cleanup success. Evidence is not a release authorization. No release publication job or write token exists here.

## Failure handling and limits

- Both signing cleanup and diagnostics upload use `always()`, including after import/build/test failures. A missing checkout can make cleanup itself fail; no key import should have happened before checkout/identity verification. Runner loss or force cancellation can prevent any final step; rely on hosted disposable-runner destruction as the final boundary, not an impossible cleanup guarantee.
- A pre-checkout rejection may leave no evidence files; upload warns rather than concealing the original failure. GitHub step logs remain the source for that rejection. Public provenance is written immediately after verified checkout.
- Never upload the runner home, certificate stores, environment dumps, PFX files, broad `artifacts/**`, signed installers, or ZIP payloads as diagnostics. No secret is intentionally logged; review all approved scripts for this property.
- The lifecycle test enforces hosted Windows, elevation, Windows PowerShell 5.1 and absence of prior installation/data. It covers all eight settings/logs/backups selection combinations with exact retained-byte checks. Installer/uninstaller invocation is silent, not Settings/UAC automation. The running-app retry test starts the normal GUI and invokes its real tray Quit command through UIAutomation; this requires an interactive English Explorer desktop on the runner. Missing/ambiguous tray controls fail the suite rather than skipping it. No driver installation or power-loss recovery is claimed.
- Inno Setup 6.3.0 or newer is required for `ExecAndLogOutput`. Both productive callbacks capture backend stdout/stderr in their `/LOG` evidence; isolated nonprivileged setup/uninstall fixtures verify this behavior. These diagnostics do not establish the cause of the earlier Sandbox Code 1 failure.
- A helper policy refusal is an intentional blocker. Fix it only through the separately reviewed narrow policy above, never by spoofing CI identity or exporting the key.

## Safe local verification

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Test-UninstallWorkflowContract.ps1
git diff --check
```

The contract is a dependency-free static source check, not a complete GitHub Actions semantic validator or execution of any signing/lifecycle code. It checks manual-only triggering, repository/branch/SHA controls, read-only permissions, protected-environment reference, helper usage, process isolation, cleanup and artifact boundaries. Remote environment configuration, secret availability, workflow default-branch registration and real destructive lifecycle results remain unverified until an explicitly approved GitHub run.
