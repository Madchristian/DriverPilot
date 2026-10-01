using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace DriverPilot.RemoteDiagnosis;

// Response shapes of contracts/remote-diagnosis/v1/schemas. Parsed strictly: unknown or missing members invalidate the response.
public sealed record PrivacyNotice(string Version, string Text);
public sealed record Capabilities(string ProtocolVersion, IReadOnlyList<string> ReportSchemaVersions, string ResultSchemaVersion, int MaxUploadBytes,
    int MaxScanAgeSeconds, int MaxFutureSkewSeconds, int CaseRetentionDays, PrivacyNotice PrivacyNotice, bool ExternalAiOffered);
public sealed record PairingResponse(string ClientId, string AccessToken, string ExpiresAt);
public sealed record CaseAccepted(string CaseId, string Status, string ReceivedAt, string ExpiresAt, string ReportSha256, int? PollAfterSeconds);
public sealed record CaseView(string CaseId, string Status, string? StatusReason, string ScanId, string ReportSha256, string ReceivedAt, string ExpiresAt, int? PollAfterSeconds, CaseResult? Result);
public sealed record ResultReference(string Kind, string? Id = null, string? Pointer = null);
public sealed record ResultFact(string Id, string Text, IReadOnlyList<ResultReference> References);
public sealed record ResultHypothesis(string Text, bool Proven, IReadOnlyList<string> FactRefs);
public sealed record ResultStep(string Title, string Rationale, string Instructions, string RiskClass, string RequiredPrivileges, string ExpectedOutcome, string? Rollback, string? AbortCondition);
public sealed record ResultSource(string Title, string Url);
public sealed record CaseResult(string ResultSchemaVersion, string CaseId, string ScanId, string ReportSha256, int Revision, string ReleasedAt, string Origin, string Summary,
    IReadOnlyList<ResultFact> Facts, IReadOnlyList<ResultHypothesis> Hypotheses, IReadOnlyList<ResultStep> NextSteps, IReadOnlyList<string> OpenQuestions,
    IReadOnlyList<string> Warnings, IReadOnlyList<ResultSource> Sources);
public sealed record FeedbackRequest(string Outcome, string? Note, int ResultRevision);
public sealed record FeedbackResponse(string FeedbackId, string CaseId, int ResultRevision, string ReceivedAt);
public sealed record ErrorDetail(string Code, string Message);
public sealed record ErrorBody(ErrorDetail Error, string RequestId, int? RetryAfterSeconds);

/// <summary>A request that did not succeed. Code is a contract error code or one of the Local* codes.</summary>
public sealed class RemoteApiException(string code, int status, int? retryAfterSeconds = null, string? serverMessage = null) : Exception(code)
{
    public const string Network = "local_network", Redirect = "local_redirect", InvalidResponse = "local_invalid_response";
    public string Code { get; } = code;
    public int Status { get; } = status;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
    public string? ServerMessage { get; } = serverMessage;
    /// <summary>The same bytes with the same idempotency key may succeed later.</summary>
    public bool Transient => Code == Network || Status == 429 || Status >= 500;
}

/// <summary>Strict reading of server responses: wrong shape, type, enum value or length raises InvalidResponse.</summary>
public static class ContractResponse
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };
    static readonly string[] CaseStates = ["queued", "analyzing", "awaiting_review", "released", "analysis_failed"];
    static readonly string[] StatusReasons = ["provider_unavailable", "budget_exhausted", "output_rejected", "outcome_unknown"];
    static readonly Regex SchemaVersion = new(@"\A[0-9]{1,3}\.[0-9]{1,3}\z", RegexOptions.CultureInvariant);
    static readonly Regex Token = new(@"\A[A-Za-z0-9_-]{43,128}\z", RegexOptions.CultureInvariant);
    static readonly Regex Pointer = new(@"\A(/[a-z0-9_]+)+\z", RegexOptions.CultureInvariant);
    static readonly Regex SourceUrl = new(@"\Ahttps://[a-z0-9]([a-z0-9.-]*[a-z0-9])?(/[!-~]*)?\z", RegexOptions.CultureInvariant);

    public static T Parse<T>(ReadOnlySpan<byte> json, Action<T> validate)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(json, Json) ?? throw new JsonException("null");
            validate(value);
            return value;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        { throw new RemoteApiException(RemoteApiException.InvalidResponse, 0); }
    }

    static void Require(bool condition) { if (!condition) throw new JsonException("contract"); }
    static bool PollAfter(int? value) => value is null or (>= 1 and <= 3600);
    static bool Revision(int value) => value is >= 1 and <= 1000;
    static bool All<T>(IReadOnlyList<T>? items, int max, Func<T, bool> valid, int min = 0) => items != null && items.Count >= min && items.Count <= max && items.All(i => i != null && valid(i));

    public static void Validate(Capabilities c)
    {
        Require(Regex.IsMatch(c.ProtocolVersion, @"\A1\.[0-9]{1,3}\z", RegexOptions.CultureInvariant));
        Require(All(c.ReportSchemaVersions, 8, SchemaVersion.IsMatch, 1) && c.ReportSchemaVersions.Distinct().Count() == c.ReportSchemaVersions.Count);
        Require(SchemaVersion.IsMatch(c.ResultSchemaVersion));
        Require(c.MaxUploadBytes is >= 1 and <= 262144 && c.MaxScanAgeSeconds is >= 1 and <= 900 && c.MaxFutureSkewSeconds is >= 0 and <= 300 && c.CaseRetentionDays is >= 1 and <= 7);
        Require(ContractFormat.IsVersionLabel(c.PrivacyNotice.Version) && ContractFormat.IsFreeText(c.PrivacyNotice.Text, 8000));
    }

    public static void Validate(PairingResponse p)
        => Require(ContractFormat.IsUuid(p.ClientId) && Token.IsMatch(p.AccessToken) && ContractFormat.IsTimestamp(p.ExpiresAt));

    public static void Validate(CaseAccepted a)
        => Require(ContractFormat.IsUuid(a.CaseId) && CaseStates.Contains(a.Status) && ContractFormat.IsTimestamp(a.ReceivedAt) && ContractFormat.IsTimestamp(a.ExpiresAt)
                   && ContractFormat.IsSha256(a.ReportSha256) && PollAfter(a.PollAfterSeconds));

    public static void Validate(CaseView c)
    {
        Require(ContractFormat.IsUuid(c.CaseId) && ContractFormat.IsUuid(c.ScanId) && CaseStates.Contains(c.Status) && ContractFormat.IsSha256(c.ReportSha256));
        Require(ContractFormat.IsTimestamp(c.ReceivedAt) && ContractFormat.IsTimestamp(c.ExpiresAt) && PollAfter(c.PollAfterSeconds));
        // The conditional rules of case.schema.json.
        Require((c.Status == "released") == (c.Result != null));
        Require(c.Status == "analysis_failed" ? StatusReasons.Contains(c.StatusReason) : c.StatusReason == null);
        Require((c.Status is "released" or "analysis_failed") == (c.PollAfterSeconds == null));
        if (c.Result != null) Validate(c.Result);
    }

    public static void Validate(CaseResult r)
    {
        static bool Text(string? value, int max) => ContractFormat.IsFreeText(value, max);
        static bool Optional(string? value, int max) => value == null || Text(value, max);
        Require(r.ResultSchemaVersion == "1.0" && ContractFormat.IsUuid(r.CaseId) && ContractFormat.IsUuid(r.ScanId) && ContractFormat.IsSha256(r.ReportSha256));
        Require(Revision(r.Revision) && ContractFormat.IsTimestamp(r.ReleasedAt) && r.Origin is "human" or "ai_assisted_human_reviewed" && Text(r.Summary, 2000));
        Require(All(r.Facts, 50, f => ContractFormat.IsLocalId(f.Id) && Text(f.Text, 1000) && All(f.References, 10, Reference, 1)));
        Require(All(r.Hypotheses, 10, h => Text(h.Text, 1000) && !h.Proven && All(h.FactRefs, 10, ContractFormat.IsLocalId) && h.FactRefs.Distinct().Count() == h.FactRefs.Count));
        Require(All(r.NextSteps, 20, s => Text(s.Title, 200) && Text(s.Rationale, 1000) && Text(s.Instructions, 4000)
            && s.RiskClass is "read_only" or "reversible_change" or "expert_only" && s.RequiredPrivileges is "none" or "standard_user" or "administrator"
            && Text(s.ExpectedOutcome, 1000) && Optional(s.Rollback, 1000) && Optional(s.AbortCondition, 1000)));
        Require(All(r.OpenQuestions, 10, q => Text(q, 1000)) && All(r.Warnings, 20, w => Text(w, 1000)));
        Require(All(r.Sources, 10, s => Text(s.Title, 200) && s.Url.Length <= 500 && SourceUrl.IsMatch(s.Url)));
    }

    static bool Reference(ResultReference reference) => reference.Kind switch
    {
        "finding" or "device" => ContractFormat.IsLocalId(reference.Id) && reference.Pointer == null,
        "report_field" => reference.Id == null && reference.Pointer is { Length: <= 100 } pointer && Pointer.IsMatch(pointer),
        _ => false
    };

    public static void Validate(FeedbackResponse f)
        => Require(ContractFormat.IsUuid(f.FeedbackId) && ContractFormat.IsUuid(f.CaseId) && Revision(f.ResultRevision) && ContractFormat.IsTimestamp(f.ReceivedAt));

    public static void Validate(ErrorBody e, IReadOnlySet<string> knownCodes)
        => Require(knownCodes.Contains(e.Error.Code) && ContractFormat.IsFreeText(e.Error.Message, 300) && ContractFormat.IsUuid(e.RequestId)
                   && e.RetryAfterSeconds is null or (>= 1 and <= 86400));
}

/// <summary>Where the server lives. Only an https origin is accepted; the client appends /api/v1 itself.</summary>
public static class ServerAddress
{
    public static Uri? Parse(string? input, out string error)
    {
        error = "Serveradresse bitte als https://name.beispiel eingeben, ohne Pfad, Benutzername oder Zusätze.";
        var text = (input ?? "").Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0) return null;
        if (uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/") return null;
        error = "";
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }
}

/// <summary>HTTP transport for the six contract operations. Holds no token and no case data.</summary>
public sealed class RemoteApiClient
{
    const int MaxResponseBytes = 512 * 1024;
    readonly HttpClient http;
    readonly Uri origin;
    readonly ContractRules rules;

    public RemoteApiClient(HttpMessageHandler handler, Uri origin, string appVersion, ContractRules rules)
    {
        this.origin = origin; this.rules = rules;
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = MaxResponseBytes };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DriverPilot", appVersion));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>No redirects, cookies, decompression or credential forwarding.</summary>
    public static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(15)
    };

    public Uri Origin => origin;

    public async Task<Capabilities> GetCapabilitiesAsync(CancellationToken cancel = default)
        => ContractResponse.Parse<Capabilities>(await Send(HttpMethod.Get, "capabilities", null, null, null, null, HttpStatusCode.OK, cancel), ContractResponse.Validate);

    public async Task<PairingResponse> RedeemAsync(string invitationCode, CancellationToken cancel = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new { invitation_code = invitationCode });
        return ContractResponse.Parse<PairingResponse>(await Send(HttpMethod.Post, "pairings/redeem", null, null, null, body, HttpStatusCode.Created, cancel), ContractResponse.Validate);
    }

    public async Task<CaseAccepted> CreateCaseAsync(string token, Guid idempotencyKey, byte[] body, CancellationToken cancel = default)
        => ContractResponse.Parse<CaseAccepted>(await Send(HttpMethod.Post, "cases", token, idempotencyKey, null, body, HttpStatusCode.Accepted, cancel), ContractResponse.Validate);

    /// <summary>Returns null for 304: the case is unchanged since <paramref name="etag"/>.</summary>
    public async Task<(CaseView? Case, string? ETag)> GetCaseAsync(string token, string caseId, string? etag, CancellationToken cancel = default)
    {
        using var response = await Exchange(HttpMethod.Get, "cases/" + caseId, token, null, etag, null, cancel);
        if (response.StatusCode == HttpStatusCode.NotModified) return (null, etag);
        var json = await Body(response, HttpStatusCode.OK, cancel);
        return (ContractResponse.Parse<CaseView>(json, ContractResponse.Validate), response.Headers.ETag?.ToString());
    }

    public async Task<FeedbackResponse> SendFeedbackAsync(string token, string caseId, Guid idempotencyKey, byte[] body, CancellationToken cancel = default)
        => ContractResponse.Parse<FeedbackResponse>(await Send(HttpMethod.Post, $"cases/{caseId}/feedback", token, idempotencyKey, null, body, HttpStatusCode.Created, cancel), ContractResponse.Validate);

    public async Task DeleteCaseAsync(string token, string caseId, CancellationToken cancel = default)
    {
        using var response = await Exchange(HttpMethod.Delete, "cases/" + caseId, token, null, null, null, cancel);
        if (response.StatusCode != HttpStatusCode.NoContent) throw await Failure(response, cancel);
    }

    async Task<byte[]> Send(HttpMethod method, string path, string? token, Guid? idempotencyKey, string? etag, byte[]? body, HttpStatusCode expected, CancellationToken cancel)
    {
        using var response = await Exchange(method, path, token, idempotencyKey, etag, body, cancel);
        return await Body(response, expected, cancel);
    }

    async Task<HttpResponseMessage> Exchange(HttpMethod method, string path, string? token, Guid? idempotencyKey, string? etag, byte[]? body, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(method, new Uri(origin, "api/v1/" + path));
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey.HasValue) request.Headers.TryAddWithoutValidation("Idempotency-Key", ContractFormat.Uuid(idempotencyKey.Value));
        if (etag != null) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        if (body != null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }
        try { return await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancel); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.IO.IOException)
        {
            if (cancel.IsCancellationRequested) throw;
            throw new RemoteApiException(RemoteApiException.Network, 0);
        }
    }

    async Task<byte[]> Body(HttpResponseMessage response, HttpStatusCode expected, CancellationToken cancel)
    {
        if (response.StatusCode != expected) throw await Failure(response, cancel);
        if (response.Content.Headers.ContentType?.MediaType != "application/json") throw new RemoteApiException(RemoteApiException.InvalidResponse, (int)response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync(cancel);
    }

    async Task<RemoteApiException> Failure(HttpResponseMessage response, CancellationToken cancel)
    {
        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400) return new(RemoteApiException.Redirect, status);
        int? retryAfter = response.Headers.TryGetValues("Retry-After", out var values) && int.TryParse(values.FirstOrDefault(), out var seconds) && seconds is >= 1 and <= 86400 ? seconds : null;
        if (status >= 400 && response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            try
            {
                var error = ContractResponse.Parse<ErrorBody>(await response.Content.ReadAsByteArrayAsync(cancel), e => ContractResponse.Validate(e, rules.ErrorCodes));
                return new(error.Error.Code, status, error.RetryAfterSeconds ?? retryAfter, error.Error.Message);
            }
            catch (RemoteApiException) { }
        }
        // Not a contract error body, for example a proxy page: fall back to the status code alone.
        return new(status switch { 401 => "token_invalid", 404 => "not_found", 429 => "rate_limited", >= 500 => "service_unavailable", _ => RemoteApiException.InvalidResponse }, status, retryAfter);
    }
}
