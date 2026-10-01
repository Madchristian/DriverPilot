using System.IO;
using System.Windows;
namespace DriverPilot;
public partial class App : System.Windows.Application
{
    private Mutex? single;
    protected override async void OnStartup(StartupEventArgs e)
    {
        // Isolated synthetic path: before base startup, mutex, Store, tray, timers or scans.
        if(e.Args.Contains("--remote-transparency-test"))
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            var output=AppContext.BaseDirectory;
            var index=Array.IndexOf(e.Args,"--remote-transparency-output");
            if(index>=0 && index+1<e.Args.Length)output=Path.GetFullPath(e.Args[index+1]);
            try
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output,"remote-transparency-test-results.txt"),await RemoteTransparencyTests.Run(output));
                Shutdown(0);
            }
            catch(Exception ex)
            {
                try{File.WriteAllText(Path.Combine(output,"remote-transparency-test-failure.txt"),ex.ToString());}catch{}
                Shutdown(1);
            }
            return;
        }
        if(e.Args.Contains("--system-report-test"))
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            var output=AppContext.BaseDirectory;
            var index=Array.IndexOf(e.Args,"--system-report-output");
            if(index>=0 && index+1<e.Args.Length)output=Path.GetFullPath(e.Args[index+1]);
            try
            {
                Directory.CreateDirectory(output);
                var result=SystemReportTests.RunBackend()+"\n"+await SystemReportTests.RunUi(output);
                File.WriteAllText(Path.Combine(output,"system-report-test-results.txt"),result);Shutdown(0);
            }
            catch(Exception ex)
            {
                try{File.WriteAllText(Path.Combine(output,"system-report-test-failure.txt"),ex.ToString());}catch{}
                Shutdown(1);
            }
            return;
        }
        base.OnStartup(e);
        if(e.Args.Length==2 && e.Args[0]=="--wait-for" && int.TryParse(e.Args[1],out var previousPid))
        {try{using var old=System.Diagnostics.Process.GetProcessById(previousPid);await old.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));}catch(ArgumentException){}catch{Shutdown(1);return;}}
        if(e.Args.Contains("--manufacturer-test"))
        {
            try{File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"manufacturer-test-results.txt"),await ManufacturerTests.Run());Shutdown(0);}
            catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"manufacturer-test-failure.txt"),ex.ToString());Shutdown(1);}return;
        }
        if(e.Args.Contains("--pipe-fixture"))
        { try{await TransportTests.EmitFixture();Shutdown(0);}catch{Shutdown(1);}return; }
        if(e.Args.Contains("--transport-test"))
        {
            try{File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"transport-test-results.txt"),await TransportTests.Run());Shutdown(0);}
            catch(Exception ex){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"transport-test-failure.txt"),ex.ToString());Shutdown(1);}return;
        }
        if (e.Args.Contains("--self-test"))
        {
            try { await SelfTest.Run(); Shutdown(0); } catch (Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"test-failure.txt"),ex.ToString()); Shutdown(1); }
            return;
        }
        LifecycleGate? startup;
        try { startup = LifecycleGate.TryEnter("Global\\DriverPilot.UninstallLifecycle"); }
        catch { Shutdown(1); return; } // Access-denied/unknown gate state must not start any work.
        if (startup == null) { Shutdown(1); return; }
        using (startup)
        {
        DispatcherUnhandledException += (_, args) => { Store.Log("Fehler", args.Exception.Message); System.Windows.MessageBox.Show(args.Exception.Message, "DriverPilot"); args.Handled = true; };
        if (e.Args.Contains("--maintenance"))
        {
            startup.Dispose();
            // The child itself owns the cross-session maintenance lock, even if this parent exits.
            try { await PowerShell.Run("Maintenance", 10800); Shutdown(0); } catch (Exception ex) { Store.Log("Wartungsfehler", ex.Message); Shutdown(1); }
            return;
        }
        single = new Mutex(true, "Local\\DriverPilot.Desktop", out bool created);
        if (!created) { System.Windows.MessageBox.Show("DriverPilot läuft bereits. Öffne es über das Symbol im Infobereich."); Shutdown(); return; }
        startup.Dispose(); // Existing process is now visible to the uninstaller; later starts stay gated.
        var window = new MainWindow(); MainWindow = window;
        if (!e.Args.Contains("--tray")) window.Show();
        await window.InitializeAsync();
        }
    }
    protected override void OnExit(ExitEventArgs e) { single?.Dispose(); base.OnExit(e); }
}
