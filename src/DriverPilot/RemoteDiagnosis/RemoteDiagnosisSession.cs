using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace DriverPilot.RemoteDiagnosis;

public sealed record RemoteAccess(string Origin, string ClientId, string ProtectedToken, string ExpiresAt);
public sealed record RemoteCaseRef(string CaseId, string ScanId, string ReportSha256, string ScannedAt, string ExpiresAt);
public sealed record RemoteState(RemoteAccess? Access = null, RemoteCaseRef? Case = null);

public interface IRemoteStateStore { RemoteState Load(); void Save(RemoteState state); }
/// <summary>Binds the token to the Windows user. Unprotect returns null when the value cannot be read.</summary>
public interface ISecretProtector { string Protect(string secret); string? Unprotect(string protectedValue); }

/// <summary>
/// Supervised remote diagnosis without UI, timers or background work: every network call is one explicit method call.
/// Not thread-safe; the owner serializes calls.
/// </summary>
public sealed class RemoteDiagnosisSession
{
    readonly IRemoteStateStore store;
    readonly ISecretProtector protector;
    readonly Func<Uri, RemoteApiClient> connect;
    readonly Func<DateTimeOffset> clock;
    readonly ContractRules rules;
    readonly string appVersion;
    RemoteState state;
    RemoteApiClient? api;
    string? token;
    RemoteReport? draft;
    (byte[] Body, Guid Key, RemoteCaseRef Ref)? pendingUpload;
    (byte[] Body, Guid Key)? pendingFeedback;
    string? etag;
    int failures;
    int? retryAfter;

    public string Status { get; private set; } = "";
    public Capabilities? Capabilities { get; private set; }
    public string? Preview { get; private set; }
    public CaseView? Case { get; private set; }
    public RemoteCaseRef? CaseRef => state.Case;
    public bool Paired => token != null && api != null;
    public string? ServerHost => api?.Origin.Host;
    public bool CanSend => Paired && Capabilities != null && (draft != null || pendingUpload != null);
    /// <summary>A sent body awaits a retry with identical bytes and key.</summary>
    public bool HasPendingUpload => pendingUpload != null;

    public RemoteDiagnosisSession(IRemoteStateStore store, ISecretProtector protector, Func<Uri, RemoteApiClient> connect, Func<DateTimeOffset> clock, ContractRules rules, string appVersion)
    {
        this.store = store; this.protector = protector; this.connect = connect; this.clock = clock; this.rules = rules; this.appVersion = appVersion;
        state = store.Load();
        RestoreAccess();
    }

    void RestoreAccess()
    {
        var access = state.Access;
        if (access == null) return;
        var origin = ServerAddress.Parse(access.Origin, out _);
        var secret = protector.Unprotect(access.ProtectedToken);
        if (origin == null || secret == null || !ContractFormat.IsTimestamp(access.ExpiresAt) || ContractFormat.ParseTimestamp(access.ExpiresAt) <= clock())
        { Forget("Der gespeicherte Zugang ist abgelaufen oder nicht mehr lesbar. Bitte Christian um eine neue Einladung bitten."); return; }
        api = connect(origin); token = secret;
    }

    void Save(RemoteState next) { state = next; store.Save(next); }

    void Forget(string status)
    {
        api = null; token = null; Capabilities = null; Case = null; etag = null; pendingUpload = null; pendingFeedback = null;
        Save(new RemoteState());
        Status = status;
    }

    public async Task<bool> PairAsync(string serverAddress, string invitationCode)
    {
        var origin = ServerAddress.Parse(serverAddress, out var error);
        if (origin == null) { Status = error; return false; }
        var code = (invitationCode ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, @"\A[A-Za-z0-9_-]{22,128}\z")) { Status = "Der Einladungscode hat nicht das erwartete Format. Bitte vollständig und ohne Leerzeichen einfügen."; return false; }
        var candidate = connect(origin);
        return await Run(async () =>
        {
            var pairing = await candidate.RedeemAsync(code);
            api = candidate; token = pairing.AccessToken; Capabilities = null; Case = null; etag = null;
            Save(new RemoteState(new RemoteAccess(origin.AbsoluteUri, pairing.ClientId, protector.Protect(pairing.AccessToken), pairing.ExpiresAt)));
            Status = $"Verbunden mit {origin.Host}. Der Zugang gilt bis {Local(pairing.ExpiresAt)}.";
        });
    }

    /// <summary>Removes the local access only; cases already on the server expire on their own unless deleted first.</summary>
    public void Unpair() => Forget("Zugang auf diesem PC entfernt.");

    public async Task<bool> LoadCapabilitiesAsync()
    {
        Capabilities? loaded = null;
        if (!await Run(async () => loaded = await Api().GetCapabilitiesAsync())) return false;
        if (!loaded!.ReportSchemaVersions.Contains(ContractRules.ReportSchemaVersion))
        {
            Capabilities = null; draft = null; Preview = null; pendingUpload = null;
            Status = "Der Server erwartet ein anderes Berichtsformat. Bitte DriverPilot aktualisieren.";
            return false;
        }
        // A changed notice invalidates what the user has seen so far.
        if (Capabilities != null && Capabilities.PrivacyNotice.Version != loaded.PrivacyNotice.Version) { draft = null; Preview = null; pendingUpload = null; }
        Capabilities = loaded;
        return true;
    }

    /// <summary>Builds the preview from a fresh scan. Any change discards an earlier consent and its frozen bytes.</summary>
    public bool Prepare(Snapshot snapshot, Guid scanId, DateTimeOffset scannedAt, SymptomInput input)
    {
        draft = null; Preview = null; pendingUpload = null;
        if (!Fresh(scannedAt)) { Status = "Der Scan ist zu alt. Bitte den Dialog schließen und 'Jetzt prüfen' ausführen."; return false; }
        try
        {
            draft = RemoteReportBuilder.Build(snapshot, scanId, scannedAt, appVersion, input, state.Case?.CaseId, rules);
            Preview = RemoteReportBuilder.Preview(draft);
            Status = "Vorschau neu erzeugt. Bitte vollständig prüfen.";
            return true;
        }
        catch (ReportInputException ex) { Status = ex.Message; return false; }
    }

    bool Fresh(DateTimeOffset scannedAt)
    {
        var age = clock() - scannedAt;
        return age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(Math.Min(rules.MaxScanAgeSeconds, Capabilities?.MaxScanAgeSeconds ?? rules.MaxScanAgeSeconds));
    }

    /// <summary>
    /// The user's explicit consent click. The first call freezes body and idempotency key;
    /// after a transient failure the next call resends exactly those bytes.
    /// </summary>
    public async Task<bool> ApproveAndSendAsync(bool externalAiAllowed)
    {
        if (!Paired) { Status = "Bitte zuerst mit einer Einladung verbinden."; return false; }
        if (pendingUpload == null)
        {
            if (draft == null || Capabilities == null) { Status = "Keine gültige Vorschau. Bitte Angaben prüfen."; return false; }
            if (!Fresh(ContractFormat.ParseTimestamp(draft.ScannedAt))) { draft = null; Preview = null; Status = "Der Scan ist inzwischen zu alt. Bitte erneut prüfen und die Vorschau neu ansehen."; return false; }
            var consent = new ReportConsent(Capabilities.PrivacyNotice.Version, ContractFormat.Timestamp(clock()), true, externalAiAllowed && Capabilities.ExternalAiOffered);
            var body = RemoteReportBuilder.Freeze(draft, consent);
            if (body.Length > Math.Min(rules.MaxUploadBytes, Capabilities.MaxUploadBytes)) { Status = "Der Bericht ist zu groß und wurde nicht gesendet."; return false; }
            var sha = Convert.ToHexStringLower(SHA256.HashData(body));
            pendingUpload = (body, Guid.NewGuid(), new RemoteCaseRef("", draft.ScanId, sha, draft.ScannedAt, ""));
            draft = null;
        }
        var upload = pendingUpload.Value;
        var sent = await Run(async () =>
        {
            var accepted = await Api().CreateCaseAsync(token!, upload.Key, upload.Body);
            if (accepted.ReportSha256 != upload.Ref.ReportSha256) throw new RemoteApiException(RemoteApiException.InvalidResponse, 0);
            pendingUpload = null; pendingFeedback = null; etag = null; Case = null;
            Save(state with { Case = upload.Ref with { CaseId = accepted.CaseId, ExpiresAt = accepted.ExpiresAt } });
            Status = $"Bericht angenommen. Christian prüft ihn; das Ergebnis erscheint hier. Der Fall wird am {Local(accepted.ExpiresAt)} gelöscht.";
        });
        // Only a transient failure keeps the frozen request; anything else needs a new preview and consent.
        if (!sent && !lastFailureTransient) { pendingUpload = null; Preview = null; }
        return sent;
    }

    public Task<bool> RefreshAsync()
    {
        var reference = state.Case;
        if (reference == null) { Status = "Es gibt keinen offenen Fall."; return Task.FromResult(false); }
        return Run(async () =>
        {
            var (view, tag) = await Api().GetCaseAsync(token!, reference.CaseId, etag);
            if (view == null) return;
            var bound = view.CaseId == reference.CaseId && view.ScanId == reference.ScanId && view.ReportSha256 == reference.ReportSha256
                        && (view.Result == null || (view.Result.CaseId == reference.CaseId && view.Result.ScanId == reference.ScanId && view.Result.ReportSha256 == reference.ReportSha256));
            if (!bound) throw new RemoteApiException(RemoteApiException.InvalidResponse, 0);
            Case = view; etag = tag;
            Status = CaseStatusText(view);
        }, caseRequest: true);
    }

    public async Task<bool> SendFeedbackAsync(string outcome, string? note)
    {
        var reference = state.Case;
        if (reference == null || Case?.Result == null) { Status = "Eine Rückmeldung ist erst nach einem freigegebenen Ergebnis möglich."; return false; }
        byte[] body;
        try { body = JsonSerializer.SerializeToUtf8Bytes(new FeedbackRequest(outcome, RemoteReportBuilder.FreeText(note, "Notiz", rules), Case.Result.Revision), RemoteReportBuilder.Json); }
        catch (ReportInputException ex) { Status = ex.Message; return false; }
        // Same content after a failure reuses the key; changed content is a new request.
        if (pendingFeedback == null || !pendingFeedback.Value.Body.AsSpan().SequenceEqual(body)) pendingFeedback = (body, Guid.NewGuid());
        var feedback = pendingFeedback.Value;
        var sent = await Run(async () =>
        {
            await Api().SendFeedbackAsync(token!, reference.CaseId, feedback.Key, feedback.Body);
            pendingFeedback = null;
            Status = "Rückmeldung gesendet. Danke.";
        }, caseRequest: true);
        if (!sent && !lastFailureTransient) pendingFeedback = null;
        return sent;
    }

    public Task<bool> DeleteCaseAsync()
    {
        var reference = state.Case;
        if (reference == null) { Status = "Es gibt keinen offenen Fall."; return Task.FromResult(false); }
        return Run(async () =>
        {
            await Api().DeleteCaseAsync(token!, reference.CaseId);
            DropCase("Der Fall wurde auf dem Server gelöscht.");
        }, caseRequest: true);
    }

    void DropCase(string status)
    {
        Case = null; etag = null; pendingFeedback = null;
        Save(state with { Case = null });
        Status = status;
    }

    /// <summary>Delay before the next automatic refresh, or null when polling must stop.</summary>
    public TimeSpan? NextPoll(double jitter)
    {
        if (!Paired || state.Case == null) return null;
        if (Case != null && Case.PollAfterSeconds == null && failures == 0) return null;
        var seconds = (double)Math.Max(rules.PollInitialSeconds, Case?.PollAfterSeconds ?? rules.PollInitialSeconds);
        if (failures > 0) seconds = Math.Min(rules.PollMaxBackoffSeconds, rules.PollInitialSeconds * Math.Pow(2, failures)) * (1 + 0.2 * Math.Clamp(jitter, 0, 1));
        return TimeSpan.FromSeconds(Math.Max(seconds, retryAfter ?? 0));
    }

    /// <summary>True when the case belongs to an older scan than the one currently on screen.</summary>
    public bool IsCaseForOlderScan(DateTimeOffset? currentScanAt)
        => state.Case != null && currentScanAt.HasValue && ContractFormat.ParseTimestamp(state.Case.ScannedAt) < currentScanAt.Value.AddSeconds(-1);

    /// <summary>Offline fallback: the exact preview text goes to a place the user picks.</summary>
    public bool Export(Func<string, bool?> save)
    {
        if (Preview == null) { Status = "Keine gültige Vorschau. Bitte Angaben prüfen."; return false; }
        try
        {
            var result = save(Preview);
            Status = result == true ? "Vorschau unverändert lokal gespeichert. Kein Upload." : result == null ? "Speichern abgebrochen. Keine Datei geschrieben." : "Speichern fehlgeschlagen. Anderen Speicherort wählen.";
            return result == true;
        }
        catch { Status = "Speichern fehlgeschlagen. Anderen Speicherort wählen."; return false; }
    }

    RemoteApiClient Api() => api ?? throw new RemoteApiException("token_invalid", 401);
    bool lastFailureTransient;

    async Task<bool> Run(Func<Task> action, bool caseRequest = false)
    {
        try
        {
            await action();
            failures = 0; retryAfter = null; lastFailureTransient = false;
            return true;
        }
        catch (RemoteApiException ex)
        {
            lastFailureTransient = ex.Transient;
            failures++; retryAfter = ex.RetryAfterSeconds;
            if (ex.Code == "token_invalid") Forget(RemoteText.Error(ex));
            else if (caseRequest && ex.Code == "not_found") DropCase("Der Fall ist auf dem Server nicht mehr vorhanden (gelöscht oder abgelaufen).");
            else Status = RemoteText.Error(ex);
            return false;
        }
    }

    static string CaseStatusText(CaseView view) => view.Status switch
    {
        "queued" => "Der Bericht wartet auf die Bearbeitung.",
        "analyzing" => "Der Bericht wird gerade ausgewertet.",
        "awaiting_review" => "Christian prüft den Fall. Es ist noch nichts freigegeben.",
        "released" => $"Ergebnis freigegeben (Revision {view.Result!.Revision}).",
        _ => "Die automatische Auswertung ist fehlgeschlagen. Christian kann den Fall trotzdem manuell bearbeiten; später erneut aktualisieren."
    };

    static string Local(string timestamp) => ContractFormat.ParseTimestamp(timestamp).ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}

/// <summary>German texts for the user. Server texts are shown only for codes without an own text, as plain text.</summary>
public static class RemoteText
{
    public static string Error(RemoteApiException ex) => ex.Code switch
    {
        RemoteApiException.Network => "Server nicht erreichbar. Internetverbindung prüfen und später erneut versuchen. Der Bericht kann auch lokal gespeichert werden.",
        RemoteApiException.Redirect => "Der Server hat eine Weiterleitung geantwortet. Aus Sicherheitsgründen wird ihr nicht gefolgt. Bitte Serveradresse prüfen.",
        RemoteApiException.InvalidResponse => "Der Server hat eine unerwartete Antwort gesendet. Sie wird nicht angezeigt. Bitte Christian informieren.",
        "token_invalid" => "Der Zugang ist ungültig oder abgelaufen. Bitte Christian um eine neue Einladung bitten.",
        "invitation_invalid" => "Die Einladung ist ungültig, abgelaufen oder bereits verwendet. Bitte Christian um eine neue Einladung bitten.",
        "not_found" => "Nicht vorhanden.",
        "idempotency_key_conflict" or "request_retired" => "Diese Anfrage kann nicht wiederholt werden. Bitte die Vorschau neu erzeugen.",
        "result_revision_conflict" => "Das Ergebnis wurde inzwischen geändert. Bitte aktualisieren und die Rückmeldung erneut senden.",
        "feedback_limit_reached" => "Für diesen Fall sind keine weiteren Rückmeldungen möglich.",
        "payload_too_large" => "Der Bericht ist zu groß.",
        "privacy_notice_outdated" => "Der Datenschutzhinweis wurde geändert. Bitte die Vorschau neu erzeugen und den neuen Hinweis lesen.",
        "scan_too_old" => "Der Scan ist zu alt. Bitte erneut prüfen und die Vorschau neu ansehen.",
        "clock_skew" => "Die Uhrzeit dieses PCs weicht zu stark ab. Bitte Datum und Uhrzeit in Windows prüfen.",
        "text_rejected" => "Ein Textfeld enthält möglicherweise persönliche Daten, Pfade oder Zugangsdaten. Bitte ohne solche Angaben formulieren.",
        "consent_required" => "Ohne Zustimmung wird nichts gesendet.",
        "rate_limited" => Wait("Zu viele Anfragen.", ex),
        "daily_case_limit_reached" => Wait("Für heute sind keine weiteren Fälle möglich.", ex),
        "capacity_exhausted" => Wait("Der Server nimmt gerade keine neuen Fälle an.", ex),
        "service_unavailable" or "internal_error" => Wait("Der Server ist gerade nicht bereit. Es wurde nichts gespeichert.", ex),
        _ => ex.ServerMessage is { Length: > 0 } message ? message : "Der Bericht wurde vom Server abgelehnt. Bitte DriverPilot aktualisieren oder Christian informieren."
    };

    static string Wait(string text, RemoteApiException ex)
        => ex.RetryAfterSeconds is { } seconds ? $"{text} Frühestens in {Math.Max(1, (seconds + 59) / 60)} Minute(n) erneut versuchen." : text + " Bitte später erneut versuchen.";

    public static string Category(string code) => code switch
    {
        "wow_crash" => "World of Warcraft stürzt ab", "pc_freeze" => "PC friert ein", "graphics_reset" => "Bild setzt aus / Grafiktreiber startet neu",
        "performance" => "Ruckeln oder schlechte Leistung", "network" => "Verbindungsprobleme", "addon_error" => "Addon-Fehler", _ => "Etwas anderes"
    };

    /// <summary>Plain text only. Nothing in a result is ever interpreted, linked or executed.</summary>
    public static string Result(CaseResult result, string scannedAt)
    {
        var text = new StringBuilder();
        void Section(string title) => text.Append('\n').Append(title.ToUpperInvariant()).Append('\n');
        text.Append($"Ergebnis Revision {result.Revision} · freigegeben am {Time(result.ReleasedAt)} · Scan vom {Time(scannedAt)}\n");
        text.Append(result.Origin == "human" ? "Von Christian erstellt.\n" : "KI-Entwurf, von Christian geprüft und freigegeben.\n");
        Section("Zusammenfassung"); text.Append(result.Summary).Append('\n');
        if (result.Warnings.Count > 0) { Section("Wichtig"); foreach (var warning in result.Warnings) text.Append("! ").Append(warning).Append('\n'); }
        if (result.Facts.Count > 0) { Section("Beobachtete Fakten"); foreach (var fact in result.Facts) text.Append("• ").Append(fact.Text).Append('\n'); }
        if (result.Hypotheses.Count > 0) { Section("Vermutungen (nicht bewiesen)"); foreach (var hypothesis in result.Hypotheses) text.Append("? ").Append(hypothesis.Text).Append('\n'); }
        if (result.NextSteps.Count > 0) Section("Nächste Schritte (von dir selbst auszuführen)");
        for (var i = 0; i < result.NextSteps.Count; i++)
        {
            var step = result.NextSteps[i];
            text.Append($"\n{i + 1}. {step.Title}\n   Risiko: {Risk(step.RiskClass)} · Rechte: {Privileges(step.RequiredPrivileges)}\n");
            text.Append("   Warum: ").Append(step.Rationale).Append("\n   Anleitung: ").Append(step.Instructions).Append("\n   Erwartet: ").Append(step.ExpectedOutcome).Append('\n');
            if (step.Rollback != null) text.Append("   Rückweg: ").Append(step.Rollback).Append('\n');
            if (step.AbortCondition != null) text.Append("   Abbrechen, wenn: ").Append(step.AbortCondition).Append('\n');
        }
        if (result.OpenQuestions.Count > 0) { Section("Rückfragen an dich"); foreach (var question in result.OpenQuestions) text.Append("• ").Append(question).Append('\n'); }
        if (result.Sources.Count > 0) { Section("Quellen (nicht automatisch geöffnet)"); foreach (var source in result.Sources) text.Append("• ").Append(source.Title).Append(": ").Append(source.Url).Append('\n'); }
        return text.ToString();
    }

    static string Time(string timestamp) => ContractFormat.ParseTimestamp(timestamp).ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    static string Risk(string value) => value switch { "read_only" => "nur lesen", "reversible_change" => "umkehrbare Änderung", _ => "nur mit Betreuung" };
    static string Privileges(string value) => value switch { "none" => "keine", "standard_user" => "normales Benutzerkonto", _ => "Administrator" };
}
