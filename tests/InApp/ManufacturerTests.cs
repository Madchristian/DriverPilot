using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace DriverPilot;
internal static class ManufacturerTests
{
    public static async Task<string> Run()
    {
        foreach(var url in new[]{"http://www.amd.com/","https://www.amd.com.evil.example/","https://www.amd.com@evil.example/","file:///C:/test.exe","https://www.amd.com:444/"})
            if(ManufacturerScanner.OfficialUrl(url))throw new Exception("Unsafe source URL accepted: "+url);
        if(!ManufacturerScanner.OfficialUrl(ManufacturerScanner.AmdUrl))throw new Exception("Official source rejected.");
        if(ManufacturerScanner.NvidiaVersion("32.0.16.1714")!="617.14"||ManufacturerScanner.NvidiaVersion("31.0.15.3640")!="536.40")throw new Exception("NVIDIA version conversion failed.");
        var html="<article><h4>AMD Chipset Drivers</h4><strong>Revision Number</strong><p>8.08.12.551</p><strong>Release Date</strong><p>2020-08-14</p></article><article><h4>AMD RAID Driver</h4><p>99.0.0.0</p></article>";
        var amd=ManufacturerScanner.ParseAmd(html,"8.05.04.516");
        if(!amd.Newer||amd.Available!="8.08.12.551")throw new Exception("AMD package comparison / section isolation failed.");
        var unknown=ManufacturerScanner.ParseAmd(html,"");if(unknown.Newer||unknown.Status!="Versionsvergleich offen")throw new Exception("Unknown installed version presented as update.");
        if(ManufacturerScanner.ParseAmd(html,"9.0.0.0").Newer)throw new Exception("Downgrade offered as newer.");
        bool rejected=false;try{ManufacturerScanner.ParseAmd("Access denied","8.0.0.0");}catch{rejected=true;}if(!rejected)throw new Exception("Blocked page treated as valid.");
        var info=new{Success="1",IDS=new[]{new{downloadInfo=new{IsBeta="1",IsWHQL="1",IsFeaturePreview="0",Version="999.99",DetailsURL="https://www.nvidia.com/en-us/drivers/",ReleaseDateTime="Tue Sep 22, 2020"}},new{downloadInfo=new{IsBeta="0",IsWHQL="1",IsFeaturePreview="0",Version="617.14",DetailsURL="https://www.nvidia.com/en-us/drivers/",ReleaseDateTime="Tue Sep 22, 2020"}}}};
        using var doc=JsonDocument.Parse(JsonSerializer.Serialize(info));var nv=ManufacturerScanner.ParseNvidia(doc.RootElement,"NVIDIA GeForce RTX 4090","617.14");
        if(nv.Newer||nv.Available!="617.14")throw new Exception("NVIDIA beta filtering/equal-version comparison failed.");
        var snapshot=JsonSerializer.Deserialize<Snapshot>(await PowerShell.Run("Inventory"),Store.Json)!;
        var report=await ManufacturerScanner.Scan(snapshot);
        if(report.Findings.Count==0)throw new Exception("No manufacturer entries generated for this PC.");
        if(report.Findings.Any(f=>f.Category.Contains("Firmware")&&f.Newer))throw new Exception("Unverified firmware marked as update.");
        var window=new MainWindow();window.LoadPreview(snapshot);window.LoadManufacturerPreview(report);window.SelectPreviewPage(7);
        var surface=(FrameworkElement)window.Content;surface.Measure(new System.Windows.Size(1280,850));surface.Arrange(new Rect(0,0,1280,850));surface.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1280,850,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
        using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"manufacturer-preview.png")))png.Save(output);window.StopForTest();
        return "PASS: official HTTPS URLs, deceptive URLs rejected, NVIDIA version mapping, AMD section isolation, missing installed version, downgrade, blocked HTML, beta filtering.\nLive read-only scan:\n"+string.Join("\n",report.Findings.Select(f=>$"{f.Device} | {f.Installed} -> {f.Available} | {f.Status}"))+"\nManufacturer page rendered. No driver/firmware download, install or reboot.";
    }
}
