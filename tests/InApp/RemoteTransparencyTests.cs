using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DriverPilot.RemoteDiagnosis;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
namespace DriverPilot;

public static class RemoteTransparencyTests
{
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); }
    static readonly DateTimeOffset ScanTime = new(2026, 10, 3, 14, 2, 11, TimeSpan.Zero);
    sealed class Memory : IRemoteStateStore
    {
        RemoteState state = new(new("https://synthetic.example.invalid:8443/", "5f0c2a7e-3b1d-4c8a-9e21-7a4d0b6c1f02", "fixture-protected", "2027-01-01T00:00:00Z"));
        public RemoteState Load() => state;
        public string? DeniedPath;
        public Exception? Failure;
        public void Save(RemoteState next)
        {
            if (DeniedPath != null) File.WriteAllText(DeniedPath, "synthetic"); // a directory: real Windows write denial
            if (Failure != null) throw Failure;
            state = next;
        }
    }
    sealed class Secret : ISecretProtector
    {
        public string Protect(string value) => "fixture-protected";
        public string? Unprotect(string value) => "SYNTHETIC-access-token-not-a-secret-000000000000";
    }
    sealed class Server : HttpMessageHandler
    {
        public List<(string Path, byte[] Body, string? Key)> Calls = [];
        public TaskCompletionSource? Hold;
        public bool Accept;
        public bool FailCapabilities;
        public HashSet<string> AcceptedFeedbackKeys = [];
        string sha = "";
        const string CaseId = "5f0c2a7e-3b1d-4c8a-9e21-7a4d0b6c1f02";
        static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status) { Content = new StringContent(JsonSerializer.Serialize(value, RemoteReportBuilder.Json), Encoding.UTF8, "application/json") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            var body = request.Content == null ? [] : await request.Content.ReadAsByteArrayAsync(cancel);
            Calls.Add((request.RequestUri!.AbsoluteUri, body, request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.Single() : null));
            if (request.Method == HttpMethod.Post)
            {
                if (request.RequestUri.AbsolutePath.EndsWith("/pairings/redeem"))
                    return Json(HttpStatusCode.Created, new PairingResponse(CaseId, "SYNTHETIC-access-token-not-a-secret-000000000000", "2027-01-01T00:00:00Z"));
                if (Accept && request.RequestUri.AbsolutePath.EndsWith("/cases"))
                {
                    sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(body));
                    return Json(HttpStatusCode.Accepted, new CaseAccepted(CaseId, "queued", "2026-10-03T14:05:00Z", "2026-10-10T14:05:00Z", sha, 10));
                }
                if (request.RequestUri.AbsolutePath.EndsWith("/feedback")) AcceptedFeedbackKeys.Add(Calls[^1].Key!); // committed, response lost
                if (Hold != null) await Hold.Task.WaitAsync(cancel);
                throw new HttpRequestException("SYNTHETIC private raw response must not be displayed");
            }
            if (request.RequestUri.AbsolutePath.Contains("/cases/"))
            {
                var result = new CaseResult("1.0", CaseId, "c1a9d3e4-52b7-4f60-8a1c-0d9e8f7a6b54", sha, 1, "2026-10-03T14:05:00Z", "human", "Synthetisches Ergebnis <script>inert</script>", [], [], [], [], [], []);
                return Json(HttpStatusCode.OK, new CaseView(CaseId, "released", null, result.ScanId, sha, "2026-10-03T14:05:00Z", "2026-10-10T14:05:00Z", null, result));
            }
            if (FailCapabilities) throw new HttpRequestException("PRIVATE capability failure");
            var caps = new Capabilities("1.0", ["1.0"], "1.0", 262144, 900, 300, 7, new("2026-10-01.1", "Synthetischer Hinweis <script>inert</script>"), true);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(caps, RemoteReportBuilder.Json), Encoding.UTF8, "application/json") };
        }
    }
    static async Task Pump() => await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    static async Task Wait(Func<bool> ready)
    {
        for (var i = 0; i < 300 && !ready(); i++) await Task.Delay(10);
        Check(ready(), "async handler completed within test deadline");
    }
    static async Task Geometry(RemoteDiagnosisWindow window, string output)
    {
        await RemoteLayoutTests.Run(window, output);
        foreach (var (width, height, name) in new[] { (1000d, 860d, "default"), (760d, 620d, "minimum") })
        {
            window.Width = width; window.Height = height; window.UpdateLayout(); await Pump();
            foreach (var control in new[] { "RemotePreview", "RemoteReview", "RemoteSend", "RemoteExport", "RemoteConsent", "RemoteExternalAi" })
            {
                var element = (FrameworkElement)window.FindName(control);
                element.BringIntoView(); window.UpdateLayout(); await Pump();
                var point = element.TransformToAncestor(window).Transform(new System.Windows.Point());
                Check(element.ActualWidth > 0 && element.ActualHeight > 0 && point.X >= 0 && point.Y >= 0 && point.X + element.ActualWidth <= window.ActualWidth && point.Y + element.ActualHeight <= window.ActualHeight, name + " scroll-reachable geometry " + control);
            }
            ((FrameworkElement)window.FindName("RemotePreview")).BringIntoView(); window.UpdateLayout(); await Pump();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(output, "remote-preview-" + name + ".png"))) png.Save(file);
            var preview = (TextBox)window.FindName("RemotePreview");
            Check(preview.IsReadOnly && preview.VerticalScrollBarVisibility == System.Windows.Controls.ScrollBarVisibility.Auto, "full payload is read-only and scrollable");
            preview.ScrollToEnd(); window.UpdateLayout(); await Pump();
            bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(output, "remote-consent-" + name + ".png"))) png.Save(file);
            preview.ScrollToHome();
        }
    }
    public static async Task<string> Run(string output)
    {
        Directory.CreateDirectory(output);
        // Keep the legacy real-scan dialog assertions covered by the isolated harness too.
        _ = RemoteDiagnosisTests.Run(SystemReportTests.Fixture());
        var server = new Server();
        var now = ScanTime.AddMinutes(2);
        var memory = new Memory();
        var session = new RemoteDiagnosisSession(memory, new Secret(), origin => new RemoteApiClient(server, origin, "0.3.5", ContractRules.Embedded), () => now, ContractRules.Embedded, "0.3.5");
        string? exported = null;
        var window = new RemoteDiagnosisWindow(session, new(SystemReportTests.Fixture(), Guid.Parse("c1a9d3e4-52b7-4f60-8a1c-0d9e8f7a6b54"), ScanTime), text =>
        {
            exported = text; File.WriteAllText(Path.Combine(output, "reviewed-report.json"), text, new UTF8Encoding(false)); return true;
        });
        T Get<T>(string name) where T : class => (T)window.FindName(name);
        try
        {
            window.Show(); await Pump();
            Get<TextBox>("RemoteDescription").Text = "Spiel stürzt ab";
            var consent = Get<CheckBox>("RemoteConsent");
            consent.IsChecked = true;
            Get<CheckBox>("RemoteExternalAi").IsChecked = true;
            Check(consent.IsChecked == false, "AI checkbox invalidates prior approval through productive handler");
            Check(server.Calls.All(c => c.Body.Length == 0), "open and edits never upload diagnostic report");
            var review = window.FindName("RemoteReview") as Button;
            Check(review != null, "separate local review button exists before sending");
            consent.IsChecked = true;
            review!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var preview = Get<TextBox>("RemotePreview").Text;
            Check(session.HasFinalPreview && preview.Contains("\"consent\"") && preview.Contains("\"external_ai_allowed\": true"), "review handler shows full final consent JSON");
            Check(server.Calls.All(c => c.Body.Length == 0), "review does not upload");
            Get<Button>("RemoteExport").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(exported == preview && File.ReadAllBytes(Path.Combine(output, "reviewed-report.json")).SequenceEqual(Encoding.UTF8.GetBytes(preview)), "productive export handler writes exact final UTF-8 without BOM");
            await Geometry(window, output);
            var send = Get<Button>("RemoteSend");
            Check(send.IsEnabled, "separate explicit send enabled only after review");
            server.Hold = new();
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Pump();
            foreach (var name in new[] { "RemoteDescription", "RemoteSteps", "RemoteCategory", "RemoteExternalAi", "RemoteConsent", "RemoteReview", "RemoteUnpair", "RemoteExport", "RemoteServer" })
                Check(!Get<UIElement>(name).IsEnabled, "busy disables " + name);
            Check(Get<TextBox>("RemotePreview").Text == preview && Encoding.UTF8.GetString(server.Calls[^1].Body) == preview, "in-flight displayed UTF-8 body identity");
            server.Hold.SetResult(); await Wait(() => Get<TextBox>("RemoteDescription").IsEnabled);
            Check(session.HasPendingUpload && !session.Status.Contains("private raw"), "failed upload retains reviewed request and hides raw error");
            var first = server.Calls[^1];
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => Get<TextBox>("RemoteDescription").IsEnabled);
            Check(server.Calls[^1].Body.SequenceEqual(first.Body) && server.Calls[^1].Key == first.Key, "productive retry reuses exact bytes and key");
            Get<CheckBox>("RemoteExternalAi").IsChecked = false;
            Check(!session.HasFinalPreview && !session.HasPendingUpload && consent.IsChecked == false && !send.IsEnabled, "AI edit clears pending retry and approval");
            consent.IsChecked = true; review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var disclosure = Get<System.Windows.Controls.TextBlock>("RemoteDisclosure").Text;
            foreach (var expected in new[] { "https://synthetic.example.invalid:8443/api/v1/cases", "7", "IP", "Proxy", "Token", "Geräte", "Befunde", "consent", "2026-10-01.1", "/api/v1/capabilities" })
                Check(disclosure.Contains(expected), "understandable disclosure includes " + expected);
            foreach (var secret in new[] { "Alice", "SERIAL", "private", "SYNTHETIC-access", "legacy-" })
                Check(!Get<TextBox>("RemotePreview").Text.Contains(secret) && !disclosure.Contains(secret), "no hidden source field/secret " + secret);
            server.Hold = new();
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Pump();
            Get<Button>("RemoteCancelUpload").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => Get<TextBox>("RemoteDescription").IsEnabled);
            Check(session.HasPendingUpload && session.Status.Contains("Abgebrochen"), "cancel explains uncertain outcome and preserves exact retry");
            var count = server.Calls.Count;
            Get<TextBox>("RemoteSteps").Text = "Neuer Schritt";
            Check(!session.HasFinalPreview && !send.IsEnabled && consent.IsChecked == false, "text edit withdraws approval after cancellation");
            consent.IsChecked = true; review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            consent.IsChecked = false;
            Check(!session.HasFinalPreview && !send.IsEnabled, "withdrawing consent discards frozen bytes");
            consent.IsChecked = true; review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            now = ScanTime.AddMinutes(16);
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => Get<TextBox>("RemoteDescription").IsEnabled);
            Check(server.Calls.Count == count && session.Preview == null, "stale final preview fails closed before network");
            await Task.Delay(100);
            Check(server.Calls.Count == count, "cancel has no hidden follow-on upload");
            now = ScanTime.AddMinutes(2); server.Hold = null; server.Accept = true;
            Get<TextBox>("RemoteDescription").Text = "Neuer Testfall";
            consent.IsChecked = true; review.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            memory.DeniedPath = output;
            var reviewed = session.Preview;
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => Get<TextBox>("RemoteDescription").IsEnabled);
            Check(session.HasPendingUpload && session.CaseRef == null && session.Preview == reviewed && Get<System.Windows.Controls.TextBlock>("RemoteStatus").Text.Contains("Lokales Speichern"), "productive async upload survives real Windows disk write denial after acceptance");
            var acceptedAttempt = server.Calls[^1];
            memory.DeniedPath = null;
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => Get<TextBox>("RemoteDescription").IsEnabled);
            Check(server.Calls[^1].Key == acceptedAttempt.Key && server.Calls[^1].Body.SequenceEqual(acceptedAttempt.Body), "productive recovery resends accepted bytes/key");
            Check(Get<System.Windows.Controls.TextBlock>("RemotePreviewStage").Text.StartsWith("ENDGÜLTIGES JSON") && !send.IsEnabled && !review.IsEnabled, "successful upload keeps exact final preview visibly final, not falsely labelled consent:null draft");
            Get<Button>("RemoteRefresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => Get<TextBox>("RemoteDescription").IsEnabled);
            Check(Get<TextBox>("RemoteResult").Text.Contains("<script>inert</script>") && Get<TextBox>("RemoteResult").IsReadOnly, "server result remains inert plain text");
            var feedbackReview = window.FindName("RemoteFeedbackReview") as Button;
            Check(feedbackReview != null, "feedback also has separate full payload review");
            var feedbackButton = Get<Button>("RemoteFeedback");
            Check(!feedbackButton.IsEnabled, "no feedback upload before final preview");
            feedbackReview!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var feedbackJson = Get<TextBox>("RemoteFeedbackPreview").Text;
            Check(feedbackJson == session.FeedbackPreview && feedbackJson.Contains("result_revision"), "feedback includes otherwise hidden result revision");
            feedbackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => Get<TextBox>("RemoteDescription").IsEnabled);
            Check(Encoding.UTF8.GetString(server.Calls[^1].Body) == feedbackJson, "feedback sends only reviewed payload");
            var uncertainFeedback = server.Calls[^1];
            feedbackReview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            feedbackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => Get<TextBox>("RemoteDescription").IsEnabled);
            Check(server.Calls[^1].Key == uncertainFeedback.Key && server.Calls[^1].Body.SequenceEqual(uncertainFeedback.Body), "productive unchanged re-review preserves uncertain feedback key and bytes");
            Check(server.AcceptedFeedbackKeys.Count == 1, "response-lost acceptance plus re-review creates only one server feedback");
            Get<TextBox>("RemoteFeedbackNote").Text = "Neue Rückmeldung";
            Check(!feedbackButton.IsEnabled && session.FeedbackPreview == null, "feedback edits invalidate preview and retry");
            var beforeChangedSend = server.Calls.Count;
            feedbackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => Get<TextBox>("RemoteDescription").IsEnabled);
            Check(server.Calls.Count == beforeChangedSend, "changed feedback cannot silently retry old approved body");
            feedbackReview.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var changedFeedback = session.FeedbackPreview;
            feedbackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => Get<TextBox>("RemoteDescription").IsEnabled);
            Check(server.Calls[^1].Key != uncertainFeedback.Key && Encoding.UTF8.GetString(server.Calls[^1].Body) == changedFeedback && server.AcceptedFeedbackKeys.Count == 2, "explicit new review sends changed feedback as a new request");
            server.FailCapabilities = true;
            var restored = new RemoteDiagnosisSession(memory, new Secret(), origin => new RemoteApiClient(server, origin, "0.3.5", ContractRules.Embedded), () => now, ContractRules.Embedded, "0.3.5");
            var restoredWindow = new RemoteDiagnosisWindow(restored, null);
            try
            {
                restoredWindow.Show();
                await Wait(() => restored.Case?.Result != null && ((Button)restoredWindow.FindName("RemoteRefresh")).IsEnabled);
                Check(!((Button)restoredWindow.FindName("RemoteFeedbackReview")).IsEnabled && !((Button)restoredWindow.FindName("RemoteFeedback")).IsEnabled, "restored released case cannot review/send after capabilities failure");
            }
            finally { restoredWindow.Close(); }
            server.FailCapabilities = false;
            memory.Failure = new InvalidOperationException("PRIVATE unexpected store");
            Get<Button>("RemoteUnpair").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(session.Paired && !Get<System.Windows.Controls.TextBlock>("RemoteStatus").Text.Contains("PRIVATE"), "productive unpair contains injected store exception");
            memory.Failure = null;
            Get<Button>("RemoteUnpair").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!session.Paired && session.Preview == null && !send.IsEnabled, "productive unpair invalidates preview and access");
            Get<TextBox>("RemoteServer").Text = "https://synthetic.example.invalid:8443";
            Get<System.Windows.Controls.PasswordBox>("RemoteInvitation").Password = "SYNTHETIC-invitation-not-a-secret-0000";
            Get<Button>("RemotePair").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => Get<TextBox>("RemoteDescription").IsEnabled);
            Check(session.Paired && session.Preview != null && !session.HasFinalPreview && !send.IsEnabled && consent.IsChecked == false, "pair handler rebuilds existing edited draft after work, never carries prior approval");
            var work = typeof(RemoteDiagnosisWindow).GetMethod("Work", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            await (Task)work.Invoke(window, new object[] { (Func<Task>)(() => throw new InvalidOperationException("PRIVATE boundary")), "synthetic" })!;
            Check(Get<TextBox>("RemoteDescription").IsEnabled && Get<System.Windows.Controls.TextBlock>("RemoteStatus").Text.Contains("Vorgang nicht abgeschlossen") && !session.Status.Contains("PRIVATE"), "productive Work boundary contains unexpected failures and restores idle UI");
            return "PASS: isolated remote UI review, exact UTF-8 export/send/retry, geometry, disclosure, cancellation, edits, stale scans, pairing and feedback review";
        }
        finally { window.Close(); }
    }
}
