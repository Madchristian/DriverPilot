using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using Point = System.Windows.Point;
namespace DriverPilot;

public static class SystemReportTests
{
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); }
    internal static Snapshot Fixture() => new("Windows 11 Pro", "AMD Ryzen 7 7800X3D", "legacy-board-secret", "legacy-bios-secret", 32,
        [new("Alice private monitor", "Display", "32.0.15.6094", "NVIDIA", "2025-01-02", 0, @"PCI\VEN_10DE&DEV_2684&SUBSYS_123410DE&REV_A1\ALICE-PC-SERIAL"),
         new("Private printer Alice", "Printer", "1.0", "Secret provider", "", 42, @"USB\VID_1234&PID_ABCD\SERIAL"),
         new("Bob private USB", "USB", "10.0.26100.1", "Microsoft", "", 10, @"USB\VID_1234&PID_ABCD\BOB-SERIAL")],
        [new("secret-id", "secret-finding", "secret-event-log", "Warnung", "secret-time")], ["secret-warning"],
        new("ASUSTeK", "ROG Model", "ASUSTeK", "B650 PLUS", "1.0", "1811", "2024-01-02", "64-bit", "26100", false));

    public static string RunBackend()
    {
        var text = SystemReport.Create(Fixture(), "Bildschirm flackert", "Spiel starten", "Ruhiges Bild", "Flackern").Text;
        Check(text.Contains("DriverPilot Systembericht v1"), "versioned schema");
        Check(text.Contains("App-Version: " + typeof(SystemReport).Assembly.GetName().Version), "actual assembly app version in immutable report");
        Check(SystemReport.Create(Fixture() with { Windows = "Microsoft Windows 11 Pro · Build 26200" }).Text.Contains("Windows: Microsoft Windows 11 Pro · Build 26200"), "real inventory Windows separator retained");
        Check(!SystemReport.Create(Fixture() with { Windows = "Windows · host=PRIVATE-PC" }).Text.Contains("PRIVATE-PC"), "Windows sensitive filter retained");
        Check(text.Contains("B650 PLUS") && text.Contains("26100") && text.Contains("7800X3D"), "structured hardware allowlist");
        Check(text.Contains("PCI\\VEN_10DE&DEV_2684&SUBSYS_123410DE") && text.Contains("USB\\VID_1234&PID_ABCD"), "strict hardware ID projection");
        foreach (var secret in new[] { "Alice", "Bob", "SERIAL", "legacy-", "secret-", "Printer", "REV_A1", "Secret provider" })
            Check(!text.Contains(secret, StringComparison.OrdinalIgnoreCase), "excluded " + secret);
        Check(text.Contains("Nicht geprüft") && text.Contains("Bildschirm flackert") && text.Contains("Spiel starten") && text.Contains("Ruhiges Bild"), "notes and honest manufacturer status");
        var malicious = Fixture() with { Cpu = @"C:\Users\Alice\private", Hardware = new(SystemModel:"host=ALICE-PC", BoardModel:"serial=SER123456", BiosVersion:"token=ghp_SUPERSECRET123") };
        var notes = "alice@example.com\n192.168.1.12\nAA:BB:CC:DD:EE:FF\nC:\\Users\\Alice\\secret\npassword=HUNTER2\nhostname=ALICE-PC\nhttps://example.com/private\n@Alice\nBearer eyJhbGciOiJIUzI1NiJ9.secret.signature\nS/N=SECRET987\n\\\\ALICE-PC\\share\n/home/alice/private";
        var scrubbed = SystemReport.Create(malicious, notes).Text;
        foreach (var secret in new[] { "Alice", "192.168", "AA:BB", "HUNTER2", "SUPERSECRET", "SER123456", "SECRET987", "eyJhb", "example.com", "/home/" })
            Check(!scrubbed.Contains(secret, StringComparison.OrdinalIgnoreCase), "planted sensitive value " + secret);
        Check(SystemReport.Create(Fixture() with { Hardware = null }).Text.Contains("Mainboard: unbekannt"), "missing profile is unknown not legacy fallback");
        Check(!SystemReport.Create(Fixture() with { Devices = [new("name", "Display", "1", "", "", 0, @"PCI\VEN_12345&DEV_ABCD\SECRET")] }).Text.Contains("VEN_1234"), "malformed ID rejected");
        Check(!SystemReport.Create(Fixture() with { Devices = [Fixture().Devices[0] with { Version = "192.168.1.12" }] }).Text.Contains("192.168.1.12"), "IP planted in technical driver version is removed");
        try { SystemReport.Create(Fixture(), new string('x', 1001)); throw new Exception("FAIL: note limit"); } catch (ArgumentException) { }
        try { SystemReport.Create(Fixture(), new string(' ', 1001)); throw new Exception("FAIL: whitespace note limit"); } catch (ArgumentException) { }
        try { SystemReport.Create(Fixture(), string.Concat(Enumerable.Repeat("<\n", 500))); throw new Exception("FAIL: expanded report limit"); } catch (ArgumentException) { }
        try { SystemReport.Create(Fixture() with { Devices = Enumerable.Repeat(Fixture().Devices[0], 1025).ToList() }); throw new Exception("FAIL: device input limit"); } catch (ArgumentException) { }
        Check(typeof(SystemReport).Assembly.GetType("DriverPilot.SystemReportSession") != null, "consent-gated draft/export session exists");
        RunManufacturerBinding();
        RunManufacturerPublication().GetAwaiter().GetResult();
        RunSession();
        return "PASS: schema, hardware allowlist, ID projection, minimization, planted secrets, missing profile, limits";
    }

    static void RunManufacturerBinding()
    {
        var type = typeof(SystemReport).Assembly.GetType("DriverPilot.ReportScanContext");
        Check(type != null, "current scan generation context exists");
        dynamic context = Activator.CreateInstance(type!)!;
        var now = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
        var scan = Fixture();
        long first = context.BeginScan();
        context.CompleteScan(first, scan, now);
        var report = new ManufacturerReport(now.LocalDateTime, new("private board", "private bios", "", [], ""),
            [new("private name", "Grafiktreiber", "secret installed", "secret available", "", "Angebotene Version installiert", "https://secret.invalid", "private error"),
             new("private", "BIOS / UEFI", "", "", "", "Manuelle Modellprüfung", "", "private"),
             new("private", "SSD-Firmware", "", "", "", "Nicht unterstützt · Zuordnung offen", "", "private"),
             new("private", "Treiberpaket", "", "", "", "Quelle nicht abrufbar", "", "private"),
             new("private", "evil category", "", "", "", "evil status", "", "private")]);
        Check(!context.AcceptManufacturers(first, scan with { }, report, now), "value-equal but reference-mismatched result rejected");
        Check(context.AcceptManufacturers(first, scan, report, now), "current bound manufacturer result accepted");
        dynamic summary = context.SummaryFor(scan, now);
        Check(summary != null, "fresh summary available");
        string projected = summary!.Text;
        foreach (var status in new[] { "Supported", "Manual", "Unsupported", "FetchError", "Unknown" }) Check(projected.Contains(status), "projected status " + status);
        foreach (var secret in new[] { "private", "secret", "evil", "https" }) Check(!projected.Contains(secret), "no raw manufacturer fields " + secret);
        var constructor = typeof(SystemReportSession).GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 6);
        Check(constructor != null, "session accepts optional bound manufacturer summary");
        string? saved = null; Uri? opened = null;
        var session = (SystemReportSession)constructor!.Invoke([scan, now, (Func<DateTimeOffset>)(() => now), (Func<Uri, bool>)(u => { opened = u; return true; }), (Func<string, bool?>)(s => { saved = s; return true; }), summary]);
        Check(session.Report!.Text.Contains(projected), "summary included in immutable preview");
        session.Export(); session.Consent = true;
        Check(session.OpenDraft(), "manufacturer summary fits successful draft fixture");
        Check(Uri.UnescapeDataString(opened!.Query.Split("&body=")[1]) == saved && saved == session.Report.Text, "manufacturer preview export URL identity");
        var mismatch = (SystemReportSession)constructor.Invoke([scan with { }, now, (Func<DateTimeOffset>)(() => now), (Func<Uri, bool>)(_ => true), (Func<string, bool?>)(_ => true), summary]);
        Check(mismatch.Report!.Text.Contains("Nicht geprüft"), "summary cannot attach to another snapshot");
        report.Findings.Clear(); Check((string)summary.Text == projected, "summary freezes projection");
        Check(context.SummaryFor(scan with { }, now) == null && context.SummaryFor(scan, now.AddMinutes(16)) == null, "mismatched and expired summaries excluded");
        Check(context.AcceptManufacturers(first, scan, report, now.AddMinutes(16)), "old-scan manual manufacturer result still completes");
        Check(context.SummaryFor(scan, now.AddMinutes(16)) == null, "old-scan manufacturer result remains unavailable for sharing");
        long second = context.BeginScan();
        session.Consent = true;
        Check(!session.Export() && !session.OpenDraft() && !session.Consent, "revoked manufacturer generation blocks existing session side effects");
        Check(context.SummaryFor(scan, now) == null, "new scan immediately clears manufacturer context even on failure");
        context.CompleteScan(second, scan, now);
        Check(!context.AcceptManufacturers(first, scan, report, now), "prior generation async result rejected even with same reference");
        var revoked = (SystemReportSession)constructor.Invoke([scan, now, (Func<DateTimeOffset>)(() => now), (Func<Uri, bool>)(_ => true), (Func<string, bool?>)(_ => true), summary]);
        Check(revoked.Report!.Text.Contains("Nicht geprüft"), "retained old summary cannot attach to new generation");
        var pending = new TaskCompletionSource<ManufacturerReport>();
        async Task<bool> CompleteLater()
        {
            var completed = await pending.Task;
            return context.AcceptManufacturers(second, scan, completed, now);
        }
        var delayed = CompleteLater();
        context.BeginScan(); // Replacement scan fails: no CompleteScan call.
        pending.SetResult(report);
        Check(!delayed.GetAwaiter().GetResult() && context.SummaryFor(scan, now) == null, "delayed asynchronous result cannot resurrect failed replacement scan");
        Console.WriteLine("PASS: current-scan reference/generation binding, stale rejection, immutable manufacturer projection");
    }

    static async Task RunManufacturerPublication()
    {
        Check(typeof(ReportScanContext).GetMethod("PublishManufacturers") != null, "generation validation before persistence/render callbacks exists");
        dynamic context = new ReportScanContext();
        var now = DateTimeOffset.UtcNow;
        var scanA = Fixture(); var scanB = Fixture();
        long a = context.BeginScan(); context.CompleteScan(a, scanA, now);
        var reportA = new ManufacturerReport(now.LocalDateTime, new("A", "", "", [], ""), []);
        var reportB = reportA with { Inventory = reportA.Inventory with { Board = "B" } };
        ManufacturerReport? persisted = null, rendered = null;
        var effects = new List<string>();
        Action<ManufacturerReport> save = r => { persisted = r; effects.Add("save"); };
        Action<ManufacturerReport> render = r => { rendered = r; effects.Add("render"); };
        var pending = new TaskCompletionSource<ManufacturerReport>();
        async Task<bool> CompleteA()
        {
            var result = await pending.Task;
            return context.PublishManufacturers(a, scanA, result, now, save, render);
        }
        var delayed = CompleteA();
        long b = context.BeginScan(); context.CompleteScan(b, scanB, now);
        Check(context.PublishManufacturers(b, scanB, reportB, now, save, render), "B publishes");
        Check(effects.SequenceEqual(new[] { "save", "render" }), "accepted publication saves then renders exactly once");
        effects.Clear(); pending.SetResult(reportA);
        Check(!await delayed && effects.Count == 0 && ReferenceEquals(persisted, reportB) && ReferenceEquals(rendered, reportB), "delayed A cannot save or render over B");
        context.BeginScan();
        Check(!context.PublishManufacturers(b, scanB, reportB, now, save, render) && effects.Count == 0, "failed replacement blocks all publication callbacks");
        long old = context.BeginScan(); context.CompleteScan(old, scanA, now.AddMinutes(-16));
        Check(context.PublishManufacturers(old, scanA, reportA, now, save, render) && ReferenceEquals(rendered, reportA), "old-scan manual search completes visibly");
        int sharingCalls = 0;
        var session = new SystemReportSession(scanA, now.AddMinutes(-16), () => now, _ => { sharingCalls++; return true; }, _ => { sharingCalls++; return true; }, (ManufacturerSummary?)context.SummaryFor(scanA, now));
        session.Consent = true;
        Check(!session.Export() && !session.OpenDraft() && sharingCalls == 0, "old scan sharing still fails closed");
        GC.KeepAlive(context);
        Console.WriteLine("PASS: delayed A/B publication callbacks, failed replacement, old-scan visible completion and closed sharing");
    }

    static void RunSession()
    {
        var now = new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero);
        var scanned = now;
        int calls = 0; Uri? opened = null; string? saved = null;
        // Reflection lets the feature-absence assertion run before implementation exists.
        dynamic session = Activator.CreateInstance(typeof(SystemReport).Assembly.GetType("DriverPilot.SystemReportSession")!,
            Fixture(), scanned, (Func<DateTimeOffset>)(() => now), (Func<Uri, bool>)(uri => { calls++; opened = uri; return true; }),
            (Func<string, bool?>)(body => { saved = body; return true; }))!;
        Check(!session.OpenDraft() && calls == 0, "no browser without consent");
        Check(session.Export() && saved == (string)session.Report.Text, "export equals immutable preview");
        session.Consent = true;
        Check(session.OpenDraft() && calls == 1, "explicit consent opens draft");
        Check(opened!.GetLeftPart(UriPartial.Path) == "https://github.com/Madchristian/DriverPilot/issues/new", "fixed repo only");
        Check(Uri.UnescapeDataString(opened.Query.Split("&body=")[1]) == saved, "issue body equals preview/export");
        Check(!session.Consent, "consent consumed");
        session.Consent = true; session.UpdateNotes("Changed", "", "", "");
        Check(!session.Consent && ((string)session.Report.Text).Contains("Changed"), "edit regenerates preview and invalidates consent");
        session.UpdateNotes(new string('ä', 990), "", "", ""); session.Consent = true;
        Check(!session.OpenDraft() && calls == 1 && ((string)session.Status).Contains("lang"), "oversize URL blocked without truncation");
        Check(session.Export() && saved!.Contains(new string('ä', 990)), "oversize exports complete report offline");
        session.UpdateNotes(new string('x', 1001), "", "", "");
        Check(session.Report == null && !session.Consent && !session.Export(), "invalid edit clears old report");
        session.UpdateNotes("", "", "", ""); session.Consent = true; now = scanned.AddMinutes(16);
        Check(!session.OpenDraft() && !session.Export() && calls == 1, "expired scan rejected at side-effect time");
        now = scanned.AddMinutes(-1);
        Check(!session.OpenDraft(), "future scan rejected");
        Check(!SystemReportSession.IsFresh(null, scanned, scanned) && !SystemReportSession.IsFresh(Fixture(), null, scanned), "missing scan rejected");
        Check(!SystemReportSession.IsFresh(Fixture() with { Devices = [] }, scanned, scanned), "empty scan rejected");
        foreach (var outcome in new[] { "cancel", "false", "throw" })
        {
            var failure = new SystemReportSession(Fixture(), scanned, () => scanned,
                _ => outcome == "throw" ? throw new IOException("private browser path") : false,
                _ => outcome == "throw" ? throw new IOException("private file path") : outcome == "cancel" ? null : false);
            failure.Consent = true;
            Check(!failure.OpenDraft() && !failure.Status.Contains("private") && !failure.Consent, "browser false/exception safe error");
            Check(!failure.Export() && !failure.Status.Contains("private"), "export cancellation/false/exception safe error");
            if (outcome == "cancel") Check(failure.Status.Contains("abgebrochen"), "cancel distinguished from error");
        }
        Console.WriteLine("PASS: consent, exact preview/export/draft identity, edits, URL cap, offline export, invalid input and freshness");
    }

    public static async Task<string> RunUi(string outputDirectory)
    {
        var type = typeof(SystemReport).Assembly.GetType("DriverPilot.SystemReportWindow");
        Check(type != null, "report dialog exists");
        int opened = 0; string? saved = null; Uri? draft = null;
        var now = DateTimeOffset.UtcNow;
        var scan = Fixture(); var context = new ReportScanContext();
        var generation = context.BeginScan(); context.CompleteScan(generation, scan, now);
        context.AcceptManufacturers(generation, scan, new(now.LocalDateTime, new("private", "", "", [], ""),
            [new("private name", "Grafiktreiber", "private", "private", "", "Angebotene Version installiert", "https://private.invalid", "private error")]), now);
        var session = new SystemReportSession(scan, now, () => now,
            uri => { opened++; draft = uri; return true; }, text => { saved = text; return true; }, context.SummaryFor(scan, now));
        var window = (Window)Activator.CreateInstance(type!, session)!;
        try
        {
            window.Show();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            // Force collection after an async boundary: the test must model MainWindow's strong owner.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            T Get<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
            void Click(string name) => Get<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i); yield return child;
                    foreach (var nested in Descendants(child)) yield return nested;
                }
            }
            var warning = string.Join(" ", Descendants(window).OfType<TextBlock>().Select(t => t.Text));
            Check(warning.Contains("Repository ist derzeit privat") && warning.Contains("GitHub-Konto und Zugriffsrechte sind erforderlich") && !warning.Contains("privat?"), "unambiguous private repository warning");
            Check(opened == 0 && saved == null, "opening dialog has no side effects");
            Check(Get<TextBox>("ReportPreview").Text == session.Report!.Text && Get<TextBox>("ReportPreview").IsReadOnly, "read-only preview is exact report");
            Check(Get<TextBox>("ReportPreview").Text.Contains("Graphics: Supported (1)") && !Get<TextBox>("ReportPreview").Text.Contains("private"), "UI includes only bound manufacturer projection");
            Check(!Get<Button>("OpenDraftButton").IsEnabled, "browser action disabled without consent");
            Get<TextBox>("ProblemNote").Text = "Bild flackert";
            Get<CheckBox>("ShareConsent").IsChecked = true;
            Get<TextBox>("ExpectedNote").Text = "Ruhiges Bild";
            Check(Get<CheckBox>("ShareConsent").IsChecked == false && !Get<Button>("OpenDraftButton").IsEnabled, "UI edit clears consent");
            Click("SaveReportButton");
            Check(saved == Get<TextBox>("ReportPreview").Text, "UI export equals displayed preview");
            Get<CheckBox>("ShareConsent").IsChecked = true; Click("OpenDraftButton");
            Check(opened == 1 && Uri.UnescapeDataString(draft!.Query.Split("&body=")[1]) == saved, "UI browser handler uses exact preview");
            Check(!Get<Button>("OpenDraftButton").IsEnabled, "UI clears consumed consent");
            Directory.CreateDirectory(outputDirectory);
            foreach (var (width, height, file) in new[] { (980d, 820d, "system-report-default.png"), (720d, 650d, "system-report-minimum.png") })
            {
                window.Width = width; window.Height = height;
                await window.Dispatcher.InvokeAsync(() => { window.UpdateLayout(); }, DispatcherPriority.ApplicationIdle);
                foreach (var name in new[] { "ReportPreview", "ShareConsent", "SaveReportButton", "OpenDraftButton", "CloseReportButton", "ReportStatus" })
                {
                    var control = Get<FrameworkElement>(name); var position = control.TransformToAncestor(window).Transform(new Point());
                    Check(control.ActualHeight > 0 && position.X >= 0 && position.Y >= 0 && position.X + control.ActualWidth <= window.ActualWidth && position.Y + control.ActualHeight <= window.ActualHeight, "visible control at " + width + ": " + name);
                }
                Check(Get<TextBox>("ReportPreview").ActualHeight >= 110, "usable preview at minimum geometry");
                var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(outputDirectory, file)); encoder.Save(stream);
            }
            Get<TextBox>("ActualNote").Text = new string('ä', 990);
            Get<CheckBox>("ShareConsent").IsChecked = true; Click("OpenDraftButton");
            Check(opened == 1 && Get<TextBlock>("ReportStatus").Text.Contains("lang"), "UI oversized draft fallback");
            Click("SaveReportButton"); Check(saved == session.Report!.Text, "UI oversized complete export");
            Click("CloseReportButton"); Check(!window.IsVisible, "cancel closes without additional transmission");
        }
        finally { window.Close(); GC.KeepAlive(context); }
        return "PASS: displayed dialog, synthetic handlers, exact preview/export/draft, consent/edit/oversize/cancel, default/minimum geometry and screenshots";
    }
}