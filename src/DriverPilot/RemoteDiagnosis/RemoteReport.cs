using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace DriverPilot.RemoteDiagnosis;

// Wire shape of case-create-request.schema.json. Every member is always written; unknown is null.
public sealed record RemoteReport(string SchemaVersion, string AppVersion, string ScanId, string ScannedAt, string ObservationStart, string ObservationEnd,
    string? PreviousCaseId, ReportSymptom Symptom, ReportHardware Hardware, IReadOnlyList<ReportDevice> Devices, IReadOnlyList<ReportFinding> Findings,
    ReportCollection Collection, ReportWow? Wow, ReportConsent? Consent);
public sealed record ReportSymptom(string Category, string Description, string? ReproductionSteps, string? OccurredAt);
public sealed record ReportHardware(string? WindowsVersion, string? WindowsBuild, string? Architecture, string? Cpu, int? RamGb, string? SystemManufacturer, string? SystemModel,
    bool? IsPortable, string? BoardManufacturer, string? BoardModel, string? BoardRevision, string? BiosVersion, string? BiosDate);
public sealed record ReportDeviceModel(string Bus, string VendorId, string ProductId, string? SubsystemId);
public sealed record ReportDevice(string Id, string Class, ReportDeviceModel? Model, string? DriverVersion, string? DriverDate, int? ProblemCode);
public sealed record ReportFinding(string Id, string Source, int EventId, int Count, string FirstSeen, string LastSeen, string Severity, string? DeviceRef);
public sealed record ReportCollection(string Hardware, string Devices, string SystemEvents, string WlanEvents);
public sealed record ReportWow(string? GameBuild, string ProblemScope, string AddonTest);
public sealed record ReportConsent(string PrivacyNoticeVersion, string AcceptedAt, bool UploadApproved, bool ExternalAiAllowed);

/// <summary>What the user typed. Checked, never silently shortened or cleaned.</summary>
public sealed record SymptomInput(string Category, string Description, string ReproductionSteps, WowInput? Wow = null);
public sealed record WowInput(string GameBuild, string ProblemScope, string AddonTest);

/// <summary>User-fixable problem with the entered text; the message is shown as is.</summary>
public sealed class ReportInputException(string message) : Exception(message);

/// <summary>Explicit transport projection. Never serializes a Snapshot; no network, disk or UI access.</summary>
public static class RemoteReportBuilder
{
    public const int MaxDevices = 64, MaxFindings = 200, MaxFreeText = 2000;
    public static readonly IReadOnlyList<string> Categories = ["wow_crash", "pc_freeze", "graphics_reset", "performance", "network", "addon_error", "other"];
    public static readonly IReadOnlyList<string> ProblemScopes = ["wow_only", "also_elsewhere", "unknown"];
    public static readonly IReadOnlyList<string> AddonTests = ["yes", "no", "unknown"];
    static readonly string[] EventSources = ["Display", "nvlddmkm", "amdwddmg", "Microsoft-Windows-Kernel-PnP", "Microsoft-Windows-DriverFrameworks-UserMode",
        "Microsoft-Windows-WHEA-Logger", "disk", "stornvme", "storahci", "Microsoft-Windows-WLAN-AutoConfig"];
    static readonly string[] CollectionStates = ["complete", "truncated", "failed", "unavailable"];
    // Classes that are always worth a driver version, even without a problem.
    static readonly string[] CoreClasses = ["display", "network", "audio", "storage_controller", "scsi_adapter"];
    static readonly string[] SeverityOrder = ["critical", "error", "warning", "info"];

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static RemoteReport Build(Snapshot snapshot, Guid scanId, DateTimeOffset scannedAt, string appVersion, SymptomInput input, string? previousCaseId, ContractRules rules)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var end = Truncate(scannedAt);
        var start = end.AddSeconds(-rules.MaxObservationWindowSeconds);
        var symptom = Symptom(input, rules);
        var wow = Wow(input.Wow);

        var events = (snapshot.Findings ?? []).Where(f => f != null && EventSources.Contains(f.Source) && f.EventId is >= 0 and <= 65535 && f.FirstSeen.HasValue && f.LastSeen.HasValue)
            .Select(f => (Finding: f, First: Utc(f.FirstSeen!.Value), Last: Utc(f.LastSeen!.Value), Severity: Severity(f.Level)))
            .Where(e => e.Last >= start)
            .OrderBy(e => Array.IndexOf(SeverityOrder, e.Severity)).ThenByDescending(e => e.Last).ThenBy(e => e.Finding.Key, StringComparer.Ordinal)
            .ToList();
        var selectedEvents = events.Take(MaxFindings).ToList();

        var referenced = selectedEvents.Select(e => e.Finding.DeviceRef).Where(r => !string.IsNullOrEmpty(r)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = (snapshot.Devices ?? []).Where(d => d != null)
            .Select(d => (Device: d, Class: DeviceClass(d.Class), Model: HardwareModel(d.Id)))
            .Select(c => (c.Device, c.Class, c.Model, Rank: referenced.Contains(c.Device.Id) ? 0 : c.Device.Code != 0 ? 1 : c.Model != null && CoreClasses.Contains(c.Class) ? 2 : 3))
            .Where(c => c.Rank < 3)
            .OrderBy(c => c.Rank).ThenBy(c => c.Class, StringComparer.Ordinal).ThenBy(c => c.Device.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var localIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var devices = new List<ReportDevice>();
        foreach (var c in candidates.Take(MaxDevices))
        {
            var id = $"dev-{devices.Count + 1:00}";
            localIds.TryAdd(c.Device.Id, id);
            devices.Add(new(id, c.Class, c.Model, DriverVersion(c.Device.Version), ContractFormat.IsDate(c.Device.Date) ? c.Device.Date : null, c.Device.Code is >= 0 and <= 255 ? c.Device.Code : null));
        }

        var findings = selectedEvents.Select((e, index) =>
        {
            // The scan itself takes time: events a few seconds before the window are pulled onto its edge.
            var last = e.Last > end ? end : e.Last;
            var first = e.First < start ? start : e.First > last ? last : e.First;
            return new ReportFinding($"f-{index + 1:000}", e.Finding.Source, e.Finding.EventId, Math.Clamp(e.Finding.Count, 1, 100000),
                ContractFormat.Timestamp(first), ContractFormat.Timestamp(last), e.Severity,
                !string.IsNullOrEmpty(e.Finding.DeviceRef) && localIds.TryGetValue(e.Finding.DeviceRef, out var deviceId) ? deviceId : null);
        }).ToList();

        var status = snapshot.Collection ?? new CollectionStatus();
        var collection = new ReportCollection(
            snapshot.Hardware == null ? "failed" : State(status.Hardware),
            Limited(State(status.Devices), candidates.Count > MaxDevices),
            Limited(State(status.SystemEvents), events.Count > MaxFindings),
            State(status.WlanEvents));

        return new(ContractRules.ReportSchemaVersion, appVersion, ContractFormat.Uuid(scanId), ContractFormat.Timestamp(end), ContractFormat.Timestamp(start), ContractFormat.Timestamp(end),
            ContractFormat.IsUuid(previousCaseId) ? previousCaseId : null, symptom, Hardware(snapshot, rules), devices, findings, collection, wow, null);
    }

    /// <summary>Exactly what the user approves: every field except consent.</summary>
    public static string Preview(RemoteReport report)
    {
        var node = JsonSerializer.SerializeToNode(report, Json)!.AsObject();
        node.Remove("consent");
        return node.ToJsonString(Json);
    }

    /// <summary>The request body. These bytes are hashed by the server and must be reused unchanged for every retry.</summary>
    public static byte[] Freeze(RemoteReport report, ReportConsent consent)
        => JsonSerializer.SerializeToUtf8Bytes(report with { Consent = consent }, Json);

    static ReportSymptom Symptom(SymptomInput input, ContractRules rules)
    {
        if (!Categories.Contains(input.Category)) throw new ReportInputException("Bitte eine Art des Problems auswählen.");
        var description = FreeText(input.Description, "Beschreibung", rules)
            ?? throw new ReportInputException("Bitte das Problem kurz beschreiben.");
        return new(input.Category, description, FreeText(input.ReproductionSteps, "Schritte", rules), null);
    }

    public static string? FreeText(string? value, string label, ContractRules rules)
    {
        var text = (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (text.Length == 0) return null;
        if (ContractFormat.Length(text) > MaxFreeText) throw new ReportInputException($"{label}: höchstens {MaxFreeText} Zeichen. Es wird nichts abgeschnitten.");
        if (!ContractFormat.IsFreeText(text, MaxFreeText)) throw new ReportInputException($"{label}: enthält nicht erlaubte Steuerzeichen.");
        if (rules.HasForbiddenText(text)) throw new ReportInputException($"{label}: enthält möglicherweise persönliche Daten, Pfade, Adressen oder Zugangsdaten. Bitte ohne solche Angaben formulieren.");
        return text;
    }

    static ReportWow? Wow(WowInput? input)
    {
        if (input == null) return null;
        var build = input.GameBuild.Trim();
        if (build.Length > 0 && !Regex.IsMatch(build, @"\A[0-9]{1,2}\.[0-9]{1,2}\.[0-9]{1,2}(\.[0-9]{1,6})?\z", RegexOptions.CultureInvariant))
            throw new ReportInputException("Spielversion bitte in der Form 11.2.5 oder 11.2.5.63506 angeben oder leer lassen.");
        if (!ProblemScopes.Contains(input.ProblemScope) || !AddonTests.Contains(input.AddonTest)) throw new ReportInputException("Bitte die Angaben zu World of Warcraft vollständig auswählen.");
        return new(build.Length == 0 ? null : build, input.ProblemScope, input.AddonTest);
    }

    static ReportHardware Hardware(Snapshot snapshot, ContractRules rules)
    {
        var h = snapshot.Hardware;
        string? Text(string? value)
        {
            var text = (value ?? "").Trim();
            return text.Length == 0 || ManufacturerRouting.Placeholder(text) || !ContractFormat.IsTechnicalText(text) || rules.HasForbiddenText(text) ? null : text;
        }
        var build = h?.WindowsBuild?.Trim();
        return new(
            Text(snapshot.Windows?.Split(" · ")[0]),
            build != null && Regex.IsMatch(build, @"\A[0-9]{1,6}(\.[0-9]{1,6})?\z", RegexOptions.CultureInvariant) ? build : null,
            h?.Architecture?.Trim().ToLowerInvariant() switch { "x64" or "64-bit" => "x64", "arm64" => "arm64", "x86" or "32-bit" => "x86", _ => null },
            Text(snapshot.Cpu),
            double.IsFinite(snapshot.RamGB) && Math.Round(snapshot.RamGB) is >= 1 and <= 16384 ? (int)Math.Round(snapshot.RamGB) : null,
            Text(h?.SystemManufacturer), Text(h?.SystemModel), h?.IsPortable,
            Text(h?.BoardManufacturer), Text(h?.BoardModel), Text(h?.BoardRevision), Text(h?.BiosVersion),
            ContractFormat.IsDate(h?.BiosDate) ? h!.BiosDate : null);
    }

    static ReportDeviceModel? HardwareModel(string? instanceId)
        => HardwareId.Parse(instanceId) is { } id ? new(id.Bus, id.VendorId, id.ProductId, id.SubsystemId) : null;

    static string DeviceClass(string? value) => value?.ToUpperInvariant() switch
    {
        "DISPLAY" => "display", "NET" => "network", "MEDIA" => "audio", "HDC" => "storage_controller",
        "SCSIADAPTER" => "scsi_adapter", "USB" => "usb", "SYSTEM" => "system", _ => "other"
    };
    static string? DriverVersion(string? value)
        => value is { Length: > 0 and <= 40 } && Regex.IsMatch(value, @"\A[0-9]+(\.[0-9]+){0,7}\z", RegexOptions.CultureInvariant) ? value : null;
    static string Severity(int level) => level switch { 1 => "critical", 2 => "error", 3 => "warning", _ => "info" };
    static string State(string? value) => CollectionStates.Contains(value) ? value! : "failed";
    static string Limited(string state, bool limitHit) => limitHit && state == "complete" ? "truncated" : state;
    static DateTimeOffset Utc(DateTime value) => Truncate(new DateTimeOffset(value.ToUniversalTime()));
    static DateTimeOffset Truncate(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
}
