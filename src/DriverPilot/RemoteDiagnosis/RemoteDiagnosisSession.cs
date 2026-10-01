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
    bool uploadAttempted;
    bool finalPreview;
    public bool HasFinalPreview => finalPreview && Preview != null;
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
    public string Disclosure
    {
        get
        {
            var endpoint = api == null ? "Noch kein Empfänger verbunden – kein Upload möglich." : new Uri(api.Origin, "api/v1/cases").AbsoluteUri;
            var source = api == null ? "Noch nicht geladen" : new Uri(api.Origin, "api/v1/capabilities").AbsoluteUri;
            return $"Empfänger: Christian über POST {endpoint}\n"
                + $"Inhalt: Problemtext und Schritte, Windows-/Hardware-/BIOS-Daten, {draft?.Devices.Count ?? 0} Geräte (Modellkennungen, Treiber, Problemcodes), {draft?.Findings.Count ?? 0} Befunde (Ereignisgruppen, keine Rohmeldungen), Erfassungsstatus, optional WoW-Angaben. "
                + "Dazu: App-/Schema-Version, Scan-ID, Scan- und Beobachtungszeiten, ggf. vorherige Fall-ID und consent (Hinweisversion, Zustimmungszeit in UTC, Upload-Zustimmung, KI-Wahl). Alle JSON-Felder stehen unten.\n"
                + $"Aufbewahrung laut Server: {(Capabilities == null ? "unbekannt" : Capabilities.CaseRetentionDays + " Tage")}. Hinweisversion: {Capabilities?.PrivacyNotice.Version ?? "unbekannt"}. Quelle des angezeigten Hinweises: {source}. Serverangabe, keine unabhängige Datenschutzprüfung.\n"
                + "Nicht ausgewählt: rohe Ereignistexte, vollständige Geräteinstanz-IDs, Chat, Einstellungen und gespeicherte Zugangsdaten. Filter sind keine Garantie für vollständige Anonymisierung: eigene Texte und die gesamte Vorschau selbst prüfen.\n"
                + "HTTPS verschlüsselt den Transport, verbirgt aber nicht die Verbindungs-IP vor Server bzw. beteiligtem Proxy. Zur Anmeldung wird ein geheimes Bearer-Token im Authorization-Header gesendet; sein Wert wird hier und im Export NICHT offengelegt. Hinzu kommen Idempotency-Key, DriverPilot-Version (User-Agent), JSON-Inhaltstyp und übliche HTTP-/TLS-Verbindungsmetadaten. Dies sind keine zusätzlichen Diagnosefelder.\n"
                + "Verbinden überträgt den Einladungscode; Hinweis-/Statusabfragen senden keinen Diagnosebericht. Statusabfragen eines vorhandenen Falls laufen bei offenem Dialog automatisch. Berichte und Wiederholungen nur nach ausdrücklichem Senden-Klick. Abbrechen kann eine bereits erfolgte Annahme nicht rückgängig machen.";
        }
    }
    public bool CanSend => Paired && Capabilities != null && HasFinalPreview && pendingUpload != null;
    /// <summary>A sent body awaits a retry with identical bytes and key.</summary>
    public bool HasPendingUpload => pendingUpload != null && uploadAttempted;

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
        {
            try { Forget("Der gespeicherte Zugang ist abgelaufen oder nicht mehr lesbar. Bitte Christian um eine neue Einladung bitten."); }
            catch (LocalPersistenceException) { PersistenceFailure(); }
            return;
        }
        api = connect(origin); token = secret;
    }

    sealed class LocalPersistenceException : Exception { }
    void Save(RemoteState next)
    {
        try { store.Save(next); }
        catch { throw new LocalPersistenceException(); }
        state = next;
    }

    public void ReportUnexpectedFailure()
    {
        lastFailureTransient = true;
        Status = "Vorgang nicht abgeschlossen. Eine Serveraktion kann bereits erfolgt sein. Geprüfte Anfragen bleiben für eine ausdrückliche Wiederholung erhalten. Keine technischen Fehlerdetails werden angezeigt.";
    }

    void PersistenceFailure()
    {
        lastFailureTransient = true;
        Status = "Lokales Speichern fehlgeschlagen. Eine Serveraktion kann bereits erfolgt sein; eine Annahme wird dadurch nicht rückgängig gemacht. Dialog offen lassen und unverändert erneut versuchen, sobald lokal gespeichert werden kann.";
    }

    void Forget(string status)
    {
        Save(new RemoteState());
        Invalidate();
        api = null; token = null; Capabilities = null; Case = null; etag = null;
        Status = status;
    }

    public async Task<bool> PairAsync(string serverAddress, string invitationCode)
    {
        Invalidate();
        var origin = ServerAddress.Parse(serverAddress, out var error);
        if (origin == null) { Status = error; return false; }
        var code = (invitationCode ?? "").Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, @"\A[A-Za-z0-9_-]{22,128}\z")) { Status = "Der Einladungscode hat nicht das erwartete Format. Bitte vollständig und ohne Leerzeichen einfügen."; return false; }
        var candidate = connect(origin);
        return await Run(async () =>
        {
            var pairing = await candidate.RedeemAsync(code);
            Save(new RemoteState(new RemoteAccess(origin.AbsoluteUri, pairing.ClientId, protector.Protect(pairing.AccessToken), pairing.ExpiresAt)));
            api = candidate; token = pairing.AccessToken; Capabilities = null; Case = null; etag = null;
            Status = $"Verbunden mit {origin.Host}. Der Zugang gilt bis {Local(pairing.ExpiresAt)}.";
        });
    }

    /// <summary>Removes the local access only; cases already on the server expire on their own unless deleted first.</summary>
    public void Unpair()
    {
        try { Forget("Zugang auf diesem PC entfernt."); }
        catch (LocalPersistenceException) { PersistenceFailure(); }
        catch { ReportUnexpectedFailure(); }
    }

    public async Task<bool> LoadCapabilitiesAsync()
    {
        Capabilities? loaded = null;
        if (!await Run(async () => loaded = await Api().GetCapabilitiesAsync())) { Invalidate(); Capabilities = null; return false; }
        if (!loaded!.ReportSchemaVersions.Contains(ContractRules.ReportSchemaVersion))
        {
            Capabilities = null; Invalidate();
            Status = "Der Server erwartet ein anderes Berichtsformat. Bitte DriverPilot aktualisieren.";
            return false;
        }
        // A changed notice invalidates what the user has seen so far.
        if (Capabilities != null && JsonSerializer.Serialize(Capabilities) != JsonSerializer.Serialize(loaded)) Invalidate();
        Capabilities = loaded;
        return true;
    }

    /// <summary>Builds the preview from a fresh scan. Any change discards an earlier consent and its frozen bytes.</summary>
    public bool Prepare(Snapshot snapshot, Guid scanId, DateTimeOffset scannedAt, SymptomInput input)
    {
        Invalidate();
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

    /// <summary>Consent is prepared locally. No network; the full immutable body must be reviewed before SendReviewedAsync.</summary>
    public bool Review(bool externalAiAllowed)
    {
        if (!Paired) { Status = "Bitte zuerst mit einer Einladung verbinden."; return false; }
        if (pendingUpload != null)
        {
            if (draft?.Consent?.ExternalAiAllowed == (externalAiAllowed && Capabilities?.ExternalAiOffered == true)) return true;
            Invalidate(); Status = "KI-Wahl geändert. Bericht und Zustimmung bitte neu vorbereiten."; return false;
        }
        {
            if (draft == null || Capabilities == null) { Status = "Keine gültige Vorschau. Bitte Angaben prüfen."; return false; }
            if (!Fresh(ContractFormat.ParseTimestamp(draft.ScannedAt))) { draft = null; Preview = null; Status = "Der Scan ist inzwischen zu alt. Bitte erneut prüfen und die Vorschau neu ansehen."; return false; }
            var consent = new ReportConsent(Capabilities.PrivacyNotice.Version, ContractFormat.Timestamp(clock()), true, externalAiAllowed && Capabilities.ExternalAiOffered);
            draft = draft with { Consent = consent };
            var body = RemoteReportBuilder.Freeze(draft, consent);
            if (body.Length > Math.Min(rules.MaxUploadBytes, Capabilities.MaxUploadBytes)) { Status = "Der Bericht ist zu groß und wurde nicht gesendet."; return false; }
            var sha = Convert.ToHexStringLower(SHA256.HashData(body));
            pendingUpload = (body, Guid.NewGuid(), new RemoteCaseRef("", draft.ScanId, sha, draft.ScannedAt, ""));
            Preview = Encoding.UTF8.GetString(body); finalPreview = true;
            uploadAttempted = false;
            Status = "Zustimmung vorbereitet, noch nichts gesendet. Endgültiges JSON einschließlich consent prüfen; erst danach ausdrücklich senden.";
            return true;
        }
    }

    /// <summary>Only sends the already displayed bytes. Retries preserve body and key, including the reviewed AI choice.</summary>
    public async Task<bool> SendReviewedAsync(CancellationToken cancel = default)
    {
        if (!CanSend) { Status = "Keine endgültige Vorschau. Erst Vorschau prüfen / Zustimmung wählen."; return false; }
        if (!uploadAttempted && !Fresh(ContractFormat.ParseTimestamp(pendingUpload!.Value.Ref.ScannedAt)))
        { Invalidate(); Status = "Der Scan ist inzwischen zu alt. Bitte erneut prüfen und die Vorschau neu ansehen."; return false; }
        var upload = pendingUpload!.Value;
        uploadAttempted = true;
        var sent = await Run(async () =>
        {
            var accepted = await Api().CreateCaseAsync(token!, upload.Key, upload.Body, cancel);
            if (accepted.ReportSha256 != upload.Ref.ReportSha256) throw new RemoteApiException(RemoteApiException.InvalidResponse, 0);
            Save(state with { Case = upload.Ref with { CaseId = accepted.CaseId, ExpiresAt = accepted.ExpiresAt } });
            pendingUpload = null; pendingFeedback = null; etag = null; Case = null;
            Status = $"Bericht angenommen. Christian prüft ihn; das Ergebnis erscheint hier. Der Fall wird am {Local(accepted.ExpiresAt)} gelöscht.";
        });
        // Only a transient failure keeps the frozen request; anything else needs a new preview and consent.
        if (!sent && !lastFailureTransient) { pendingUpload = null; Preview = null; }
        return sent;
    }

    public void Invalidate() { draft = null; Preview = null; pendingUpload = null; pendingFeedback = null; uploadAttempted = false; finalPreview = false; }

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

    public bool CanReviewFeedback => Paired && Capabilities != null && state.Case != null && Case?.Result != null;
    public string? FeedbackPreview => pendingFeedback is { } value ? Encoding.UTF8.GetString(value.Body) : null;
    public string? FeedbackEndpoint => api != null && state.Case != null ? new Uri(api.Origin, "api/v1/cases/" + state.Case.CaseId + "/feedback").AbsoluteUri : null;
    public void InvalidateFeedback() => pendingFeedback = null;
    public bool ReviewFeedback(string outcome, string? note)
    {
        if (!CanReviewFeedback) { Status = "Rückmeldung benötigt einen geladenen Datenschutzhinweis und eine freigegebene Fallrevision."; return false; }
        if (outcome is not ("improved" or "unchanged" or "worse" or "not_tried")) { Status = "Bitte eine gültige Rückmeldung wählen."; return false; }
        try
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(new FeedbackRequest(outcome, RemoteReportBuilder.FreeText(note, "Notiz", rules), Case!.Result!.Revision), RemoteReportBuilder.Json);
            if (pendingFeedback == null || !pendingFeedback.Value.Body.AsSpan().SequenceEqual(body))
                pendingFeedback = (body, Guid.NewGuid());
            Status = "Rückmeldung vollständig prüfen (einschließlich Ergebnisrevision), dann separat senden. Noch kein Upload.";
            return true;
        }
        catch (ReportInputException ex) { Status = ex.Message; return false; }
    }

    public async Task<bool> SendFeedbackAsync(string outcome, string? note)
    {
        var reference = state.Case;
        if (!CanReviewFeedback || reference == null) { Status = "Eine Rückmeldung ist erst nach einem freigegebenen Ergebnis möglich."; return false; }
        byte[] body;
        try { body = JsonSerializer.SerializeToUtf8Bytes(new FeedbackRequest(outcome, RemoteReportBuilder.FreeText(note, "Notiz", rules), Case!.Result!.Revision), RemoteReportBuilder.Json); }
        catch (ReportInputException ex) { Status = ex.Message; return false; }
        // Changes, including a newly released revision, require a new visible review.
        if (pendingFeedback == null || !pendingFeedback.Value.Body.AsSpan().SequenceEqual(body))
        { pendingFeedback = null; Status = "Rückmeldung erst vollständig prüfen und separat senden."; return false; }
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
        Save(state with { Case = null });
        Case = null; etag = null; pendingFeedback = null;
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
        // Covers action failures AND persistence failures in the API-error cleanup branches.
        try { return await RunCore(action, caseRequest); }
        catch (LocalPersistenceException) { PersistenceFailure(); return false; }
        catch { ReportUnexpectedFailure(); return false; }
    }

    async Task<bool> RunCore(Func<Task> action, bool caseRequest)
    {
        try
        {
            await action();
            failures = 0; retryAfter = null; lastFailureTransient = false;
            return true;
        }
        catch (OperationCanceledException)
        {
            lastFailureTransient = true;
            Status = "Abgebrochen. Der Server könnte den Bericht bereits angenommen haben. Kein automatischer neuer Upload; bei Wiederholung bleiben die geprüften Bytes unverändert.";
            return false;
        }
        catch (RemoteApiException ex)
        {
            lastFailureTransient = ex.Transient;
            failures++; retryAfter = ex.RetryAfterSeconds;
            if (ex.Code == "token_invalid")
            {
                // Revocation is authoritative even if removing the stale disk credential fails.
                Invalidate(); token = null; api = null; Capabilities = null; Case = null; etag = null;
                Forget(RemoteText.Error(ex));
            }
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

/// <summary>German error texts are local; arbitrary server errors are never echoed. Results remain inert plain text.</summary>
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
        "service_unavailable" or "internal_error" => Wait("Der Server ist gerade nicht bereit. Eine gesendete Anfrage wurde möglicherweise bereits angenommen. Wiederholen nur ausdrücklich mit unveränderten geprüften Daten.", ex),
        _ => "Der Bericht wurde vom Server abgelehnt. Bitte DriverPilot aktualisieren oder Christian informieren."
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
