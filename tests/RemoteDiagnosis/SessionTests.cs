using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DriverPilot.RemoteDiagnosis;
using static DriverPilot.Tests.Assert;
namespace DriverPilot.Tests;

static class ResponseTests
{
    static readonly ContractRules Rules = ContractRules.Embedded;
    static void Parse(string schema, byte[] json)
    {
        switch (schema)
        {
            case "capabilities": ContractResponse.Parse<Capabilities>(json, ContractResponse.Validate); break;
            case "pairing-redeem-response": ContractResponse.Parse<PairingResponse>(json, ContractResponse.Validate); break;
            case "case-accepted": ContractResponse.Parse<CaseAccepted>(json, ContractResponse.Validate); break;
            case "case": ContractResponse.Parse<CaseView>(json, ContractResponse.Validate); break;
            case "result": ContractResponse.Parse<CaseResult>(json, ContractResponse.Validate); break;
            case "feedback-response": ContractResponse.Parse<FeedbackResponse>(json, ContractResponse.Validate); break;
            case "error": ContractResponse.Parse<ErrorBody>(json, e => ContractResponse.Validate(e, Rules.ErrorCodes)); break;
            default: throw new ArgumentException(schema);
        }
    }
    static bool Accepts(string schema, byte[] json)
    {
        try { Parse(schema, json); return true; }
        catch (RemoteApiException ex) when (ex.Code == RemoteApiException.InvalidResponse) { return false; }
    }

    public static string Run()
    {
        string[] schemas = ["capabilities", "pairing-redeem-response", "case-accepted", "case", "result", "feedback-response", "error"];
        var count = 0;
        foreach (var schema in schemas)
            foreach (var path in Contract.Fixtures("valid", schema)) { Check(Accepts(schema, File.ReadAllBytes(path)), "accepts valid/" + schema + "/" + Path.GetFileName(path)); count++; }
        foreach (var path in Contract.Fixtures("invalid", "result")) { Check(!Accepts("result", File.ReadAllBytes(path)), "refuses invalid/result/" + Path.GetFileName(path)); count++; }

        // Every mutation below also violates the schema: the client is never stricter or laxer than the contract here.
        void Refused(string schema, string fixture, Action<JsonObject> mutate, string name)
        {
            var node = JsonNode.Parse(Contract.Fixture(fixture))!.AsObject();
            mutate(node);
            var json = Encoding.UTF8.GetBytes(node.ToJsonString());
            Check(!Contract.Conforms(schema, json), "schema refuses: " + name);
            Check(!Accepts(schema, json), "client refuses: " + name);
        }
        Refused("case", "valid/case/queued.json", n => n["draft"] = "internal", "unknown member");
        Refused("case", "valid/case/queued.json", n => n.Remove("result"), "missing member");
        Refused("case", "valid/case/queued.json", n => n["status"] = "done", "unknown state");
        Refused("case", "valid/case/queued.json", n => n["poll_after_seconds"] = null, "active case without poll interval");
        Refused("case", "valid/case/released.json", n => n["result"] = null, "released without result");
        Refused("case", "valid/case/released.json", n => n["poll_after_seconds"] = 10, "released case that keeps polling");
        Refused("case", "valid/case/analysis-failed.json", n => n["status_reason"] = null, "failed without reason");
        Refused("case", "valid/case/analysis-failed.json", n => n["status_reason"] = "stack trace", "unknown failure reason");
        Refused("case-accepted", "valid/case-accepted/queued.json", n => n["report_sha256"] = "ABC", "malformed hash");
        Refused("case-accepted", "valid/case-accepted/queued.json", n => n["received_at"] = "2026-10-03T16:06:41+02:00", "non-UTC timestamp");
        Refused("capabilities", "valid/capabilities/pilot.json", n => n["max_upload_bytes"] = 999999, "upload limit above the contract");
        Refused("capabilities", "valid/capabilities/pilot.json", n => n["protocol_version"] = "2.0", "incompatible protocol");
        Refused("pairing-redeem-response", "valid/pairing-redeem-response/synthetic.json", n => n["access_token"] = "short", "weak token");
        Refused("result", "valid/result/ai-assisted.json", n => n["summary"] = new string('x', 2001), "summary too long");
        Refused("result", "valid/result/ai-assisted.json", n => n["warnings"]![0] = null, "null inside list");
        Refused("result", "valid/result/ai-assisted.json", n => n["next_steps"]![0]!["rollback"] = "", "empty optional text");
        Refused("error", "valid/error/not-found.json", n => n["error"]!["code"] = "made_up", "unknown error code");
        Refused("error", "valid/error/not-found.json", n => n["trace"] = "at Server.Main()", "error with stack trace member");

        var text = RemoteText.Result(ContractResponse.Parse<CaseResult>(Contract.Fixture("valid/result/ai-assisted.json"), ContractResponse.Validate), "2026-10-03T14:02:11Z");
        Check(text.Contains("<script>alert(1)</script>") && text.Contains("NICHT BEWIESEN") && text.Contains("KI-Entwurf, von Christian geprüft") && text.Contains("Revision 1") && text.Contains("https://support.example.invalid/artikel/1"), "result is rendered as inert plain text with origin, revision and unproven marker");
        return $"PASS: {count} contract fixtures and 18 mutations read strictly; result rendered as plain text";
    }
}

static class SessionTests
{
    static readonly ContractRules Rules = ContractRules.Embedded;
    const string Token = "SYNTHETIC-access-token-not-a-secret-000000000000";
    const string Invitation = "SYNTHETIC-invitation-not-a-secret-0000";
    const string CaseId = "5f0c2a7e-3b1d-4c8a-9e21-7a4d0b6c1f02";

    sealed class World
    {
        public FakeServer Server = new();
        public MemoryStore Store = new();
        public DateTimeOffset Now = Synthetic.ScannedAt.AddMinutes(4);
        public List<Uri> Origins = [];
        public RemoteDiagnosisSession Session() => new(Store, new TestProtector(), origin => { Origins.Add(origin); return new RemoteApiClient(Server, origin, "0.4.0", Rules); }, () => Now, Rules, "0.4.0");
    }

    static string CaseJson(string status, string sha, JsonNode? result = null, string? reason = null, int? poll = 10)
        => new JsonObject { ["case_id"] = CaseId, ["status"] = status, ["status_reason"] = reason, ["scan_id"] = Synthetic.ScanId.ToString(), ["report_sha256"] = sha,
            ["received_at"] = "2026-10-03T14:06:41Z", ["expires_at"] = "2026-10-10T14:06:41Z", ["poll_after_seconds"] = poll, ["result"] = result }.ToJsonString();
    static string Accepted(string sha) => new JsonObject { ["case_id"] = CaseId, ["status"] = "queued", ["received_at"] = "2026-10-03T14:06:41Z", ["expires_at"] = "2026-10-10T14:06:41Z", ["report_sha256"] = sha, ["poll_after_seconds"] = 10 }.ToJsonString();
    static JsonNode Result(string sha, string? caseId = null)
    {
        var node = JsonNode.Parse(Contract.Fixture("valid/result/ai-assisted.json"))!;
        node["report_sha256"] = sha; node["case_id"] = caseId ?? CaseId;
        return node;
    }

    static async Task<RemoteDiagnosisSession> Paired(World world)
    {
        var session = world.Session();
        world.Server.Reply(HttpStatusCode.Created, Contract.Fixture("valid/pairing-redeem-response/synthetic.json"));
        Check(await session.PairAsync("https://diagnose.example.invalid", Invitation), "pairing succeeds");
        world.Server.Reply(HttpStatusCode.OK, Contract.Fixture("valid/capabilities/pilot.json"));
        Check(await session.LoadCapabilitiesAsync(), "capabilities load");
        return session;
    }

    public static async Task<string> Run()
    {
        await PairingAndStorage();
        await UploadRetryAndBinding();
        await ConsentRules();
        await CaseLifecycle();
        await Transport();
        return "PASS: pairing, protected token, frozen retry with same bytes and key, consent rules, result binding, polling, feedback, deletion, transport safety";
    }

    static async Task PairingAndStorage()
    {
        var world = new World();
        var session = world.Session();
        Check(!await session.PairAsync("http://diagnose.example.invalid", Invitation) && !await session.PairAsync("https://user@diagnose.example.invalid", Invitation)
              && !await session.PairAsync("https://diagnose.example.invalid/api?x=1", Invitation) && !await session.PairAsync("https://diagnose.example.invalid", "short"), "unsafe address or malformed invitation never reaches the network");
        Check(world.Server.Requests.Count == 0 && world.Origins.Count == 0, "no request for rejected input");

        world.Server.ReplyError(HttpStatusCode.Unauthorized, "invitation_invalid");
        Check(!await session.PairAsync("https://diagnose.example.invalid", Invitation) && !session.Paired && session.Status.Contains("Einladung"), "used or unknown invitation explained");

        session = await Paired(world);
        var redeem = world.Server.Requests[1];
        Check(redeem is { Authorization: null, IdempotencyKey: null, ContentType: "application/json; charset=utf-8", HasContentEncoding: false } && redeem.Path == "https://diagnose.example.invalid/api/v1/pairings/redeem"
              && Contract.Conforms("pairing-redeem-request", redeem.Body), "invitation travels only in a schema-conforming body");
        var saved = JsonSerializer.Serialize(world.Store.State);
        Check(!saved.Contains(Token) && world.Store.State.Access is { Origin: "https://diagnose.example.invalid/" } && session.Paired && session.ServerHost == "diagnose.example.invalid", "token is stored protected, bound to its origin");

        Check(world.Session().Paired, "access survives a restart");
        world.Now = new DateTimeOffset(2026, 11, 2, 9, 15, 0, TimeSpan.Zero);
        Check(!world.Session().Paired && world.Store.State.Access == null, "expired access is discarded on load");
        world.Store.State = new(new("https://diagnose.example.invalid/", "9b2e4d6f-1a3c-4e5f-8b7d-2c4e6f8a0b1d", "plain:" + Token, "2027-01-01T00:00:00Z"));
        Check(!world.Session().Paired && world.Store.State.Access == null, "unreadable token is discarded");
    }

    static async Task UploadRetryAndBinding()
    {
        var world = new World();
        var session = await Paired(world);
        Check(session.Prepare(Synthetic.Snapshot(), Synthetic.ScanId, Synthetic.ScannedAt, Synthetic.Symptom()) && session.Preview != null && session.CanSend, "preview from fresh scan");

        world.Server.Fail();
        Check(!await session.ApproveAndSendAsync(true) && session.HasPendingUpload && session.Status.Contains("lokal gespeichert"), "network failure keeps the frozen request and points to the offline export");
        world.Server.ReplyError(HttpStatusCode.ServiceUnavailable, "service_unavailable", 120);
        Check(!await session.ApproveAndSendAsync(true) && session.HasPendingUpload && session.Status.Contains("2 Minute"), "503 keeps the frozen request");
        world.Now = world.Now.AddMinutes(30);
        var first = world.Server.Requests[^2];
        var sha = Convert.ToHexStringLower(SHA256.HashData(first.Body));
        world.Server.Reply(HttpStatusCode.Accepted, Accepted(sha));
        Check(await session.ApproveAndSendAsync(true) && !session.HasPendingUpload, "retry succeeds even after the scan aged, because the first attempt may have been accepted");

        var attempts = world.Server.Requests.Where(r => r.Path.EndsWith("/api/v1/cases")).ToList();
        Check(attempts.Count == 3 && attempts.All(a => a.Body.AsSpan().SequenceEqual(first.Body) && a.IdempotencyKey == first.IdempotencyKey && a.Authorization == "Bearer " + Token), "all attempts carry identical bytes, key and token");
        Check(ContractFormat.IsUuid(first.IdempotencyKey) && Contract.Conforms("case-create-request", first.Body) && first.Body.Length < Rules.MaxUploadBytes, "sent body conforms to the contract");
        using (var sent = JsonDocument.Parse(first.Body))
        {
            var consent = sent.RootElement.GetProperty("consent");
            Check(consent.GetProperty("privacy_notice_version").GetString() == "2026-10-01.1" && consent.GetProperty("upload_approved").GetBoolean() && consent.GetProperty("external_ai_allowed").GetBoolean()
                  && consent.GetProperty("accepted_at").GetString() == "2026-10-03T14:06:11Z" && sent.RootElement.GetProperty("previous_case_id").ValueKind == JsonValueKind.Null, "consent names the shown notice version and the click time");
        }
        Check(world.Store.State.Case is { CaseId: CaseId, ScannedAt: "2026-10-03T14:02:11Z", ExpiresAt: "2026-10-10T14:06:41Z" } reference && reference.ReportSha256 == sha && reference.ScanId == Synthetic.ScanId.ToString(), "case reference persisted with scan binding");
        Check(!JsonSerializer.Serialize(world.Store.State).Contains("stürzt"), "no report content is persisted locally");

        // A server that answers with a different hash did not store what the user approved.
        var other = new World();
        var mismatch = await Paired(other);
        mismatch.Prepare(Synthetic.Snapshot(), Synthetic.ScanId, Synthetic.ScannedAt, Synthetic.Symptom());
        other.Server.Reply(HttpStatusCode.Accepted, Accepted(new string('0', 64)));
        Check(!await mismatch.ApproveAndSendAsync(false) && mismatch.CaseRef == null && !mismatch.HasPendingUpload, "hash mismatch is refused");

        // The next report of this client points to its predecessor.
        world.Now = Synthetic.ScannedAt.AddMinutes(1);
        session.Prepare(Synthetic.Snapshot(), Guid.NewGuid(), Synthetic.ScannedAt, Synthetic.Symptom());
        Check(session.Preview!.Contains($"\"previous_case_id\": \"{CaseId}\""), "follow-up report references the own previous case");
    }

    static async Task ConsentRules()
    {
        var world = new World();
        var session = await Paired(world);
        var before = world.Server.Requests.Count;
        Check(!await session.ApproveAndSendAsync(true) && world.Server.Requests.Count == before, "nothing is sent without a preview");

        world.Now = Synthetic.ScannedAt.AddMinutes(16);
        Check(!session.Prepare(Synthetic.Snapshot(), Synthetic.ScanId, Synthetic.ScannedAt, Synthetic.Symptom()) && session.Preview == null, "scan older than 15 minutes gives no preview");
        world.Now = Synthetic.ScannedAt.AddMinutes(14);
        Check(session.Prepare(Synthetic.Snapshot(), Synthetic.ScanId, Synthetic.ScannedAt, Synthetic.Symptom()), "preview at 14 minutes");
        world.Now = Synthetic.ScannedAt.AddMinutes(16);
        Check(!await session.ApproveAndSendAsync(true) && world.Server.Requests.Count == before && session.Preview == null, "scan that aged during the preview is not sent");

        world.Now = Synthetic.ScannedAt.AddMinutes(2);
        Check(!session.Prepare(Synthetic.Snapshot(), Synthetic.ScanId, Synthetic.ScannedAt, Synthetic.Symptom() with { Description = "Mail an alice@example.invalid" }) && !session.CanSend, "forbidden text gives no preview");

        // A definitive rejection consumes the consent: the same bytes are never offered again.
        session.Prepare(Synthetic.Snapshot(), Synthetic.ScanId, Synthetic.ScannedAt, Synthetic.Symptom());
        world.Server.ReplyError(HttpStatusCode.UnprocessableEntity, "privacy_notice_outdated");
        Check(!await session.ApproveAndSendAsync(false) && !session.HasPendingUpload && session.Preview == null && session.Status.Contains("Datenschutzhinweis"), "422 discards the frozen request");
        using (var sent = JsonDocument.Parse(world.Server.Requests[^1].Body))
            Check(!sent.RootElement.GetProperty("consent").GetProperty("external_ai_allowed").GetBoolean(), "external AI stays off unless chosen");

        // Editing after a failed attempt creates a new request instead of reusing the old consent.
        session.Prepare(Synthetic.Snapshot(), Synthetic.ScanId, Synthetic.ScannedAt, Synthetic.Symptom());
        world.Server.Fail();
        await session.ApproveAndSendAsync(false);
        var firstKey = world.Server.Requests[^1].IdempotencyKey;
        session.Prepare(Synthetic.Snapshot(), Synthetic.ScanId, Synthetic.ScannedAt, Synthetic.Symptom() with { Description = "Anderer Text" });
        Check(!session.HasPendingUpload, "editing invalidates the frozen request");
        world.Server.Fail();
        await session.ApproveAndSendAsync(false);
        Check(world.Server.Requests[^1].IdempotencyKey != firstKey, "changed report gets a new idempotency key");

        // Server without AI offer: the flag is forced off even if the caller passes true.
        var manual = new World();
        var manualSession = manual.Session();
        manual.Server.Reply(HttpStatusCode.Created, Contract.Fixture("valid/pairing-redeem-response/synthetic.json"));
        await manualSession.PairAsync("https://diagnose.example.invalid", Invitation);
        var capabilities = JsonNode.Parse(Contract.Fixture("valid/capabilities/pilot.json"))!;
        capabilities["external_ai_offered"] = false;
        manual.Server.Reply(HttpStatusCode.OK, capabilities.ToJsonString());
        await manualSession.LoadCapabilitiesAsync();
        manualSession.Prepare(Synthetic.Snapshot(), Synthetic.ScanId, Synthetic.ScannedAt, Synthetic.Symptom());
        manual.Server.Fail();
        await manualSession.ApproveAndSendAsync(true);
        using (var sent = JsonDocument.Parse(manual.Server.Requests[^1].Body))
            Check(!sent.RootElement.GetProperty("consent").GetProperty("external_ai_allowed").GetBoolean(), "AI flag cannot be set when the server does not offer it");

        capabilities["report_schema_versions"] = new JsonArray("2.0");
        manual.Server.Reply(HttpStatusCode.OK, capabilities.ToJsonString());
        Check(!await manualSession.LoadCapabilitiesAsync() && manualSession.Capabilities == null && !manualSession.CanSend, "unsupported report version blocks sending");
    }

    static async Task CaseLifecycle()
    {
        // Learn the hash of the body first: a scripted server cannot compute it.
        var probe = new World();
        var probeSession = await Paired(probe);
        probeSession.Prepare(Synthetic.Snapshot(), Synthetic.ScanId, Synthetic.ScannedAt, Synthetic.Symptom());
        probe.Server.Fail();
        await probeSession.ApproveAndSendAsync(false);
        var sha = Convert.ToHexStringLower(SHA256.HashData(probe.Server.Requests[^1].Body));

        var world = new World();
        var session = await Paired(world);
        session.Prepare(Synthetic.Snapshot(), Synthetic.ScanId, Synthetic.ScannedAt, Synthetic.Symptom());
        world.Server.Reply(HttpStatusCode.Accepted, Accepted(sha));
        Check(await session.ApproveAndSendAsync(false), "case accepted");
        Check(session.NextPoll(0) == TimeSpan.FromSeconds(10), "polling starts at 10 seconds");

        world.Server.Reply(HttpStatusCode.OK, CaseJson("awaiting_review", sha, poll: 30), etag: "\"v1\"");
        Check(await session.RefreshAsync() && session.Case!.Status == "awaiting_review" && session.NextPoll(0) == TimeSpan.FromSeconds(30), "server poll interval is honoured");
        world.Server.Reply(HttpStatusCode.NotModified);
        Check(await session.RefreshAsync() && session.Case!.Status == "awaiting_review" && world.Server.Requests[^1].IfNoneMatch == "\"v1\"", "304 keeps the state and sends If-None-Match");

        world.Server.ReplyError(HttpStatusCode.TooManyRequests, "rate_limited", 90);
        Check(!await session.RefreshAsync() && session.NextPoll(0) == TimeSpan.FromSeconds(90), "Retry-After wins over backoff");
        world.Server.Fail();
        Check(!await session.RefreshAsync() && session.NextPoll(0) == TimeSpan.FromSeconds(40) && session.NextPoll(1) == TimeSpan.FromSeconds(48), "backoff doubles with at most 20 percent jitter");
        world.Server.Fail(); world.Server.Fail();
        await session.RefreshAsync(); await session.RefreshAsync();
        Check(session.NextPoll(0) == TimeSpan.FromSeconds(60), "backoff is capped at 60 seconds");

        // A result bound to another report or case must never be shown.
        world.Server.Reply(HttpStatusCode.OK, CaseJson("released", sha, Result(new string('0', 64)), poll: null), etag: "\"v2\"");
        Check(!await session.RefreshAsync() && session.Case!.Result == null && session.Status.Contains("unerwartete Antwort"), "result for a different report is refused");
        world.Server.Reply(HttpStatusCode.OK, CaseJson("released", sha, Result(sha, "00000000-0000-4000-8000-000000000000"), poll: null), etag: "\"v2\"");
        Check(!await session.RefreshAsync() && session.Case!.Result == null, "result for a different case is refused");

        world.Server.Reply(HttpStatusCode.OK, CaseJson("released", sha, Result(sha), poll: null), etag: "\"v2\"");
        Check(await session.RefreshAsync() && session.Case!.Result!.Revision == 1 && session.NextPoll(0) == null, "bound result is shown and polling ends");
        Check(!session.IsCaseForOlderScan(Synthetic.ScannedAt) && session.IsCaseForOlderScan(Synthetic.ScannedAt.AddMinutes(5)), "newer local scan marks the result as older");

        Check(!await session.SendFeedbackAsync("unchanged", "Siehe https://example.invalid/log") && world.Server.Requests[^1].Method == HttpMethod.Get, "feedback note with URL is not sent");
        world.Server.Fail();
        await session.SendFeedbackAsync("unchanged", "Weiterhin Absturz nach 20 Minuten.");
        world.Server.Reply(HttpStatusCode.Created, Contract.Fixture("valid/feedback-response/created.json"));
        Check(await session.SendFeedbackAsync("unchanged", "Weiterhin Absturz nach 20 Minuten."), "feedback retry succeeds");
        var feedback = world.Server.Requests.Where(r => r.Path.EndsWith("/feedback")).ToList();
        Check(feedback.Count == 2 && feedback[0].IdempotencyKey == feedback[1].IdempotencyKey && feedback[0].Body.AsSpan().SequenceEqual(feedback[1].Body) && Contract.Conforms("feedback-request", feedback[0].Body)
              && feedback[0].Path == $"https://diagnose.example.invalid/api/v1/cases/{CaseId}/feedback", "feedback retry reuses key and bytes and conforms");

        world.Server.Reply(HttpStatusCode.NoContent);
        Check(await session.DeleteCaseAsync() && session.CaseRef == null && session.Case == null && world.Store.State.Case == null && world.Server.Requests[^1].Method == HttpMethod.Delete && session.NextPoll(0) == null, "deletion removes the local reference and stops polling");
        Check(world.Store.State.Access != null, "deleting a case keeps the access");

        // A case that vanished on the server is dropped locally; an invalid token ends everything.
        world.Store.State = world.Store.State with { Case = new(CaseId, Synthetic.ScanId.ToString(), sha, "2026-10-03T14:02:11Z", "2026-10-10T14:06:41Z") };
        session = world.Session();
        world.Server.ReplyError(HttpStatusCode.NotFound, "not_found");
        Check(!await session.RefreshAsync() && session.CaseRef == null && session.Paired, "expired or deleted case is dropped");
        world.Store.State = world.Store.State with { Case = new(CaseId, Synthetic.ScanId.ToString(), sha, "2026-10-03T14:02:11Z", "2026-10-10T14:06:41Z") };
        session = world.Session();
        world.Server.ReplyError(HttpStatusCode.Unauthorized, "token_invalid");
        Check(!await session.RefreshAsync() && !session.Paired && world.Store.State.Access == null && session.NextPoll(0) == null && session.Status.Contains("neue Einladung"), "revoked token is discarded and polling stops");
    }

    static async Task Transport()
    {
        var world = new World();
        var session = await Paired(world);
        world.Server.Reply(HttpStatusCode.Found, "{}");
        Check(!await session.LoadCapabilitiesAsync() && session.Status.Contains("Weiterleitung") && world.Server.Requests.Count == 3, "redirect is reported, not followed");
        world.Server.Reply(HttpStatusCode.OK, Encoding.UTF8.GetBytes("<html>proxy</html>"), contentType: "text/html");
        Check(!await session.LoadCapabilitiesAsync() && session.Status.Contains("unerwartete Antwort"), "non-JSON success is refused");
        world.Server.Reply(HttpStatusCode.BadGateway, Encoding.UTF8.GetBytes("<html>bad gateway</html>"), contentType: "text/html");
        Check(!await session.LoadCapabilitiesAsync() && session.Status.Contains("nicht bereit"), "proxy error page is treated by status only");
        world.Server.Reply(HttpStatusCode.OK, "{\"protocol_version\":\"1.0\",\"injected\":\"<script>\"}");
        Check(!await session.LoadCapabilitiesAsync() && !session.Status.Contains("script"), "malformed body is never echoed");
        Check(world.Server.Requests.All(r => r.Path.StartsWith("https://diagnose.example.invalid/api/v1/") && !r.Path.Contains('?')), "every request stays on the paired origin without query data");
        Check(world.Server.Requests.Where(r => r.Path.EndsWith("/capabilities")).All(r => r.Authorization == null), "capabilities need no token");
        Check(RemoteApiClient.CreateHandler() is SocketsHttpHandler { AllowAutoRedirect: false, UseCookies: false, AutomaticDecompression: DecompressionMethods.None }, "production handler: no redirects, cookies or decompression");
    }
}
