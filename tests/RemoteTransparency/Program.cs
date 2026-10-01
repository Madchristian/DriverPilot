internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("AppXaml")!;
        var source = System.Xml.Linq.XDocument.Load(stream);
        System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new System.Xml.Linq.XElement(ns + "ResourceDictionary", new System.Xml.Linq.XAttribute(System.Xml.Linq.XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), source.Root!.Element(ns + "Application.Resources")!.Elements());
        app.Resources = (System.Windows.ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString());
        var exit = 0;
        // Plain Application only: never construct the productive App or MainWindow in this harness.
        app.Startup += async (_, _) =>
        {
            try { Console.WriteLine(await DriverPilot.RemoteTransparencyTests.Run(args.FirstOrDefault() ?? AppContext.BaseDirectory)); }
            catch (Exception ex) { Console.WriteLine(ex); exit = 1; }
            finally { app.Shutdown(exit); }
        };
        return app.Run();
    }
}
