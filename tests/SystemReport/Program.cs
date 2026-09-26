using System.Windows;
using System.Windows.Markup;
using System.Xml.Linq;
internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Console.WriteLine(DriverPilot.SystemReportTests.RunBackend());
            if (args.Contains("--integration"))
            {
                var main = System.IO.File.ReadAllText("MainWindow.xaml.cs");
                var startup = System.IO.File.ReadAllText("App.xaml.cs");
                var view = System.IO.File.ReadAllText("MainWindow.xaml");
                if (!view.Contains("Click=\"SystemReportClick\"") || !main.Contains("SystemReportSession.IsFresh(snapshot, reportScanAt") || !main.Contains("reportScanAt=null")) throw new Exception("FAIL: hardware button / fresh-scan gate wiring");
                if (!startup.Contains("--system-report-test") || startup.IndexOf("--system-report-test", StringComparison.Ordinal) > startup.IndexOf("base.OnStartup(e)", StringComparison.Ordinal)) throw new Exception("FAIL: isolated test mode before normal startup");
                if (!main.Contains("reportContext.BeginScan()") || !main.Contains("reportContext.CompleteScan(generation, snapshot") || !main.Contains("reportContext.PublishManufacturers(generation, scan, report") || !main.Contains("reportContext.SummaryFor(snapshot!") || !main.Contains("ManufacturerScanner.Scan(scan, persist: false)")) throw new Exception("FAIL: generation-bound manufacturer integration");
                if (main.Contains("Store.Read<ManufacturerReport>")) throw new Exception("FAIL: unbound persisted manufacturer results must not reload on startup/navigation");
                Console.WriteLine("PASS: MainWindow fresh-scan/button and early isolated startup wiring");
            }
            if (!args.Contains("--ui")) return 0;
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var source = XDocument.Load("App.xaml");
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var dictionary = new XElement(ns + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), source.Root!.Element(ns + "Application.Resources")!.Elements());
            app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
            int exit = 0;
            app.Startup += async (_, _) =>
            {
                try { Console.WriteLine(await DriverPilot.SystemReportTests.RunUi(AppContext.BaseDirectory)); }
                catch (Exception ex) { Console.WriteLine(ex); exit = 1; }
                finally { app.Shutdown(exit); }
            };
            return app.Run();
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
    }
}