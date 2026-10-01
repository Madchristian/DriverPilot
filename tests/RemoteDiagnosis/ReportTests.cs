using System.Text;
using System.Text.Json;
using DriverPilot.RemoteDiagnosis;
using static DriverPilot.Tests.Assert;
namespace DriverPilot.Tests;

static class ReportTests
{
    static readonly ContractRules Rules = ContractRules.Embedded;
    static RemoteReport Build(Snapshot? snapshot = null, SymptomInput? input = null, string? previous = null)
        => RemoteReportBuilder.Build(snapshot ?? Synthetic.Snapshot(), Synthetic.ScanId, Synthetic.ScannedAt, "0.4.0", input ?? Synthetic.Symptom(), previous, Rules);
    static readonly ReportConsent Consent = new("2026-10-01.1", "2026-10-03T14:06:30Z", true, false);

    public static string Run()
    {
        var report = Build();
        var body = RemoteReportBuilder.Freeze(report, Consent);
        var text = Encoding.UTF8.GetString(body);
        Check(Contract.Conforms("case-create-request", body), "frozen body conforms to case-create-request.schema.json");
        Check(text.Contains("stürzt") && !text.Contains("\r") && !text.Contains("\\u00"), "UTF-8 body keeps umlauts, LF only");

        foreach (var secret in new[] { "Alice", "ALICE", "SERIAL", "REV_A1", "legacy-", "private", "Drucker", "Miniport", "Users", "System Product Name", "System manufacturer", "Application Error" })
            Check(!text.Contains(secret, StringComparison.Ordinal), "excluded from body: " + secret);

        Check(report.ScannedAt == "2026-10-03T14:02:11Z" && report.ObservationEnd == report.ScannedAt && report.ObservationStart == "2026-10-02T14:02:11Z", "window is the 24 hours before the scan");
        Check(report.Hardware is { WindowsVersion: "Microsoft Windows 11 Pro", WindowsBuild: "26100", Architecture: "x64", RamGb: 32, SystemManufacturer: null, SystemModel: null, IsPortable: false, BiosDate: "2026-03-18" },
            "hardware projection: placeholders become null, build and architecture normalized");

        // Referenced device first, then problem devices, then core classes; software and healthy USB/system devices stay out.
        Check(report.Devices.Count == 4, "four devices selected");
        Check(report.Devices[0] is { Id: "dev-01", Class: "display", Model: { Bus: "pci", VendorId: "10DE", ProductId: "2684", SubsystemId: "88891043" }, DriverVersion: "32.0.15.6094", DriverDate: "2026-08-14", ProblemCode: 0 }, "referenced GPU first with model only");
        Check(report.Devices[1] is { Class: "other", Model: null, ProblemCode: 28 } && report.Devices[2] is { Class: "usb", Model.Bus: "usb", DriverVersion: null, DriverDate: null, ProblemCode: 22 }, "problem devices; malformed version and date become null");
        Check(report.Devices[3] is { Class: "network", Model: { VendorId: "8086", SubsystemId: null } }, "healthy core class device with hardware model");

        Check(report.Findings.Count == 3, "only allowlisted event sources inside the window");
        Check(report.Findings[0] is { Id: "f-001", Source: "nvlddmkm", EventId: 153, Count: 4, Severity: "error", DeviceRef: "dev-01", FirstSeen: "2026-10-03T13:38:11Z", LastSeen: "2026-10-03T13:41:11Z" }, "most severe finding first, bound to local device id");
        Check(report.Findings[1] is { Source: "Microsoft-Windows-WHEA-Logger", Severity: "warning", DeviceRef: null }, "warning without device");
        Check(report.Findings[2] is { Source: "disk", Severity: "info", DeviceRef: null } && report.Findings[2].FirstSeen == report.ObservationStart, "event just before the window is pulled onto its edge; unknown device reference dropped");
        Check(report.Collection == new ReportCollection("complete", "complete", "complete", "unavailable"), "collection status carried over");
        Check(report.Wow == new ReportWow("11.2.5.63506", "wow_only", "no") && report.Symptom.ReproductionSteps == "1. WoW starten\n2. Schlachtzug betreten" && report.Symptom.OccurredAt == null, "symptom and game context");

        var preview = RemoteReportBuilder.Preview(report);
        Check(!preview.Contains("consent") && preview.Contains("\"scan_id\"") && text.StartsWith(preview[..^2], StringComparison.Ordinal), "preview is the body without consent");
        Check(Build(previous: "5f0c2a7e-3b1d-4c8a-9e21-7a4d0b6c1f02").PreviousCaseId != null && Build(previous: "not-a-uuid").PreviousCaseId == null, "previous case only as UUID");

        Limits();
        Sanitizing();
        Input();
        TextRules();
        return "PASS: report projection, schema conformance, minimization, limits, hardware sanitizing, input checks, shared text rules";
    }

    static void Limits()
    {
        var snapshot = Synthetic.Snapshot();
        var manyDevices = snapshot with { Devices = Enumerable.Range(0, 70).Select(i => new Device("d" + i, "Net", "1.0", "", "", 0, $@"PCI\VEN_8086&DEV_{i:X4}\X")).ToList() };
        var limited = Build(manyDevices);
        Check(limited.Devices.Count == 64 && limited.Collection.Devices == "truncated" && limited.Devices.Select(d => d.Id).Distinct().Count() == 64, "device limit marks collection as truncated");
        var manyFindings = snapshot with { Findings = Enumerable.Range(0, 230).Select(i => Synthetic.Event("disk", i, i % 4 + 1, Synthetic.ScannedAt.AddMinutes(-i - 2), Synthetic.ScannedAt.AddMinutes(-i - 1))).ToList() };
        var findings = Build(manyFindings);
        Check(findings.Findings.Count == 200 && findings.Collection.SystemEvents == "truncated" && findings.Findings[0].Severity == "critical" && findings.Findings[^1].Severity != "critical", "finding limit keeps the most severe");
        Check(Contract.Conforms("case-create-request", RemoteReportBuilder.Freeze(findings, Consent)) && Contract.Conforms("case-create-request", RemoteReportBuilder.Freeze(limited, Consent)), "limited reports still conform");
        Check(RemoteReportBuilder.Freeze(findings, Consent).Length < Rules.MaxUploadBytes, "largest report stays below the upload limit");

        // A snapshot written before this feature has no structured event fields and no collection status.
        var legacy = snapshot with { Collection = null, Hardware = null, Findings = [new("event:disk:158::2026100313", "disk", "detail", "Warnung", "", "", 2, DateTime.Now, DateTime.Now)] };
        var old = Build(legacy);
        Check(old.Findings.Count == 0 && old.Collection == new ReportCollection("failed", "failed", "failed", "failed") && old.Hardware.BoardModel == null && old.Hardware.IsPortable == null, "unknown collection is failed, never healthy");
        Check(Contract.Conforms("case-create-request", RemoteReportBuilder.Freeze(old, Consent)), "minimal report conforms");
        Check(Build(snapshot with { Collection = new("bogus", "complete", "truncated", "complete") }).Collection is { Hardware: "failed", SystemEvents: "truncated" }, "unknown status value becomes failed");
    }

    static void Sanitizing()
    {
        var snapshot = Synthetic.Snapshot();
        var hostile = Build(snapshot with
        {
            Windows = "Windows 11 <b>", Cpu = @"C:\Users\Alice\cpu", RamGB = double.NaN,
            Hardware = snapshot.Hardware! with { BoardModel = "Tower 192.168.1.12", BoardRevision = new string('x', 161), BiosVersion = "token=ghp_SUPERSECRET123", BiosDate = "18.03.2026", Architecture = "ia64", WindowsBuild = "26100 Insider" }
        }).Hardware;
        Check(hostile is { WindowsVersion: null, Cpu: null, RamGb: null, BoardModel: null, BoardRevision: null, BiosVersion: null, BiosDate: null, Architecture: null, WindowsBuild: null }, "unsafe or malformed hardware values become null");
        Check(Build(snapshot with { Cpu = "Intel(R) Core(TM) i7-9700K CPU @ 3.60GHz" }).Hardware.Cpu != null, "ordinary CPU name with @ survives");
        Check(Build(snapshot with { Devices = [new("x", "Display", "1", "", "", 0, @"PCI\VEN_12345&DEV_ABCD\S"), new("y", "Display", "1", "", "", 0, @"PCI\VEN_10DE&DEV_2684&SUBSYS_12\S")] }).Devices.Count == 0, "malformed hardware ids are not sent");
    }

    static void Input()
    {
        var ok = Synthetic.Symptom();
        void Rejected(SymptomInput input, string name) => Throws<ReportInputException>(() => Build(input: input), name);
        Rejected(ok with { Description = "  " }, "empty description");
        Rejected(ok with { Description = new string('x', 2001) }, "description over 2000 characters is rejected, not cut");
        Rejected(ok with { Category = "hardware_defect" }, "unknown category");
        Rejected(ok with { Description = "Absturz\u0007" }, "control character");
        Rejected(ok with { ReproductionSteps = "Log liegt unter C:\\Users\\alice\\wow.log" }, "path in steps");
        Rejected(ok with { Wow = new("Patch 11", "wow_only", "no") }, "malformed game build");
        Rejected(ok with { Wow = new("", "sometimes", "no") }, "unknown problem scope");
        Check(Build(input: ok with { Description = new string('ä', 2000), ReproductionSteps = "", Wow = new("", "unknown", "unknown") }) is { Symptom.ReproductionSteps: null, Wow.GameBuild: null }, "limit counts characters; empty optional values are null");
        Check(Build(input: ok with { Wow = null }).Wow == null, "game context is optional");
    }

    /// <summary>The embedded rules must judge the contract fixtures exactly as the contract says.</summary>
    static void TextRules()
    {
        string[] pointers = ["description", "reproduction_steps", "note", "windows_version", "cpu", "system_manufacturer", "system_model", "board_manufacturer", "board_model", "board_revision", "bios_version"];
        bool Forbidden(string path)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            var holders = new List<JsonElement> { root };
            foreach (var name in new[] { "symptom", "hardware" }) if (root.TryGetProperty(name, out var child)) holders.Add(child);
            return holders.SelectMany(h => pointers.Select(p => h.TryGetProperty(p, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null)).Any(Rules.HasForbiddenText);
        }
        using var scenario = JsonDocument.Parse(Contract.Fixture("scenario.json"));
        var expected = scenario.RootElement.GetProperty("invalid").EnumerateObject().Where(e => e.Value.GetProperty("code").GetString() == "text_rejected").Select(e => e.Name).ToList();
        Check(expected.Count >= 5, "contract lists text_rejected fixtures");
        foreach (var name in expected) Check(Forbidden(Path.Combine(Contract.Root, "fixtures", "invalid", name)), "client rejects text of " + name);
        foreach (var schema in new[] { "case-create-request", "feedback-request" })
            foreach (var path in Contract.Fixtures("valid", schema)) Check(!Forbidden(path), "client accepts text of " + Path.GetFileName(path));
        Check(Rules.MaxUploadBytes == 262144 && Rules.MaxScanAgeSeconds == 900 && Rules.ErrorCodes.Contains("request_retired") && Rules.ErrorCodes.Count == 27, "embedded limits and error codes come from the contract");
    }
}
