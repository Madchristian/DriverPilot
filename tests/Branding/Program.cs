using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;

internal static class Program
{
    const string Author = "Entwickelt von Christian Strube";
    static readonly XNamespace Ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    static string Resource(string name)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }
    [STAThread] static int Main()
    {
        try
        {
            var source = XDocument.Parse(Resource("MainWindowXaml"));
            var title = source.Descendants(Ns + "TextBlock").Single(e => (string?)e.Attribute("Text") == "◈  DriverPilot");
            var author = title.ElementsAfterSelf().FirstOrDefault();
            Require(author?.Name == Ns + "TextBlock" && (string?)author.Attribute("Text") == Author,
                "exact author line immediately under app name");
            var settings = source.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "SettingsPage");
            var infoAuthor = settings.Descendants(Ns + "TextBlock").SingleOrDefault(e => (string?)e.Attribute("Text") == Author);
            Require(infoAuthor != null, "exact author line in settings Info card");
            Require((double)author!.Attribute("FontSize")! < (double)title.Attribute("FontSize")!, "subtle header hierarchy");
            Require((string?)author.Attribute("Foreground") != null, "explicit muted author color");
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var resources = XDocument.Parse(Resource("AppXaml")).Root!.Element(Ns + "Application.Resources")!;
            var dictionary = new XElement(Ns + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", X), resources.Elements());
            app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
            // Render only productive branding fragments; never construct MainWindow or start app services.
            foreach (var fragment in new[] { title.Parent!, infoAuthor!.Ancestors(Ns + "Border").First() })
            {
                var copy = new XElement(fragment);
                copy.SetAttributeValue(XNamespace.Xmlns + "x", X);
                var element = (FrameworkElement)XamlReader.Parse(copy.ToString());
                var sidebar = fragment == title.Parent;
                var host = new Border { Child = element, Padding = sidebar ? new Thickness(22, 30, 22, 30) : new Thickness(0) };
                var window = new Window { Content = host, Width = sidebar ? 230 : 706, Height = 300,
                    WindowStyle = WindowStyle.None, ShowInTaskbar = false, Background = System.Windows.Media.Brushes.Black };
                try
                {
                    window.Show(); window.UpdateLayout();
                    var texts = Descendants(element).OfType<TextBlock>().Where(t => t.Text == Author).ToArray();
                    Require(texts.Length == 1, "one rendered author per surface");
                    var text = texts[0];
                    text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Require(text.DesiredSize.Width <= text.ActualWidth + 0.5, "author fits without horizontal clipping");
                    Require(text.ActualHeight > 0 && text.IsVisible, "author visible in isolated rendered surface");
                }
                finally { window.Close(); }
            }
            app.Shutdown();
            Console.WriteLine("PASS: exact header/settings author, hierarchy and isolated WPF layout (no normal startup)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
