using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
using CheckBox=System.Windows.Controls.CheckBox;
using ListBox=System.Windows.Controls.ListBox;
namespace DriverPilot;

public partial class MainWindow
{
    readonly List<FrameworkElement> extraPages=[];
    readonly StackPanel observationRows=new(),recoveryRows=new(),actionRows=new(),blockRows=new();
    readonly List<CheckBox> dayBoxes=[];
    TextBox startHour=null!,endHour=null!,waitDays=null!,games=null!,network=null!;
    TextBlock controlStatus=null!;
    string transcript="";
    static TextBlock Text(string text,double size=14)=>new(){Text=text,FontSize=size,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
    static Button Action(string label,RoutedEventHandler handler){var b=new Button{Content=label,Margin=new Thickness(0,0,10,10),HorizontalAlignment=System.Windows.HorizontalAlignment.Left};b.Click+=handler;return b;}
    Border Card(StackPanel content)=>new(){Style=(Style)FindResource("Card"),Child=content,Margin=new Thickness(0,0,0,16)};
    static void Open(string path)=>Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
    void InitializeFeatures()
    {
        snapshot=Store.Read<Snapshot>("snapshot.json");
        transcript=Store.Read<string>("chat.json")??"";if(transcript.Length>0)ChatOutput.Text=transcript;
        string[] names=["Nach Update beobachten","Wiederherstellung","Wartung & Sperren","KI-Aktionsprotokoll"];
        StackPanel[] contents=[observationRows,recoveryRows,new(),actionRows];
        for(var i=0;i<names.Length;i++){
            var page=new ScrollViewer{Content=contents[i],VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Visibility=Visibility.Collapsed};extraPages.Add(page);PageHost.Children.Add(page);
            var button=Action(names[i],Navigate);button.Tag=(8+i).ToString();button.FontSize=12;button.Padding=new Thickness(10,8,10,8);button.HorizontalAlignment=System.Windows.HorizontalAlignment.Stretch;Navigation.Children.Add(button);
        }
        var panel=contents[2];var c=MaintenanceControls.Load();
        panel.Children.Add(Text("Installation nur im gewählten Fenster, mindestens 10 Minuten Leerlauf und ohne erkannten Spielbetrieb. Änderungen gelten für den nächsten sicheren Prüfschritt.",14));
        var form=new StackPanel();form.Children.Add(Text("Wartungsfenster",19));var days=new WrapPanel();
        string[] weekdays=["So","Mo","Di","Mi","Do","Fr","Sa"];
        for(var i=0;i<7;i++){var box=new CheckBox{Content=weekdays[i],Tag=i,IsChecked=c.Days.Contains(i),Margin=new Thickness(0,8,18,12)};dayBoxes.Add(box);days.Children.Add(box);}form.Children.Add(days);
        TextBox Field(string label,string value){form.Children.Add(Text(label));var field=new TextBox{Text=value,Margin=new Thickness(0,0,0,16)};form.Children.Add(field);return field;}
        startHour=Field("Beginn (Stunde 0–23)",c.StartHour.ToString());endHour=Field("Ende (Stunde 1–24, gleicher Tag)",c.EndHour.ToString());
        waitDays=Field("Beobachtungszeit für Pakete (14–90 Tage)",c.ObservationDays.ToString());
        games=Field("Zusätzliche Spiele/Streams: Prozessnamen ohne .exe, jeweils eine Zeile",string.Join("\n",c.BusyProcesses));games.AcceptsReturn=true;games.MinHeight=85;
        network=Field("Wartung bei Netzwerklast ab MB/s verschieben (1–100)",c.NetworkLimitMB.ToString());
        form.Children.Add(Text("OBS, Streamlabs und bekannte Spiele werden zusätzlich berücksichtigt. Netzwerkverkehr ist eine Momentaufnahme; kurze oder sehr langsame Downloads können unerkannt bleiben."));
        var actions=new WrapPanel();actions.Children.Add(Action("Regeln speichern",SaveControls));actions.Children.Add(Action("Bis morgen pausieren",(_,_)=>{var rules=MaintenanceControls.Load();rules.PauseUntil=DateTime.Today.AddDays(1).ToString("o");rules.Save();RefreshControls();}));actions.Children.Add(Action("Pause aufheben",(_,_)=>{var rules=MaintenanceControls.Load();rules.PauseUntil="";rules.Save();RefreshControls();}));form.Children.Add(actions);
        controlStatus=Text("");form.Children.Add(controlStatus);panel.Children.Add(Card(form));
        panel.Children.Add(Text("Gesperrte Geräte und Pakete",19));panel.Children.Add(Text("Geräte unter Hardware auswählen und festhalten. Einzelne angebotene Pakete unter Treiberupdates sperren. Die Sperren betreffen den DriverPilot-Autopiloten; Windows und Herstellerprogramme haben eigene Regeln."));panel.Children.Add(blockRows);
        var settings=(StackPanel)SettingsPage.Content;
        var app=new StackPanel();app.Children.Add(Text("DriverPilot "+typeof(App).Assembly.GetName().Version?.ToString(3),19));app.Children.Add(Text("Private Releases auf GitHub. Ein heruntergeladenes und entpacktes DriverPilot-Paket lässt sich hier prüfen und installieren. Der Chat bleibt lokal erhalten."));
        app.Children.Add(Action("GitHub-Releases öffnen ↗",(_,_)=>Open("https://github.com/Madchristian/DriverPilot/releases")));
        app.Children.Add(Action("Updatepaket auswählen …",InstallAppUpdate));app.Children.Add(Action("App neu starten",RestartApp));app.Children.Add(Action("DriverPilot beenden",(_,_)=>{exit=true;Close();}));settings.Children.Add(Card(app));
        RefreshControls();
    }
    void SaveControls(object sender,RoutedEventArgs e)
    {
        try{
            var c=MaintenanceControls.Load();var days=dayBoxes.Where(b=>b.IsChecked==true).Select(b=>(int)b.Tag).ToArray();
            if(days.Length==0||!int.TryParse(startHour.Text,out var start)||!int.TryParse(endHour.Text,out var end)||start<0||start>23||end<=start||end>24||!int.TryParse(waitDays.Text,out var wait)||wait<14||wait>90||!int.TryParse(network.Text,out var net)||net<1||net>100)throw new Exception("Bitte gültige Tage, Stunden und Zahlen in den angegebenen Bereichen wählen.");
            var processes=games.Text.Split(['\r','\n',',',';'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(p=>p.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?p[..^4]:p).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if(processes.Count>100||processes.Any(p=>!System.Text.RegularExpressions.Regex.IsMatch(p,@"^[\p{L}\p{N}_ .-]{1,100}$")))throw new Exception("Prozessnamen ohne Pfade oder Platzhalter eingeben.");
            c.Days=days;c.StartHour=start;c.EndHour=end;c.ObservationDays=wait;c.NetworkLimitMB=net;c.BusyProcesses=processes;c.Save();Store.Log("Wartungsregeln","Zeitfenster, Beobachtungszeit und Aktivitätsschutz gespeichert.");RefreshControls();
        }catch(Exception ex){controlStatus.Text=ex.Message;}
    }
    void RefreshControls()
    {
        var c=MaintenanceControls.Load();controlStatus.Text=DateTime.TryParse(c.PauseUntil,out var until)&&until>DateTime.Now?$"Pausiert bis {until:dd.MM.yyyy HH:mm}.":"Keine Pause aktiv. Gespeicherte Regeln gelten für den Autopiloten.";
        blockRows.Children.Clear();foreach(var id in c.BlockedDevices.ToArray())AddBlock(id,true);foreach(var id in c.BlockedUpdates.ToArray())AddBlock(id,false);
        if(blockRows.Children.Count==0)blockRows.Children.Add(Text("Keine Sperren gesetzt."));
        void AddBlock(string id,bool device){var row=new StackPanel();var label=device?SnapshotData.DeviceName(snapshot,id):null;
            row.Children.Add(Text((device?"Gerät: ":"Paket: ")+(label??id)));row.Children.Add(Action("Sperre aufheben",(_,_)=>{var current=MaintenanceControls.Load();(device?current.BlockedDevices:current.BlockedUpdates).Remove(id);current.Save();Store.Log("Sperre aufgehoben",label??id);RefreshControls();}));blockRows.Children.Add(Card(row));}
    }
    void PinDevice(object sender,RoutedEventArgs e){if(DeviceGrid.SelectedItem is not Device d){StatusLine.Text="Zuerst ein Gerät auswählen.";return;}var c=MaintenanceControls.Load();if(!c.BlockedDevices.Contains(d.Id))c.BlockedDevices.Add(d.Id);c.Save();StatusLine.Text=d.Name+" ist für automatische Treiberwechsel gesperrt.";Store.Log("Gerät festgehalten",d.Name+" · "+d.Version);RefreshControls();}
    void BlockUpdate(object sender,RoutedEventArgs e){if(UpdateGrid.SelectedItem is not UpdateRow u){StatusLine.Text="Zuerst ein angebotenes Paket auswählen.";return;}var c=MaintenanceControls.Load();if(!c.BlockedUpdates.Contains(u.Id))c.BlockedUpdates.Add(u.Id);c.Save();StatusLine.Text="Paket für DriverPilot gesperrt: "+u.Title;Store.Log("Paket gesperrt",u.Title);RefreshControls();}
    void ExplainFinding(object sender,RoutedEventArgs e)
    {
        if(FindingsList.SelectedItem is not Finding f){StatusLine.Text="Zuerst einen Befund auswählen.";return;}
        // No raw event message or device identifier is copied into the prompt.
        ChatInput.Text="Erkläre diesen ausgewählten Befund mit Schwerpunkt Stabilität und schlage prüfbare nächste Schritte vor. Lokale Fakten: "+JsonSerializer.Serialize(new{f.Title,f.Severity,f.Time,f.Count,f.FirstSeen,f.LastSeen});
        Navigate(new Button{Tag="4"},new());StatusLine.Text="Befund datensparsam vorbereitet. Mit Senden an Codex übermitteln.";
    }
    void ClearChat(object sender,RoutedEventArgs e){if(!SendButton.IsEnabled)return;transcript="";chatPrefix="";ChatOutput.Text="";Store.Save("chat.json","");codex.Dispose();Store.Log("Chat","Lokalen Chat gelöscht und KI-Kontext geschlossen.");}
    void RefreshExtra(int index)
    {
        if(index==10){RefreshControls();return;}
        if(index==8){observationRows.Children.Clear();observationRows.Children.Add(Text("Nach Treiberänderungen beobachtet DriverPilot 24 Stunden lang zusätzliche Geräte- und Ereignismeldungen. Fehlende Meldungen belegen keine vollständige Stabilität.",15));var rows=Store.Read<List<Observation>>("observations.json")??[];if(rows.Count==0)observationRows.Children.Add(Text("Noch keine Treiberänderung während der Überwachung erkannt."));foreach(var o in rows){var p=new StackPanel();p.Children.Add(Text(o.Device,19));p.Children.Add(Text(o.Status));p.Children.Add(Text(o.Detail));observationRows.Children.Add(Card(p));}return;}
        if(index==11){actionRows.Children.Clear();actionRows.Children.Add(Text("Lokales Protokoll der KI-Werkzeuge. „Angestoßen“ bestätigt keine Installation. Ergebnisse werden auf 20.000 Zeichen begrenzt.",15));var rows=Store.Read<List<AiAction>>("ai-actions.json")??[];if(rows.Count==0)actionRows.Children.Add(Text("Noch keine KI-Werkzeugaktionen protokolliert."));foreach(var a in rows){var p=new StackPanel();p.Children.Add(Text($"{a.Time:dd.MM.yyyy HH:mm:ss} · {a.Tool}",17));p.Children.Add(Text(a.Status));p.Children.Add(new TextBox{Text=a.Result,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});actionRows.Children.Add(Card(p));}return;}
        if(index==9){
            recoveryRows.Children.Clear();var intro=new StackPanel();intro.Children.Add(Text("Gezielt zu einem vorherigen Treiber zurückkehren",19));intro.Children.Add(Text("1. Betroffenes Gerät und Zeitpunkt im Verlauf vergleichen.\n2. Im Geräte-Manager: Gerät → Eigenschaften → Treiber → Vorheriger Treiber, sofern verfügbar.\n3. Falls der Rückweg fehlt: passende Sicherung über „Treiber aktualisieren → Auf meinem Computer suchen“ prüfen. Windows kann ältere Pakete ablehnen; es gibt keinen garantierten Rückweg. Keine pauschale Wiederherstellung aller Pakete.\n4. Danach erneut prüfen. Einen erforderlichen Neustart führst du selbst aus."));intro.Children.Add(Action("Geräte-Manager öffnen",(_,_)=>Open(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"devmgmt.msc"))));recoveryRows.Children.Add(Card(intro));
            try{var rows=BackupRow.Load();if(rows.Count==0)recoveryRows.Children.Add(Text("Noch keine Treibersicherung vorhanden. Der Autopilot exportiert Fremdherstellertreiber vor seiner ersten Installation pro Lauf."));foreach(var b in rows){var p=new StackPanel();p.Children.Add(Text(b.Detail,17));p.Children.Add(Action("Sicherungsordner öffnen",(_,_)=>Open(b.Path)));recoveryRows.Children.Add(Card(p));}var audit=Path.Combine(Store.Shared,"Audit");if(Directory.Exists(audit)){recoveryRows.Children.Add(Text("Vorher-/Nachher-Protokolle",19));foreach(var file in Directory.EnumerateFiles(audit,"*.json").OrderDescending().Take(50))recoveryRows.Children.Add(Action(Path.GetFileName(file),(_,_)=>Open(file)));}}catch(Exception ex){recoveryRows.Children.Add(Text("Sicherungen nicht vollständig lesbar: "+ex.Message));}
        }
    }
    void FirmwareGuide(object sender,RoutedEventArgs e)
    {
        if((sender as Button)?.DataContext is not ManufacturerFinding f)return;
        var panel=new StackPanel{Margin=new Thickness(24)};var win=new Window{Owner=this,Title="Herstellerprüfung · "+f.Device,Width=700,Height=670,Background=Background,Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},WindowStartupLocation=WindowStartupLocation.CenterOwner};
        panel.Children.Add(Text(f.Device,22));panel.Children.Add(Text("Lokal installiert: "+f.Installed));panel.Children.Add(Text("Herstellerstand: "+f.Available+" · "+f.Status));panel.Children.Add(Text(f.Details));
        if(ManufacturerScanner.OfficialUrl(f.SourceUrl))panel.Children.Add(Action("Offizielle Produktquelle öffnen ↗",(_,_)=>{if(ManufacturerScanner.OfficialUrl(f.SourceUrl))Open(f.SourceUrl);}));
        else panel.Children.Add(Text("Keine verifizierte Herstellerquelle zugeordnet. Exaktes Modell und Hersteller zuerst manuell klären."));
        var guide=ManufacturerScanner.BiosGuideUrl(f);
        if(guide.Length>0)panel.Children.Add(Action("BIOS-Anleitung beim zuständigen Hersteller prüfen ↗",(_,_)=>{if(ManufacturerScanner.OfficialUrl(guide))Open(guide);}));
        panel.Children.Add(Text("Vor einer manuellen Aktualisierung",18));
        foreach(var label in new[]{"Exaktes Modell und gegebenenfalls Hardware-Revision abgeglichen","Herstellerhinweise, bekannte Einschränkungen und Rückweg gelesen","Wichtige Daten gesichert; bei Verschlüsselung Wiederherstellungsschlüssel verfügbar","Stabile Stromversorgung und Zeit für den manuellen Neustart vorhanden"})panel.Children.Add(new CheckBox{Content=new TextBlock{Text=label,TextWrapping=TextWrapping.Wrap}});
        panel.Children.Add(Text("Manuell geprüfte Version / Datum / Ergebnis (deine Notiz, kein automatischer Nachweis):"));
        var note=new TextBox{AcceptsReturn=true,MinHeight=70,TextWrapping=TextWrapping.Wrap};panel.Children.Add(note);
        panel.Children.Add(Action("Prüfnotiz speichern",(_,_)=>{if(string.IsNullOrWhiteSpace(note.Text))return;Store.Log("Manuelle Herstellerprüfung",f.Device+" · installiert "+f.Installed+" · Benutzerangabe: "+note.Text+" · "+f.SourceUrl);win.Close();}));
        panel.Children.Add(Text("DriverPilot führt hier keinen Installer und keinen Flash-Vorgang aus. Versionsbezeichnungen unterschiedlicher Firmwarezweige lassen sich nicht zuverlässig numerisch vergleichen."));win.ShowDialog();
    }
    async void RestartApp(object sender,RoutedEventArgs e)
    {
        if(!SendButton.IsEnabled){StatusLine.Text="Bitte die laufende KI-Antwort abwarten.";return;}
        Store.Save("chat.json",transcript);var psi=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};psi.ArgumentList.Add("--wait-for");psi.ArgumentList.Add(Environment.ProcessId.ToString());Process.Start(psi);await Task.Yield();exit=true;Close();
    }
    async void InstallAppUpdate(object sender,RoutedEventArgs e)
    {
        if(!SendButton.IsEnabled){StatusLine.Text="Bitte die laufende KI-Antwort abwarten.";return;}
        var dialog=new Microsoft.Win32.OpenFolderDialog{Title="Entpacktes DriverPilot-Release auswählen"};if(dialog.ShowDialog(this)!=true)return;
        try{
            Security.PackageTrust.AssertInstalledUpdater(AppContext.BaseDirectory);
            var manifest=PackageVerifier.Verify(dialog.FolderName);
            if(System.Windows.MessageBox.Show(this,$"DriverPilot {manifest.Version} installieren?\n\nQuelle: {dialog.FolderName}\nDas gesamte Paket wurde gegen das signierte Manifest und den fest hinterlegten Herausgeberschlüssel geprüft. Die erhöhte Einrichtung prüft exakt dieses genehmigte Paket erneut.\n\nDriverPilot wird beendet und nach erfolgreichem Update neu geöffnet. Windows bleibt eingeschaltet.","App aktualisieren",MessageBoxButton.OKCancel)!=MessageBoxResult.OK)return;
            // Never let either long-lived update child pin the installed shortcut's working directory.
            var script=Path.Combine(AppContext.BaseDirectory,"Setup.ps1");var psi=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Environment.GetFolderPath(Environment.SpecialFolder.System)};
            foreach(var arg in new[]{"-NoProfile","-ExecutionPolicy","Bypass","-File",script,"-PackagePath",dialog.FolderName,"-ApprovedManifestHash",manifest.ManifestHash,"-WaitForPid",Environment.ProcessId.ToString()})psi.ArgumentList.Add(arg);
            using var process=Process.Start(psi);if(process==null)throw new Exception("Update konnte nicht gestartet werden.");Store.Save("chat.json",transcript);
            var installed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"DriverPilot","DriverPilot.exe");
            var launchScript="$ErrorActionPreference='Stop'; try { Wait-Process -Id "+process.Id+" -Timeout 180 -ErrorAction SilentlyContinue; if(Get-Process -Id "+process.Id+" -ErrorAction SilentlyContinue){throw 'Update noch nicht abgeschlossen.'}; $actual=[Diagnostics.FileVersionInfo]::GetVersionInfo('"+Path.ChangeExtension(installed,"dll").Replace("'","''")+"').FileVersion; if([version]$actual -ne [version]'"+manifest.Version+".0'){throw 'Das Update wurde nicht abgeschlossen. Die bisherige App kann erneut geöffnet werden.'}; Start-Process -FilePath '"+installed.Replace("'","''")+"' } catch { Add-Type -AssemblyName System.Windows.Forms; [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message,'DriverPilot-Update') }";
            var helper=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Environment.GetFolderPath(Environment.SpecialFolder.System)};
            helper.ArgumentList.Add("-NoProfile");helper.ArgumentList.Add("-EncodedCommand");helper.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(launchScript)));Process.Start(helper);
            await Task.Yield();exit=true;Close();
        }catch(Exception ex){StatusLine.Text="App-Update: "+ex.Message;}
    }
}


