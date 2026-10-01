using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DriverPilot.RemoteDiagnosis;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
namespace DriverPilot;

/// <summary>Windows-only parts of the remote diagnosis: DPAPI and the dialog. The rules themselves are tested in tests/RemoteDiagnosis.</summary>
public static class RemoteDiagnosisTests
{
    sealed class MemoryStore : IRemoteStateStore { RemoteState state = new(); public RemoteState Load() => state; public void Save(RemoteState next) => state = next; }
    sealed class NoNetwork : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) { Requests++; throw new HttpRequestException("offline"); }
    }
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); }

    public static string Run(Snapshot snapshot)
    {
        const string secret = "SYNTHETIC-access-token-not-a-secret-000000000000";
        var protector = new DpapiProtector();
        var stored = protector.Protect(secret);
        Check(!stored.Contains(secret) && !Encoding.UTF8.GetString(Convert.FromBase64String(stored)).Contains(secret), "DPAPI output does not contain the token");
        Check(protector.Unprotect(stored) == secret, "DPAPI round trip for the current user");
        Check(protector.Unprotect("not base64") == null && protector.Unprotect(Convert.ToBase64String(new byte[64])) == null, "unreadable protected value yields null");

        var network = new NoNetwork();
        var session = new RemoteDiagnosisSession(new MemoryStore(), protector, origin => new RemoteApiClient(network, origin, "0.0.0", ContractRules.Embedded), () => DateTimeOffset.UtcNow, ContractRules.Embedded, "0.0.0");
        var window = new RemoteDiagnosisWindow(session, new RemoteScan(snapshot, Guid.NewGuid(), DateTimeOffset.UtcNow));
        var surface = (FrameworkElement)window.Content;
        surface.Measure(new System.Windows.Size(1000, 860)); surface.Arrange(new Rect(0, 0, 1000, 860)); surface.UpdateLayout();
        T Find<T>(string name) where T : class => window.FindName(name) as T ?? throw new Exception("FAIL: missing control " + name);

        var send = Find<Button>("RemoteSend"); var export = Find<Button>("RemoteExport"); var preview = Find<TextBox>("RemotePreview"); var consent = Find<CheckBox>("RemoteConsent");
        Check(!send.IsEnabled && !export.IsEnabled && consent.IsChecked != true, "nothing can be sent or exported before a description exists");
        Find<TextBox>("RemoteDescription").Text = "Synthetischer Testfall: Bild friert nach 20 Minuten ein.";
        Check(preview.Text.Contains("\"schema_version\": \"1.0\"") && preview.Text.Contains("Synthetischer Testfall") && !preview.Text.Contains("consent"), "preview shows the real projection of this PC without consent");
        Check(export.IsEnabled && !send.IsEnabled, "unpaired: preview can be saved locally but not sent");
        consent.IsChecked = true;
        Check(!send.IsEnabled, "consent alone does not enable sending without access and privacy notice");
        Find<TextBox>("RemoteDescription").Text = "Geänderter Text.";
        Check(consent.IsChecked != true, "editing withdraws the consent");
        Find<TextBox>("RemoteDescription").Text = @"Log unter C:\Users\someone\wow.log";
        Check(!export.IsEnabled && Find<TextBlock>("RemoteStatus").Text.Contains("persönliche Daten"), "text with a path gives no preview");
        Check(Find<TextBox>("RemoteNotice").IsReadOnly && Find<TextBox>("RemoteResult").IsReadOnly && preview.IsReadOnly, "server texts are shown in read-only plain text boxes");
        Check(network.Requests == 0, "constructing and editing the dialog causes no network request");
        window.Close();
        return "Remote diagnosis: DPAPI token protection, dialog layout, preview from the real scan, consent withdrawal on edit, no request without click.";
    }
}
