using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace DriverPilot;
public static class SelfTest
{
    public static async Task Run()
    {
        var findings=new List<string>();
        var raw=await PowerShell.Run("Inventory");
        var snapshot=JsonSerializer.Deserialize<Snapshot>(raw,Store.Json)??throw new Exception("Inventory returned null");
        if(snapshot.Devices.Count<1||string.IsNullOrEmpty(snapshot.Cpu))throw new Exception("Hardware enumeration failed");
        findings.Add($"Hardware scan: {snapshot.Devices.Count} devices, {snapshot.Findings.Count} findings, {snapshot.Warnings.Count} warnings.");
        var details=snapshot.Details??throw new Exception("Detail inventory missing");
        if(details.Cpu==null||details.Memory==null||details.Storage==null||details.Platform==null||details.Gpus==null)throw new Exception("Detail inventory incomplete: "+string.Join("; ",snapshot.Warnings));
        findings.Add($"Detail inventory: {details.Memory.Modules?.Count??0} memory modules, {details.Gpus.Count} display adapters, {details.Storage.Disks?.Count??0} disks, nvidia-smi rows {details.NvidiaSmi?.Count??0}.");
        if(Rules.ShouldNotify(new("disabled","WLAN","disabled","Info",""),new()))throw new Exception("Disabled device notification regression");
        var preferences=new Preferences();preferences.Ignored.Add("ignored");
        if(Rules.ShouldNotify(new("ignored","Known","known","Warnung",""),preferences))throw new Exception("Known issue notification regression");
        if(!Rules.ShouldNotify(new("new","Fault","fault","Warnung",""),preferences))throw new Exception("New fault notification failed");
        findings.Add("Notifications: informational and acknowledged findings suppressed; new warnings enabled.");
        findings.Add(await PowerShell.Run("PolicyTests"));
        findings.Add(FeatureTests.Run());
        findings.Add(RemoteDiagnosisTests.Run(snapshot));
        findings.Add("Codex tests skipped: app-server starts only for an explicit user action in the app.");
        var window=new MainWindow();window.LoadPreview(snapshot);
        var surface=(FrameworkElement)window.Content;
        surface.Measure(new System.Windows.Size(1280,850));surface.Arrange(new Rect(0,0,1280,850));surface.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1280,850,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
        using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"preview.png")))png.Save(output);
        findings.Add("WPF overview rendered at 1280 x 850.");
        foreach(var page in new[]{1,2,3,4,5,6,8,9,10,11})
        {
            window.SelectPreviewPage(page);surface.Measure(new System.Windows.Size(1000,700));surface.Arrange(new Rect(0,0,1000,700));surface.UpdateLayout();
            var pageBitmap=new RenderTargetBitmap(1000,700,96,96,PixelFormats.Pbgra32);pageBitmap.Render(surface);var pagePng=new PngBitmapEncoder();pagePng.Frames.Add(BitmapFrame.Create(pageBitmap));
            using var output=File.Create(Path.Combine(AppContext.BaseDirectory,$"preview-{page}.png"));pagePng.Save(output);
        }
        findings.Add("All ten additional WPF pages rendered at minimum size 1000 x 700.");
        window.StopForTest();
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"test-results.txt"),string.Join(Environment.NewLine,findings));
    }
}
