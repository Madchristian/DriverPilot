using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;
namespace DriverPilot;
public partial class MainWindow : Window
{
    readonly Preferences preferences=Store.Read<Preferences>("preferences.json")??new();
    readonly CodexClient codex=new();
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMinutes(2)};
    readonly Forms.NotifyIcon tray=new();
    Snapshot? snapshot;
    bool ready,busy,updateBusy,manufacturerBusy,exit;
    string chatPrefix="";
    DateTime lastUpdates=DateTime.MinValue;
    DateTime lastManufacturers=DateTime.MinValue;
    public MainWindow()
    {
        InitializeComponent();
        MonitorBox.IsChecked=preferences.Monitor;NotifyBox.IsChecked=preferences.Notify;
        using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");StartupBox.IsChecked=key?.GetValue("DriverPilot")!=null;
        tray.Icon=System.Drawing.SystemIcons.Shield;tray.Text="DriverPilot · Stabilität zuerst";tray.Visible=true;
        var menu=new Forms.ContextMenuStrip();menu.Items.Add("DriverPilot öffnen",null,(_,_)=>Dispatcher.Invoke(ShowApp));menu.Items.Add("Jetzt prüfen",null,(_,_)=>Dispatcher.Invoke(async()=>await Scan()));menu.Items.Add("Beenden",null,(_,_)=>Dispatcher.Invoke(()=>{exit=true;Close();}));tray.ContextMenuStrip=menu;
        tray.DoubleClick+=(_,_)=>Dispatcher.Invoke(ShowApp);
        Closing+=OnClosing;
        timer.Tick+=async(_,_)=>{if(preferences.Monitor){await Scan(false);if(DateTime.Now-lastUpdates>TimeSpan.FromDays(1))await SearchUpdates();if(DateTime.Now-lastManufacturers>TimeSpan.FromDays(1))await SearchManufacturers();}RefreshWorker();};timer.Start();
        codex.TextUpdated+=text=>Dispatcher.Invoke(()=>{ChatOutput.Text=chatPrefix+text;ChatOutput.ScrollToEnd();});
        codex.AccountChanged+=text=>Dispatcher.Invoke(()=>AccountStatus.Text=text);
        InitializeFeatures();
        ready=true;
    }
    public async Task InitializeAsync(){await Scan();RefreshWorker();RenderManufacturers(Store.Read<ManufacturerReport>("manufacturers.json"));}
    void ShowApp(){Show();WindowState=WindowState.Normal;Activate();}
    void OnClosing(object? sender,CancelEventArgs e){if(!exit){e.Cancel=true;Hide();return;}timer.Stop();tray.Dispose();codex.Dispose();System.Windows.Application.Current.Shutdown();}
    void Navigate(object sender,RoutedEventArgs e)
    {
        var index=int.Parse((string)((System.Windows.Controls.Button)sender).Tag);
        FrameworkElement[] pages=[Overview,HardwarePage,IssuesPage,UpdatesPage,AiPage,HistoryPage,SettingsPage,ManufacturersPage,..extraPages];
        for(var i=0;i<pages.Length;i++)pages[i].Visibility=i==index?Visibility.Visible:Visibility.Collapsed;
        string[] titles=["Dein System im Blick","Hardware & Treiber","Probleme & Hinweise","Treiberupdates","Dein Codex-Assistent","Was sich geändert hat","Deine Einstellungen","Hersteller & Firmware","Nach Update beobachten","Wiederherstellung","Wartung & Sperren","KI-Aktionsprotokoll"];
        string[] subtitles=["Hardware, Treiber und Windows-Meldungen an einem Ort.","Aktuell erkannte Geräte und installierte Treiberversionen.","Windows-Befunde verstehen und gezielt verfolgen.","Passende Pakete suchen. Stabilität vor Versionsnummern.","Befunde erklären lassen und nächste Schritte abwägen.","Lokale Prüfungen und Wartung nachvollziehen.","Deine Regeln gelten auch im Hintergrund.","Offizielle Quellen für Treiber, BIOS und SSD-Firmware.","Zusätzliche Befunde nach einem Versionswechsel.","Sicherungen und unterstützte manuelle Rückwege.","Zeitfenster, Aktivitätsschutz und Ausnahmen.","Werkzeugaufrufe und ihre tatsächlichen Ergebnisse."];
        if(index>=8)RefreshExtra(index);
        PageTitle.Text=titles[index];PageSubtitle.Text=subtitles[index];
        if(index==5)RefreshHistory();if(index==3)RefreshWorker();if(index==7)RenderManufacturers(Store.Read<ManufacturerReport>("manufacturers.json"));
    }
    async void ScanClick(object sender,RoutedEventArgs e)=>await Scan();
    async Task Scan(bool manual=true)
    {
        if(busy)return;busy=true;ScanButton.IsEnabled=false;StatusLine.Text="Geräte und Windows-Ereignisse werden geprüft …";
        try
        {
            var data=await PowerShell.Run("Inventory");var next=JsonSerializer.Deserialize<Snapshot>(data,Store.Json)??throw new Exception("Windows hat keine Gerätedaten geliefert.");
            if(snapshot!=null)
            {
                var previous=snapshot.Devices.ToDictionary(d=>d.Id,d=>d.Version);
                foreach(var d in next.Devices)if(previous.TryGetValue(d.Id,out var version)&&version!=d.Version)Store.Log("Treiber geändert",$"{d.Name}: {version} → {d.Version}");
            }
            var existing=Store.Read<List<Observation>>("observations.json")??[];
            var evidenceCounts=existing.ToDictionary(o=>(o.Id,o.DetectedAt),o=>o.Evidence.Count);
            var observations=Stability.Compare(snapshot,next,existing,DateTime.Now);Store.Save("observations.json",observations);
            foreach(var observation in observations.Where(o=>o.Evidence.Count>evidenceCounts.GetValueOrDefault((o.Id,o.DetectedAt))))
            {Store.Log("Nach Treiberwechsel",observation.Device+": zusätzliche Befunde während der Beobachtung. Kein Ursachenbeweis.");if(preferences.Notify)tray.ShowBalloonTip(8000,"DriverPilot · Nachbeobachtung",observation.Device+": Zusätzliche Befunde nach dem Versionswechsel. Details unter Nach Update beobachten; Zusammenhang noch ungeklärt.",Forms.ToolTipIcon.Warning);}
            if(extraPages[0].Visibility==Visibility.Visible)RefreshExtra(8);
            snapshot=next;Store.Save("snapshot.json",snapshot);
            foreach(var finding in next.Findings)
            {
                if(preferences.SeenEvents.Add(finding.Key))Store.Log("Befund",finding.Title+": "+finding.Detail);
                if(preferences.Notify&&Rules.ShouldNotify(finding,preferences)){tray.ShowBalloonTip(8000,"DriverPilot · "+finding.Title,finding.Detail.Length>240?finding.Detail[..240]:finding.Detail,Forms.ToolTipIcon.Warning);preferences.Notified.Add(finding.Key);}
            }
            var current=next.Findings.Select(f=>f.Key).ToHashSet();preferences.Notified.RemoveWhere(k=>!k.StartsWith("maintenance:")&&!current.Contains(k));preferences.SeenEvents.IntersectWith(current);
            Store.Save("preferences.json",preferences);RenderSnapshot();
            if(manual)Store.Log("Systemprüfung",$"{next.Devices.Count} Geräte, {next.Findings.Count} Hinweise.");
            StatusLine.Text="Letzte Prüfung: "+DateTime.Now.ToString("HH:mm")+" · "+(next.Warnings.Count==0?"Gerätestatus und Ereignisse gelesen.":string.Join(" · ",next.Warnings));
        }
        catch(Exception ex){StatusLine.Text="Prüfung fehlgeschlagen: "+ex.Message;Store.Log("Prüffehler",ex.Message);}
        finally{busy=false;ScanButton.IsEnabled=true;}
    }
    void RenderSnapshot()
    {
        if(snapshot==null)return;
        DeviceCount.Text=snapshot.Devices.Count.ToString();
        var findings=snapshot.Findings.Where(f=>!preferences.Ignored.Contains(f.Key)).ToList();var warnings=findings.Count(f=>f.Severity!="Info");
        IssueCount.Text=warnings.ToString();HealthTitle.Text=warnings==0?"Keine akuten Gerätewarnungen erkannt":$"{warnings} Befunde verdienen einen Blick";
        if(snapshot.Warnings.Count>0)HealthTitle.Text="Prüfung teilweise eingeschränkt";
        HealthDetail.Text=$"{findings.Count(f=>f.Severity=="Info")} zusätzliche Hinweise · "+(snapshot.Warnings.Count>0?"Prüfung teilweise eingeschränkt. Details unten.":"Windows-Gerätestatus und ausgewählte Ereignisse geprüft. Keine Aussage über vollständige Systemstabilität.");
        HardwareSummary.Text=snapshot.Cpu+"\n"+string.Join(" · ",snapshot.Devices.Where(d=>d.Class.Equals("Display",StringComparison.OrdinalIgnoreCase)).Select(d=>d.Name))+"\n"+snapshot.Board+" · "+snapshot.RamGB+" GB RAM\n"+snapshot.Windows;
        FilterDevices();FindingsList.ItemsSource=findings;
    }
    void FilterChanged(object sender,TextChangedEventArgs e)=>FilterDevices();
    void FilterDevices(){if(snapshot==null||DeviceGrid==null)return;var term=DeviceFilter.Text.Trim();DeviceGrid.ItemsSource=snapshot.Devices.Where(d=>term.Length==0||$"{d.Name} {d.Provider} {d.Version} {d.Class}".Contains(term,StringComparison.OrdinalIgnoreCase)).ToList();}
    void IgnoreClick(object sender,RoutedEventArgs e){if(FindingsList.SelectedItem is Finding f){preferences.Ignored.Add(f.Key);Store.Save("preferences.json",preferences);Store.Log("Als bekannt markiert",f.Title);RenderSnapshot();}}
    void ResetIgnoredClick(object sender,RoutedEventArgs e){preferences.Ignored.Clear();Store.Save("preferences.json",preferences);RenderSnapshot();}
    async void UpdatesClick(object sender,RoutedEventArgs e)=>await SearchUpdates();
    async Task SearchUpdates()
    {
        if(updateBusy)return;updateBusy=true;UpdateButton.IsEnabled=false;UpdateStatus.Text="Windows Update sucht passende Treiberpakete …";
        try{var json=await PowerShell.Run("Updates",600);var rows=JsonSerializer.Deserialize<List<UpdateRow>>(json,Store.Json)??[];UpdateGrid.ItemsSource=rows;lastUpdates=DateTime.Now;UpdateStatus.Text=$"{rows.Count} Pakete angeboten · geprüft um {lastUpdates:HH:mm}. Noch keine Installation.";Store.Save("updates.json",rows);Store.Log("Updatesuche",$"{rows.Count} Treiberpakete von Windows Update angeboten.");}
        catch(Exception ex){lastUpdates=DateTime.Now.AddHours(-23);UpdateStatus.Text="Suche fehlgeschlagen: "+ex.Message;Store.Log("Updatesuche fehlgeschlagen",ex.Message);}
        finally{updateBusy=false;UpdateButton.IsEnabled=true;}
    }
    void PreferencesChanged(object sender,RoutedEventArgs e){if(!ready)return;preferences.Monitor=MonitorBox.IsChecked==true;preferences.Notify=NotifyBox.IsChecked==true;Store.Save("preferences.json",preferences);}
    async void ManufacturersClick(object sender,RoutedEventArgs e)=>await SearchManufacturers();
    async Task SearchManufacturers()
    {
        if(manufacturerBusy||snapshot==null)return;manufacturerBusy=true;ManufacturerButton.IsEnabled=false;ManufacturerStatus.Text="Installierte Firmware erfassen und offizielle Herstellerquellen prüfen …";
        try
        {
            var report=await ManufacturerScanner.Scan(snapshot);RenderManufacturers(report);
            var updates=report.Findings.Where(f=>f.Newer).ToList();
            var newUpdates=updates.Where(f=>preferences.ManufacturerNotified.Add(f.Device+":"+f.Available)).ToList();
            if(preferences.Notify&&newUpdates.Count>0)tray.ShowBalloonTip(8000,"DriverPilot · Herstellerupdates",$"{newUpdates.Count} neue Versionshinweise. Details unter Hersteller & Firmware. Es wurde nichts installiert.",Forms.ToolTipIcon.Info);
            Store.Save("preferences.json",preferences);Store.Log("Herstellersuche",$"{report.Findings.Count} Einträge, {updates.Count} neuere Versionen; Firmware-/BIOS-Prüfung siehe Einzelstatus.");
        }
        catch(Exception ex){lastManufacturers=DateTime.Now.AddHours(-23);ManufacturerStatus.Text="Herstellersuche fehlgeschlagen: "+ex.Message;}
        finally{manufacturerBusy=false;ManufacturerButton.IsEnabled=true;}
    }
    void RenderManufacturers(ManufacturerReport? report)
    {
        if(report==null)return;lastManufacturers=report.CheckedAt;ManufacturerList.ItemsSource=report.Findings.OrderBy(f=>f.Newer?0:f.Status=="Angebotene Version installiert"?1:f.Category=="BIOS / UEFI"?2:f.Category=="SSD-Firmware"?3:4).ToList();
        ManufacturerStatus.Text=$"Stand {report.CheckedAt:dd.MM.yyyy HH:mm} · {report.Findings.Count(f=>f.Newer)} neuere Version(en) · {report.Findings.Count(f=>f.Available=="Nicht ermittelt"||f.Status=="Versionsvergleich offen"||f.Status=="Im Browser prüfen")} offene Vergleiche";
    }
    void ManufacturerSourceClick(object sender,RoutedEventArgs e)
    {
        var url=(sender as System.Windows.Controls.Button)?.Tag as string;
        if(url==null||!ManufacturerScanner.OfficialUrl(url)){StatusLine.Text="Quellenlink ist nicht freigegeben.";return;}
        try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch(Exception ex){StatusLine.Text=ex.Message;}
    }
    void StartupChanged(object sender,RoutedEventArgs e)
    {
        if(!ready)return;try{using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");var installed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"DriverPilot","DriverPilot.exe");var exe=File.Exists(installed)?installed:Environment.ProcessPath;if(StartupBox.IsChecked==true)key.SetValue("DriverPilot",$"\"{exe}\" --tray");else key.DeleteValue("DriverPilot",false);}catch(Exception ex){StatusLine.Text=ex.Message;}
    }
    async void LoginClick(object sender,RoutedEventArgs e){try{AccountStatus.Text="Anmeldung im Browser abschließen …";await codex.Login();}catch(Exception ex){AccountStatus.Text=ex.Message;}}
    async void AccountClick(object sender,RoutedEventArgs e){try{AccountStatus.Text=await codex.Account();}catch(Exception ex){AccountStatus.Text=ex.Message;}}
    async void SendClick(object sender,RoutedEventArgs e)
    {
        if(snapshot==null||string.IsNullOrWhiteSpace(ChatInput.Text))return;SendButton.IsEnabled=false;var question=ChatInput.Text;chatPrefix=transcript+"Du: "+question+"\n\nCodex: ";ChatOutput.Text=chatPrefix;StatusLine.Text="Codex antwortet …";
        try{await codex.Ask(question,snapshot);StatusLine.Text="Codex-Antwort vollständig empfangen.";}catch(Exception ex){ChatOutput.AppendText("\n\n"+ex.Message);StatusLine.Text="Codex-Antwort nicht vollständig abgeschlossen.";}finally{transcript=ChatOutput.Text+"\n\n";if(transcript.Length>200000)transcript=transcript[^200000..];Store.Save("chat.json",transcript);SendButton.IsEnabled=true;}
    }
    async void SetupClick(object sender,RoutedEventArgs e)=>await Configure(false);
    async void DisableClick(object sender,RoutedEventArgs e)=>await Configure(true);
    async Task Configure(bool disable)
    {
        try
        {
            Security.PackageTrust.AssertInstalledUpdater(AppContext.BaseDirectory);
            var script=Path.Combine(AppContext.BaseDirectory,"Setup.ps1");
            if(!File.Exists(script))throw new Exception("Setup.ps1 fehlt im Programmordner.");
            var psi=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Environment.GetFolderPath(Environment.SpecialFolder.System)};
            psi.Arguments=$"-NoProfile -ExecutionPolicy Bypass -File \"{script}\""+(disable?" -Disable":"");
            using var p=Process.Start(psi)!;await p.WaitForExitAsync();
            if(p.ExitCode!=0)throw new Exception("Windows-Einrichtung wurde nicht abgeschlossen (Code "+p.ExitCode+").");
            Store.Log("Automatik",disable?"Wartungsauftrag deaktiviert.":"Geschützte Installation und Wartungsauftrag eingerichtet.");
            if(!disable){ready=false;StartupBox.IsChecked=true;ready=true;}
            StatusLine.Text=disable?"Update-Automatik deaktiviert.":"Update-Automatik eingerichtet. Suche alle 30 Minuten; Installation im gewählten Zeitfenster.";RefreshWorker();
        }catch(Exception ex){StatusLine.Text="Einrichtung: "+ex.Message;}
    }
    async void RefreshWorker()
    {
        try
        {
            var task=await PowerShell.Script("$t=Get-ScheduledTask -TaskName 'DriverPilot Maintenance' -ErrorAction SilentlyContinue; if($t){[string]$t.State}else{'Missing'}",15);
            AutoStatus.Text=task=="Missing"?"Nicht eingerichtet":task=="Disabled"?"Deaktiviert":"Eingerichtet";
            var path=Path.Combine(Store.Shared,"maintenance.json");
            if(File.Exists(path))
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(path));WorkerStatus.Text=doc.RootElement.GetProperty("Status").GetString();
                var status=WorkerStatus.Text??"";var key="maintenance:"+doc.RootElement.GetProperty("Time").GetString()+":"+status;
                if(preferences.Notify&&(status.Contains("Neustart")||status.Contains("fehler",StringComparison.OrdinalIgnoreCase)||status.Contains("gestoppt"))&&preferences.Notified.Add(key))
                {tray.ShowBalloonTip(8000,"DriverPilot · Wartung",status,Forms.ToolTipIcon.Info);preferences.Notified.RemoveWhere(k=>k.StartsWith("maintenance:")&&k!=key);Store.Save("preferences.json",preferences);}
            }
            else WorkerStatus.Text=task=="Missing"?"Automatik noch nicht eingerichtet.":"Wartungsauftrag wartet auf den ersten Lauf.";
        }catch{AutoStatus.Text="Status unbekannt";}
    }
    void RefreshHistory()
    {
        var rows=Store.Read<List<HistoryRow>>("history.json")??[];
        try{var path=Path.Combine(Store.Shared,"maintenance.json");if(File.Exists(path)){using var doc=JsonDocument.Parse(File.ReadAllText(path));foreach(var h in doc.RootElement.GetProperty("History").EnumerateArray())rows.Add(new(DateTime.Parse(h.GetProperty("Time").GetString()!).ToString("dd.MM.yyyy HH:mm"),"Treiberinstallation",h.GetProperty("Title").GetString()+" · Ergebnis "+h.GetProperty("Result")));}}catch{}
        HistoryPage.ItemsSource=rows.OrderByDescending(r=>DateTime.TryParse(r.Time,out var d)?d:DateTime.MinValue).ToList();
    }
    internal void LoadPreview(Snapshot data){snapshot=data;RenderSnapshot();StatusLine.Text="Prüfung mit echten lokalen Gerätedaten abgeschlossen.";}
    internal void LoadManufacturerPreview(ManufacturerReport report)=>RenderManufacturers(report);
    internal void SelectPreviewPage(int index)=>Navigate(new System.Windows.Controls.Button{Tag=index.ToString()},new RoutedEventArgs());
    internal void StopForTest(){exit=true;timer.Stop();tray.Dispose();codex.Dispose();}
}
