using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DriverPilot.RemoteDiagnosis;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using FontFamily = System.Windows.Media.FontFamily;
using Color = System.Windows.Media.Color;
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
namespace DriverPilot;

/// <summary>A fresh local scan the dialog may build a report from.</summary>
public sealed record RemoteScan(Snapshot Snapshot, Guid ScanId, DateTimeOffset ScannedAt);

/// <summary>
/// "Hilfe von Christian". Nothing is sent on open or on scan: every upload needs the consent box and a click.
/// The window only renders the session and forwards clicks; all rules live in RemoteDiagnosisSession.
/// </summary>
public sealed class RemoteDiagnosisWindow : Window
{
    static readonly string[] Outcomes = ["improved", "unchanged", "worse", "not_tried"];
    static readonly string[] OutcomeLabels = ["Besser geworden", "Unverändert", "Schlechter geworden", "Noch nicht ausprobiert"];
    static readonly string[] ScopeLabels = ["Nur in WoW", "Auch in anderen Programmen", "Weiß ich nicht"];
    static readonly string[] AddonLabels = ["Ja, ohne Addons getestet", "Nein", "Weiß ich nicht"];

    readonly RemoteDiagnosisSession session;
    readonly RemoteScan? scan;
    readonly DispatcherTimer poll = new();
    readonly Random jitter = new();
    bool working, rendering;

    readonly TextBlock accessStatus = Note(), caseStatus = Note(), status = Note();
    readonly TextBox server = Input(), description = Input(true, 90), steps = Input(true, 70), gameBuild = Input(), feedbackNote = Input(true, 60);
    readonly PasswordBox invitation = new() { Padding = new Thickness(10), FontSize = 14, Margin = new Thickness(0, 0, 0, 10) };
    readonly ComboBox category = Choice(RemoteReportBuilder.Categories.Select(RemoteText.Category)), scope = Choice(ScopeLabels, 2), addons = Choice(AddonLabels, 2), outcome = Choice(OutcomeLabels, 1);
    readonly CheckBox wow = new() { Content = Label("Angaben zu World of Warcraft mitsenden") }, externalAi = new(), consent = new();
    readonly TextBox notice = ReadOnly(110, false), preview = ReadOnly(230, true), result = ReadOnly(280, false);
    readonly Button pair = new() { Content = "Verbinden" }, unpair = new() { Content = "Zugang entfernen" };
    readonly Button send = new() { Content = "Zustimmen und senden", Background = new SolidColorBrush(Color.FromRgb(54, 107, 232)) }, export = new() { Content = "Vorschau lokal speichern …" };
    readonly Button refresh = new() { Content = "Aktualisieren" }, feedback = new() { Content = "Rückmeldung senden" }, delete = new() { Content = "Fall auf dem Server löschen" };
    readonly StackPanel pairingPanel = new(), reportPanel = new(), wowPanel = new(), casePanel = new(), feedbackPanel = new();

    public RemoteDiagnosisWindow(RemoteScan? scan)
        : this(new RemoteDiagnosisSession(new FileRemoteStateStore(), new DpapiProtector(),
            origin => new RemoteApiClient(RemoteApiClient.CreateHandler(), origin, AppVersion, ContractRules.Embedded), () => DateTimeOffset.UtcNow, ContractRules.Embedded, AppVersion), scan) { }

    public RemoteDiagnosisWindow(RemoteDiagnosisSession session, RemoteScan? scan)
    {
        this.session = session; this.scan = scan;
        Title = "DriverPilot · Hilfe von Christian"; Width = 1000; Height = 860; MinWidth = 760; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(11, 20, 35));
        NameScope.SetNameScope(this, new NameScope());
        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 16) }; Content = root;

        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var close = new Button { Content = "Schließen", IsCancel = true, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        close.Click += (_, _) => Close();
        bottom.Children.Add(status); bottom.Children.Add(close);

        var page = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        page.Children.Add(new TextBlock { Text = "Hilfe von Christian", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        page.Children.Add(Note("Du kannst Christian einen datensparsamen Diagnosebericht schicken. Er prüft ihn und gibt Empfehlungen frei, die hier erscheinen. Nichts wird automatisch gesendet, DriverPilot führt keine Empfehlung selbst aus, und die lokale Diagnose funktioniert weiter ohne Server."));

        page.Children.Add(Heading("1 · Zugang"));
        page.Children.Add(accessStatus);
        pairingPanel.Children.Add(Label("Serveradresse (von Christian, beginnt mit https://)")); pairingPanel.Children.Add(server);
        pairingPanel.Children.Add(Label("Einladungscode (einmalig, 24 Stunden gültig)")); pairingPanel.Children.Add(invitation);
        pairingPanel.Children.Add(pair);
        page.Children.Add(pairingPanel); page.Children.Add(unpair);

        page.Children.Add(reportPanel);
        reportPanel.Children.Add(Heading("2 · Problem beschreiben"));
        reportPanel.Children.Add(Label("Art des Problems")); reportPanel.Children.Add(category);
        reportPanel.Children.Add(Label("Was passiert? (Pflicht, höchstens 2000 Zeichen, bitte ohne Namen, Pfade, Adressen oder Passwörter)")); reportPanel.Children.Add(description);
        reportPanel.Children.Add(Label("Wie lässt es sich auslösen? (optional)")); reportPanel.Children.Add(steps);
        reportPanel.Children.Add(wow);
        wowPanel.Children.Add(Label("Spielversion, zum Beispiel 11.2.5 (optional)")); wowPanel.Children.Add(gameBuild);
        wowPanel.Children.Add(Label("Tritt das Problem nur in WoW auf?")); wowPanel.Children.Add(scope);
        wowPanel.Children.Add(Label("Schon ohne Addons getestet?")); wowPanel.Children.Add(addons);
        reportPanel.Children.Add(wowPanel);

        reportPanel.Children.Add(Heading("3 · Prüfen und zustimmen"));
        reportPanel.Children.Add(Label("Datenschutzhinweis des Servers")); reportPanel.Children.Add(notice);
        reportPanel.Children.Add(Label("Vorschau · genau diese Daten werden gesendet. Automatische Filter erkennen nicht alles: bitte selbst prüfen."));
        reportPanel.Children.Add(preview);
        externalAi.Content = Label("Zusätzlich erlauben: Eine externe KI darf einen Entwurf erstellen, den Christian prüft. Ohne Haken bearbeitet nur Christian den Bericht.");
        consent.Content = Label("Ich habe die gesamte Vorschau und den Datenschutzhinweis gelesen und stimme dem Senden dieses Berichts an Christian zu.");
        reportPanel.Children.Add(externalAi); reportPanel.Children.Add(consent);
        var sendActions = new WrapPanel(); sendActions.Children.Add(send); sendActions.Children.Add(export); reportPanel.Children.Add(sendActions);

        page.Children.Add(casePanel);
        casePanel.Children.Add(Heading("4 · Dein Fall"));
        casePanel.Children.Add(caseStatus); casePanel.Children.Add(result);
        var caseActions = new WrapPanel { Margin = new Thickness(0, 10, 0, 10) }; caseActions.Children.Add(refresh); caseActions.Children.Add(delete); casePanel.Children.Add(caseActions);
        feedbackPanel.Children.Add(Label("Rückmeldung: Wie ist es nach den Empfehlungen?")); feedbackPanel.Children.Add(outcome);
        feedbackPanel.Children.Add(Label("Notiz (optional)")); feedbackPanel.Children.Add(feedbackNote); feedbackPanel.Children.Add(feedback);
        casePanel.Children.Add(feedbackPanel);

        foreach (var (name, element) in new (string, FrameworkElement)[] { ("RemoteServer", server), ("RemoteInvitation", invitation), ("RemotePair", pair), ("RemoteUnpair", unpair), ("RemoteCategory", category),
            ("RemoteDescription", description), ("RemoteSteps", steps), ("RemoteNotice", notice), ("RemotePreview", preview), ("RemoteExternalAi", externalAi), ("RemoteConsent", consent), ("RemoteSend", send),
            ("RemoteExport", export), ("RemoteResult", result), ("RemoteRefresh", refresh), ("RemoteDelete", delete), ("RemoteFeedback", feedback), ("RemoteStatus", status), ("RemoteCaseStatus", caseStatus) })
            RegisterName(name, element);

        // Any edit rebuilds the preview, which withdraws the consent.
        description.TextChanged += (_, _) => Rebuild(); steps.TextChanged += (_, _) => Rebuild(); gameBuild.TextChanged += (_, _) => Rebuild();
        category.SelectionChanged += (_, _) => Rebuild(); scope.SelectionChanged += (_, _) => Rebuild(); addons.SelectionChanged += (_, _) => Rebuild();
        wow.Checked += (_, _) => Rebuild(); wow.Unchecked += (_, _) => Rebuild();
        consent.Checked += (_, _) => Render(); consent.Unchecked += (_, _) => Render();

        pair.Click += async (_, _) => await Work(async () =>
        {
            if (await session.PairAsync(server.Text, invitation.Password)) { invitation.Clear(); await session.LoadCapabilitiesAsync(); Rebuild(); }
        });
        unpair.Click += (_, _) => { poll.Stop(); session.Unpair(); Render(); };
        send.Click += async (_, _) => await Work(async () => { if (consent.IsChecked == true && await session.ApproveAndSendAsync(externalAi.IsChecked == true)) await session.RefreshAsync(); });
        export.Click += (_, _) => { session.Export(SaveFile); Render(); };
        refresh.Click += async (_, _) => await Work(() => session.RefreshAsync());
        feedback.Click += async (_, _) => await Work(async () => { if (await session.SendFeedbackAsync(Outcomes[outcome.SelectedIndex], feedbackNote.Text)) feedbackNote.Clear(); });
        delete.Click += async (_, _) =>
        {
            if (MessageBox.Show(this, "Bericht, Ergebnis und Rückmeldungen dieses Falls auf dem Server löschen?", "DriverPilot", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await Work(() => session.DeleteCaseAsync());
        };
        poll.Tick += async (_, _) => { poll.Stop(); await Work(() => session.RefreshAsync()); };
        Loaded += async (_, _) => await Work(async () =>
        {
            if (!session.Paired) return;
            if (await session.LoadCapabilitiesAsync()) Rebuild();
            if (session.CaseRef != null) await session.RefreshAsync();
        });
        Closed += (_, _) => poll.Stop();
        Render();
    }

    static string AppVersion => typeof(RemoteDiagnosisWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>One network operation at a time; the UI stays responsive and is redrawn afterwards.</summary>
    async Task Work(Func<Task> action)
    {
        if (working) return;
        working = true; Render();
        try { await action(); }
        finally { working = false; Render(); SchedulePoll(); }
    }

    void SchedulePoll()
    {
        poll.Stop();
        if (!IsLoaded || session.NextPoll(jitter.NextDouble()) is not { } delay) return;
        poll.Interval = delay; poll.Start();
    }

    void Rebuild()
    {
        if (rendering || working) return;
        // The preview needs no server: it can always be saved locally as the offline fallback.
        if (scan != null)
            session.Prepare(scan.Snapshot, scan.ScanId, scan.ScannedAt, new SymptomInput(RemoteReportBuilder.Categories[category.SelectedIndex], description.Text, steps.Text,
                wow.IsChecked == true ? new WowInput(gameBuild.Text, RemoteReportBuilder.ProblemScopes[scope.SelectedIndex], RemoteReportBuilder.AddonTests[addons.SelectedIndex]) : null));
        consent.IsChecked = false;
        Render();
    }

    void Render()
    {
        rendering = true;
        try
        {
            var paired = session.Paired;
            accessStatus.Text = paired ? $"Verbunden mit {session.ServerHost}." : "Noch nicht verbunden. Christian schickt dir Serveradresse und Einladungscode auf einem sicheren Weg.";
            pairingPanel.Visibility = Visible(!paired); unpair.Visibility = Visible(paired);
            wowPanel.Visibility = Visible(wow.IsChecked == true);
            notice.Text = session.Capabilities?.PrivacyNotice.Text ?? (paired ? "Der Datenschutzhinweis konnte noch nicht geladen werden. Ohne ihn wird nichts gesendet." : "Nach dem Verbinden erscheint hier der Datenschutzhinweis des Servers. Ohne Verbindung lässt sich die Vorschau nur lokal speichern.");
            preview.Text = session.Preview ?? (scan == null ? "Für einen neuen Bericht zuerst den Dialog schließen und 'Jetzt prüfen' ausführen. Der Scan darf höchstens 15 Minuten alt sein."
                : session.HasPendingUpload ? "Der Bericht wurde bereits zum Senden freigegeben und wird unverändert erneut gesendet." : "Noch keine gültige Vorschau.");
            externalAi.Visibility = Visible(session.Capabilities?.ExternalAiOffered == true);
            if (session.Preview == null && !session.HasPendingUpload) consent.IsChecked = false;
            send.Content = session.HasPendingUpload ? "Unverändert erneut senden" : "Zustimmen und senden";
            send.IsEnabled = !working && session.CanSend && (consent.IsChecked == true || session.HasPendingUpload);
            export.IsEnabled = session.Preview != null;
            pair.IsEnabled = !working; refresh.IsEnabled = !working; delete.IsEnabled = !working;

            var reference = session.CaseRef;
            casePanel.Visibility = Visible(paired && reference != null);
            var released = session.Case?.Result;
            caseStatus.Text = reference == null ? "" : $"Gesendeter Scan vom {Local(reference.ScannedAt)} · wird am {Local(reference.ExpiresAt)} gelöscht."
                + (session.IsCaseForOlderScan(scan?.ScannedAt) ? "\nAchtung: Es gibt inzwischen einen neueren lokalen Scan. Dieses Ergebnis gehört zum älteren Scan." : "");
            result.Text = released != null && reference != null ? RemoteText.Result(released, reference.ScannedAt) : "Noch kein freigegebenes Ergebnis.";
            feedbackPanel.Visibility = Visible(released != null); feedback.IsEnabled = !working;
            status.Text = working ? "Verbindung zum Server …" : session.Status;
        }
        finally { rendering = false; }
    }

    static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    static string Local(string timestamp) => ContractFormat.ParseTimestamp(timestamp).ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    static TextBlock Note(string text = "") => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(190, 205, 225)), Margin = new Thickness(0, 0, 0, 10) };
    static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
    static TextBlock Heading(string text) => new() { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 10) };
    static TextBox Input(bool multiline = false, double height = double.NaN)
        => new() { AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, Height = height, Padding = new Thickness(8), FontSize = 13, Margin = new Thickness(0, 0, 0, 10),
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
    // Server texts land only in read-only plain text boxes: no markup, no links, nothing clickable.
    static TextBox ReadOnly(double height, bool monospace)
        => new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Height = height, Padding = new Thickness(8), FontSize = 12, Margin = new Thickness(0, 0, 0, 10),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily(monospace ? "Consolas" : "Segoe UI") };
    static ComboBox Choice(IEnumerable<string> labels, int selected = 0)
        => new() { ItemsSource = labels.ToList(), SelectedIndex = selected, FontSize = 13, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 0, 10), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, MinWidth = 320 };

    static bool? SaveFile(string text)
    {
        var dialog = new SaveFileDialog { Title = "Vorschau des Diagnoseberichts lokal speichern", FileName = "DriverPilot-Diagnosebericht.json", DefaultExt = ".json", Filter = "JSON-Datei (*.json)|*.json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog() != true) return null;
        File.WriteAllText(dialog.FileName, text, new UTF8Encoding(false));
        return true;
    }
}
