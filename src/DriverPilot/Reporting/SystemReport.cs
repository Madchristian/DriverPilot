using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace DriverPilot;

public enum ReportManufacturerStatus { Unknown, Supported, Manual, Unsupported, FetchError }
public enum ReportManufacturerCategory { Other, Bios, Graphics, DriverPackage, BoardDrivers, Audio, Storage, Oem, Portal, Device }

/// <summary>Immutable counts only: never retains manufacturer inventory, names, URLs or errors.</summary>
public sealed class ManufacturerSummary
{
    public string Text { get; }
    readonly WeakReference<ReportScanContext> owner;
    readonly long generation;
    internal bool IsCurrent(DateTimeOffset now)
        => owner.TryGetTarget(out var context) && context.IsSummaryCurrent(this, generation, now);
    internal bool IsFor(Snapshot snapshot, DateTimeOffset now)
        => owner.TryGetTarget(out var context) && context.IsCurrent(generation, snapshot, now) && ReferenceEquals(context.SummaryFor(snapshot, now), this);
    internal ManufacturerSummary(ManufacturerReport report, ReportScanContext context)
    {
        owner = new(context); generation = context.Generation;
        var rows = (report.Findings ?? []).Where(f => f != null).Select(f => (
            Category: f.Category switch {
                "BIOS / UEFI" => ReportManufacturerCategory.Bios, "Grafiktreiber" => ReportManufacturerCategory.Graphics,
                "Treiberpaket" => ReportManufacturerCategory.DriverPackage, "Mainboard-Treiber" => ReportManufacturerCategory.BoardDrivers,
                "Audiotreiber" => ReportManufacturerCategory.Audio, "SSD-Firmware" or "SSD / Laufwerk-Firmware" => ReportManufacturerCategory.Storage,
                "OEM-Treiber" => ReportManufacturerCategory.Oem, "Herstellerportal" => ReportManufacturerCategory.Portal,
                "Herstellerprüfung" => ReportManufacturerCategory.Device, _ => ReportManufacturerCategory.Other },
            Status: f.Status switch {
                "Angebotene Version installiert" or "Installiert ist eine neuere Version" or "Neuere Version gefunden" or "Neue Version · zunächst abwarten" => ReportManufacturerStatus.Supported,
                "Manuelle Modellprüfung" or "Manuelle Modellprüfung · Zuordnung offen" or "Herstellertool nötig" or "Im Browser prüfen" or "Versionsvergleich offen" => ReportManufacturerStatus.Manual,
                "Nicht unterstützt · Zuordnung offen" => ReportManufacturerStatus.Unsupported,
                "Quelle nicht abrufbar" => ReportManufacturerStatus.FetchError, _ => ReportManufacturerStatus.Unknown }));
        Text = string.Join("\n", rows.GroupBy(r => r).OrderBy(g => g.Key.Category).ThenBy(g => g.Key.Status)
            .Select(g => $"{g.Key.Category}: {g.Key.Status} ({g.Count().ToString(CultureInfo.InvariantCulture)})"));
    }
}

/// <summary>UI-thread-owned provenance. BeginScan invalidates before any await, including failed scans.</summary>
public sealed class ReportScanContext
{
    public long Generation { get; private set; }
    Snapshot? current;
    DateTimeOffset? scannedAt;
    DateTimeOffset? checkedAt;
    ManufacturerSummary? summary;
    public long BeginScan() { current = null; scannedAt = null; ClearManufacturers(); return ++Generation; }
    public void ClearManufacturers() { summary = null; checkedAt = null; }
    public void CompleteScan(long generation, Snapshot snapshot, DateTimeOffset at)
    { if (generation == Generation) { current = snapshot; scannedAt = at; } }
    public bool IsGenerationCurrent(long generation, Snapshot snapshot)
        => generation == Generation && ReferenceEquals(current, snapshot);
    public bool IsCurrent(long generation, Snapshot snapshot, DateTimeOffset now)
        => IsGenerationCurrent(generation, snapshot) && SystemReportSession.IsFresh(current, scannedAt, now);
    public bool AcceptManufacturers(long generation, Snapshot snapshot, ManufacturerReport report, DateTimeOffset now)
    {
        if (!IsGenerationCurrent(generation, snapshot)) return false;
        summary = new ManufacturerSummary(report, this); checkedAt = now; return true;
    }
    // Synchronous UI-thread publication: never await between validation and side effects.
    public bool PublishManufacturers(long generation, Snapshot snapshot, ManufacturerReport report, DateTimeOffset now,
        Action<ManufacturerReport> persist, Action<ManufacturerReport> render)
    {
        if (!AcceptManufacturers(generation, snapshot, report, now)) return false;
        persist(report); render(report); return true;
    }
    internal bool IsSummaryCurrent(ManufacturerSummary candidate, long generation, DateTimeOffset now)
        => current != null && generation == Generation && ReferenceEquals(SummaryFor(current, now), candidate);
    public ManufacturerSummary? SummaryFor(Snapshot snapshot, DateTimeOffset now)
        => IsCurrent(Generation, snapshot, now) && checkedAt.HasValue && now >= checkedAt && now - checkedAt <= TimeSpan.FromMinutes(15) ? summary : null;
}

/// <summary>No network, disk, clipboard or Store access. Adapters run only on explicit actions.</summary>
public sealed class SystemReportSession
{
    public const string IssueEndpoint = "https://github.com/Madchristian/DriverPilot/issues/new";
    public const int MaxUrlLength = 2000;
    readonly Snapshot snapshot;
    readonly string? manufacturerText;
    readonly ManufacturerSummary? manufacturerSummary;
    readonly DateTimeOffset scannedAt;
    readonly Func<DateTimeOffset> clock;
    readonly Func<Uri, bool> open;
    readonly Func<string, bool?> save;
    public SystemReport? Report { get; private set; }
    public bool Consent { get; set; }
    public string Status { get; private set; } = "";
    public SystemReportSession(Snapshot snapshot, DateTimeOffset scannedAt, Func<DateTimeOffset> clock, Func<Uri, bool> open, Func<string, bool?> save)
        : this(snapshot, scannedAt, clock, open, save, null) { }
    public SystemReportSession(Snapshot snapshot, DateTimeOffset scannedAt, Func<DateTimeOffset> clock, Func<Uri, bool> open, Func<string, bool?> save, ManufacturerSummary? manufacturers)
    {
        manufacturerSummary = manufacturers?.IsFor(snapshot, clock()) == true ? manufacturers : null;
        manufacturerText = manufacturerSummary?.Text;
        // Freeze the only mutable allowlisted collection; no retained diagnostics.
        this.snapshot = snapshot with { Devices = snapshot.Devices?.ToList()!, Findings = [], Warnings = [] };
        this.scannedAt = scannedAt; this.clock = clock; this.open = open; this.save = save;
        UpdateNotes("", "", "", "");
    }
    public static bool IsFresh(Snapshot? data, DateTimeOffset? scannedAt, DateTimeOffset now)
        => data?.Devices is { Count: > 0 and <= 1024 } && !string.IsNullOrWhiteSpace(data.Windows) && scannedAt.HasValue && now >= scannedAt && now - scannedAt <= TimeSpan.FromMinutes(15);
    public void UpdateNotes(string problem, string reproduction, string expected, string actual)
    {
        Consent = false; Report = null;
        try { Report = SystemReport.CreateWithSummary(snapshot, problem, reproduction, expected, actual, manufacturerText); Status = "Vorschau neu erzeugt. Bitte vollständig prüfen."; }
        catch (ArgumentException) { Status = "Ungültige oder zu große Eingabe. Maximal 1000 Zeichen je Notiz; nichts wird abgeschnitten."; }
    }
    bool Ready()
    {
        if (!IsFresh(snapshot, scannedAt, clock())) { Consent = false; Status = "Kein gültiger frischer Scan. Dialog schließen und erneut prüfen (maximal 15 Minuten)."; return false; }
        if (manufacturerSummary != null && !manufacturerSummary.IsCurrent(clock())) { Consent = false; Status = "Herstellerprüfung nicht mehr aktuell gebunden. Dialog schließen und Bericht neu erzeugen."; return false; }
        if (Report == null) { Status = "Keine gültige Vorschau. Eingaben prüfen."; return false; }
        return true;
    }
    public bool OpenDraft()
    {
        if (!Ready()) return false;
        if (!Consent) { Status = "Bitte zuerst die Vorschau prüfen und der Browser-Übergabe zustimmen."; return false; }
        Consent = false;
        var url = IssueEndpoint + "?title=DriverPilot%20Systembericht&body=" + Uri.EscapeDataString(Report!.Text);
        if (url.Length > MaxUrlLength) { Status = "Entwurfs-URL zu lang. Nichts wurde übertragen oder abgeschnitten. Bericht lokal speichern und bei Bedarf manuell anhängen."; return false; }
        try
        {
            if (!open(new Uri(url))) { Status = "Browser konnte nicht geöffnet werden. Offline: Bericht lokal speichern."; return false; }
            Status = "Entwurf an Browser übergeben, kein Issue erstellt. GitHub-Anmeldung und Repository-Zugriff sind erforderlich; Veröffentlichung nur durch dich.";
            return true;
        }
        catch { Status = "Browser-Übergabe fehlgeschlagen. Offline: Bericht lokal speichern."; return false; }
    }
    public bool Export()
    {
        if (!Ready()) return false;
        try
        {
            var result = save(Report!.Text);
            Status = result == true ? "Vorschau unverändert lokal gespeichert. Kein Upload." : result == null ? "Speichern abgebrochen. Keine Datei geschrieben." : "Speichern fehlgeschlagen. Anderen Speicherort wählen.";
            return result == true;
        }
        catch { Status = "Speichern fehlgeschlagen. Anderen Speicherort wählen."; return false; }
    }
}

/// <summary>Immutable, explicitly projected sharing schema. Never serialize a Snapshot.</summary>
public sealed class SystemReport
{
    public const int MaxNoteLength = 1000;
    public const int MaxReportLength = 12000;
    // v1 allowlist: OS/CPU/RAM, structured board/BIOS/system fields, selected
    // device class + numeric driver version + status + product ID, four user notes.
    // Names, providers, arbitrary diagnostics and all persistence are excluded.
    // GPU model names are deliberately omitted: only validated numeric PCI VEN/DEV/SUBSYS,
    // not an untrusted friendly name or an unverified model lookup, enters the report.
    public string Text { get; }
    private SystemReport(string text) => Text = text;
    public static SystemReport Create(Snapshot snapshot, string problem = "", string reproduction = "", string expected = "", string actual = "")
        => CreateWithSummary(snapshot, problem, reproduction, expected, actual, null);
    internal static SystemReport CreateWithSummary(Snapshot snapshot, string problem, string reproduction, string expected, string actual, string? manufacturerText)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Devices == null || snapshot.Devices.Count > 1024) throw new ArgumentException("Geräteliste fehlt oder ist zu groß. Erneut prüfen.");
        var h = snapshot.Hardware;
        var b = new StringBuilder("DriverPilot Systembericht v1\n");
        b.AppendLine("App-Version: " + typeof(SystemReport).Assembly.GetName().Version);
        b.AppendLine("Datensparsame Auswahl; keine vollständige Diagnose. Freitext bitte selbst prüfen.");
        b.AppendLine("Windows: " + Technical(snapshot.Windows, allowInventorySeparator: true));
        b.AppendLine("Build / Architektur: " + Technical(h?.WindowsBuild) + " / " + Technical(h?.Architecture));
        b.AppendLine("CPU: " + Technical(snapshot.Cpu));
        b.AppendLine("RAM (GB): " + (double.IsFinite(snapshot.RamGB) && snapshot.RamGB > 0 && snapshot.RamGB <= 16384 ? Math.Round(snapshot.RamGB).ToString(CultureInfo.InvariantCulture) : "unbekannt"));
        b.AppendLine("System: " + Technical(h?.SystemManufacturer) + " / " + Technical(h?.SystemModel));
        b.AppendLine("Bauform: " + (h == null ? "unbekannt" : h.IsPortable ? "mobil" : "stationär / nicht als mobil erkannt"));
        b.AppendLine("Mainboard: " + (h == null ? "unbekannt" : Technical(h.BoardManufacturer) + " / " + Technical(h.BoardModel) + " / Revision " + Technical(h.BoardRevision)));
        b.AppendLine("BIOS: " + Technical(h?.BiosVersion) + " / " + Technical(h?.BiosDate));
        b.AppendLine(manufacturerText == null ? "Herstellerprüfung: Nicht geprüft (keine aktuelle, gebundene Prüfung im Bericht)."
            : "Herstellerprüfung (aktuell gebunden; Kategorien / Status / Anzahl):\n" + (manufacturerText.Length == 0 ? "Keine Prüfeinträge." : manufacturerText));
        b.AppendLine("Geräteauswahl: Grafik und problematische Kern-Geräte; maximal 8 eindeutige Einträge, keine Gerätenamen.");
        var rows = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var d in snapshot.Devices)
        {
            if (d == null) continue;
            var category = new[] { "Display", "Net", "MEDIA", "HDC", "SCSIAdapter", "USB", "System" }.FirstOrDefault(c => c.Equals(d.Class, StringComparison.OrdinalIgnoreCase));
            if (category == null || (category != "Display" && d.Code == 0)) continue;
            var id = ProductId(d.Id);
            if (id.Length == 0) continue;
            var version = d.Version is { Length: > 0 and <= 40 } && !Sensitive(d.Version) && Regex.IsMatch(d.Version, @"\A[0-9]+(?:\.[0-9]+){0,7}\z", RegexOptions.CultureInvariant) ? d.Version : "unbekannt";
            rows.Add($"{category}: {id}; Treiber {version}; Code {(d.Code is >= 0 and <= 255 ? d.Code.ToString(CultureInfo.InvariantCulture) : "unbekannt")}");
        }
        foreach (var row in rows.Take(8)) b.AppendLine(row);
        if (rows.Count == 0) b.AppendLine("Keine passenden Geräte in der datensparsamen Auswahl.");
        b.AppendLine("\nOptionale Angaben (gefiltert):");
        foreach (var (label, value) in new[] { ("Problem", problem), ("Schritte", reproduction), ("Erwartet", expected), ("Tatsächlich", actual) })
            b.AppendLine(label + ": " + Note(value));
        if (b.Length > MaxReportLength) throw new ArgumentException("Bericht ist zu groß. Angaben kürzen; es wird nichts abgeschnitten.");
        return new(b.ToString());
    }

    static string ProductId(string? value)
    {
        if (value == null || value.Length > 512) return "";
        var pci = Regex.Match(value, @"\APCI\\VEN_([0-9A-F]{4})&DEV_([0-9A-F]{4})(?:&SUBSYS_([0-9A-F]{8}))?(?=&|\\|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (pci.Success)
        {
            // Reject a malformed SUBSYS instead of silently accepting just VEN/DEV.
            if (value.Contains("&SUBSYS_", StringComparison.OrdinalIgnoreCase) && !pci.Groups[3].Success) return "";
            return $"PCI\\VEN_{pci.Groups[1].Value}&DEV_{pci.Groups[2].Value}".ToUpperInvariant() + (pci.Groups[3].Success ? "&SUBSYS_" + pci.Groups[3].Value.ToUpperInvariant() : "");
        }
        var usb = Regex.Match(value, @"\AUSB\\VID_([0-9A-F]{4})&PID_([0-9A-F]{4})(?=&|\\|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return usb.Success ? $"USB\\VID_{usb.Groups[1].Value}&PID_{usb.Groups[2].Value}".ToUpperInvariant() : "";
    }

    // Conservative heuristic, NOT a guarantee that arbitrary prose is anonymous.
    // Discard the entire suspect line, avoiding partial secret fragments.
    static bool Sensitive(string value)
    {
        const string pattern = @"(?ix) @ | :// | www\. | [a-z]:[\\/] | \\\\ | (?:^|\s)/(?:[^\s/]+/) | \b(?:\d{1,3}\.){3}\d{1,3}\b | (?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2} | (?:[0-9a-f]{0,4}:){2,}[0-9a-f:]* | \b[0-9a-f]{8}-[0-9a-f]{4}- | \b(?:gh[pousr]_|github_pat_|sk-|eyJ)[a-z0-9_-]+ | \b[a-z0-9_/-]{32,}\b | \b(?:serial(?:number)?|seriennummer|s/n|token|password|passwort|secret|api[ _-]?key|bearer|authorization|host(?:name)?|computer(?:name)?|username|benutzer(?:name)?|machine(?:name)?)\b";
        if (Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant)) return true;
        foreach (var identity in new[] { Environment.UserName, Environment.MachineName })
            if (identity.Length >= 3 && value.Contains(identity, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static string Technical(string? value, bool allowInventorySeparator = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unbekannt";
        if (value.Length > 160 || Sensitive(value) || !Regex.IsMatch(value, allowInventorySeparator ? @"\A[\p{L}\p{N} ·._()+®™/:-]+\z" : @"\A[\p{L}\p{N} ._()+®™/:-]+\z", RegexOptions.CultureInvariant)) return "[entfernt]";
        return value.Trim();
    }

    static string Note(string? value)
    {
        if (value?.Length > MaxNoteLength) throw new ArgumentException("Je Notiz sind höchstens 1000 Zeichen erlaubt. Es wird nichts abgeschnitten.");
        if (string.IsNullOrWhiteSpace(value)) return "nicht angegeben";
        return string.Join("\n", value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(line =>
            Sensitive(line) || line.Any(c => char.IsControl(c) && c != '\t' || char.GetUnicodeCategory(c) == UnicodeCategory.Format) || line.IndexOfAny(['<', '>', '[', ']', '`']) >= 0 ? "[entfernt – bitte ohne persönliche Daten formulieren]" : line.Trim()));
    }
}