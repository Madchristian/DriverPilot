using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using FontFamily = System.Windows.Media.FontFamily;
using Color = System.Windows.Media.Color;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
namespace DriverPilot;

/// <summary>Standalone dialog; construction never reads Store or starts background work.</summary>
public sealed class SystemReportWindow : Window
{
    readonly SystemReportSession session;
    readonly TextBox preview = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 12, FontFamily = new FontFamily("Consolas") };
    readonly TextBox[] notes = new TextBox[4];
    readonly CheckBox consent = new();
    readonly Button browser = new() { Content = "GitHub-Entwurf öffnen ↗", IsEnabled = false };
    readonly Button export = new() { Content = "Lokal speichern …" };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(131, 188, 255)), Margin = new Thickness(0, 8, 0, 0) };

    public SystemReportWindow(Snapshot snapshot, DateTimeOffset scannedAt, ManufacturerSummary? manufacturers = null)
        : this(new SystemReportSession(snapshot, scannedAt, () => DateTimeOffset.UtcNow, OpenBrowser, SaveFile, manufacturers)) { }

    public SystemReportWindow(SystemReportSession session)
    {
        this.session = session;
        Title = "DriverPilot · Systembericht prüfen"; Width = 980; Height = 820; MinWidth = 720; MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(11, 20, 35));
        NameScope.SetNameScope(this, new NameScope());
        var grid = new Grid { Margin = new Thickness(20, 16, 20, 16) }; Content = grid;
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(146), GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto }) grid.RowDefinitions.Add(new RowDefinition { Height = height });
        void Add(UIElement element, int row) { Grid.SetRow(element, row); grid.Children.Add(element); }
        Add(new TextBlock { Text = "Systembericht · erst prüfen, dann teilen", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) }, 0);
        Add(new TextBlock
        {
            Text = "Nur ausgewählte Hardware-/Treiberfelder, keine Ereignisse oder Gerätenamen. Automatische Filter erkennen nicht alle persönlichen Daten oder Geheimnisse: Vorschau selbst prüfen.\nZiel: github.com/Madchristian/DriverPilot · Repository ist derzeit privat. GitHub-Konto und Zugriffsrechte sind erforderlich. Bereits die Entwurfs-URL übermittelt den Bericht an Browser/GitHub und kann im Browserverlauf bleiben. Kein automatisches Issue. Offline oder ohne Zugriff: lokal speichern.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(190, 205, 225)), Margin = new Thickness(0, 0, 0, 10)
        }, 1);
        var fields = new Grid(); fields.ColumnDefinitions.Add(new()); fields.ColumnDefinitions.Add(new()); fields.RowDefinitions.Add(new()); fields.RowDefinitions.Add(new()); Add(fields, 2);
        string[] labels = ["Problem (optional)", "Schritte zur Reproduktion (optional)", "Erwartet (optional)", "Tatsächlich (optional)"];
        string[] names = ["ProblemNote", "ReproductionNote", "ExpectedNote", "ActualNote"];
        for (int i = 0; i < 4; i++)
        {
            var dock = new DockPanel { Margin = new Thickness(i % 2 == 0 ? 0 : 6, 0, i % 2 == 0 ? 6 : 0, 8) };
            Grid.SetColumn(dock, i % 2); Grid.SetRow(dock, i / 2); fields.Children.Add(dock);
            var label = new TextBlock { Text = labels[i] + " · max. 1000 Zeichen", FontSize = 11, Margin = new Thickness(0, 0, 0, 3) }; DockPanel.SetDock(label, Dock.Top); dock.Children.Add(label);
            // Backend rejects oversized text instead of silently truncating a paste.
            var note = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(7), FontSize = 12 };
            notes[i] = note; RegisterName(names[i], note); dock.Children.Add(note);
            System.Windows.Automation.AutomationProperties.SetName(note, labels[i]);
            note.TextChanged += (_, _) => NotesChanged();
        }
        Add(new TextBlock { Text = "VORSCHAU · exakt derselbe Text für Datei und Issue-Entwurf", FontSize = 11, Margin = new Thickness(0, 0, 0, 7) }, 3);
        RegisterName("ReportPreview", preview); Add(preview, 4);
        var bottom = new StackPanel(); Add(bottom, 5);
        consent.Content = new TextBlock { Text = "Ich habe die gesamte Vorschau geprüft und stimme der Übergabe dieses Textes an Browser/GitHub zu.", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        consent.Margin = new Thickness(0, 10, 0, 10); RegisterName("ShareConsent", consent); bottom.Children.Add(consent);
        consent.Checked += (_, _) => { session.Consent = true; RefreshActions(); };
        consent.Unchecked += (_, _) => { session.Consent = false; RefreshActions(); };
        var actions = new WrapPanel(); bottom.Children.Add(actions);
        var close = new Button { Content = "Schließen", IsCancel = true };
        RegisterName("SaveReportButton", export); RegisterName("OpenDraftButton", browser); RegisterName("CloseReportButton", close); RegisterName("ReportStatus", status);
        foreach (var button in new[] { export, browser, close }) { button.Padding = new Thickness(12, 9, 12, 9); actions.Children.Add(button); }
        export.Click += (_, _) => { session.Export(); Refresh(); };
        browser.Click += (_, _) => { session.OpenDraft(); Refresh(); };
        close.Click += (_, _) => Close(); bottom.Children.Add(status);
        Refresh();
    }

    void NotesChanged()
    {
        if (notes.Any(n => n == null)) return;
        session.UpdateNotes(notes[0].Text, notes[1].Text, notes[2].Text, notes[3].Text); Refresh();
    }
    void Refresh()
    {
        preview.Text = session.Report?.Text ?? "Keine gültige Vorschau. Bitte Eingaben kürzen.";
        consent.IsChecked = session.Consent; status.Text = session.Status; RefreshActions();
    }
    void RefreshActions() { browser.IsEnabled = session.Consent && session.Report != null; export.IsEnabled = session.Report != null; }
    static bool OpenBrowser(Uri uri)
    {
        // Only the session constructs the fixed endpoint and escaped body.
        using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        return process != null;
    }
    static bool? SaveFile(string text)
    {
        var dialog = new SaveFileDialog { Title = "Geprüften Systembericht lokal speichern", FileName = "DriverPilot-Systembericht.txt", DefaultExt = ".txt", Filter = "Textdatei (*.txt)|*.txt", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog() != true) return null;
        File.WriteAllText(dialog.FileName, text, new UTF8Encoding(false));
        return true;
    }
}