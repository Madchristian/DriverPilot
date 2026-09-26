using System.IO;
namespace DriverPilot;
public record PackageManifest(string Product,string Version,Dictionary<string,string> Files,string ManifestHash);
public static class PackageVerifier
{
    public static PackageManifest Verify(string folder,bool requireNewer=true)
    {
        var m=Security.PackageTrust.Verify(folder,Path.Combine(AppContext.BaseDirectory,"DriverPilot.cer"),typeof(App).Assembly.GetName().Version!.ToString(3),requireNewer);
        return new(m.Product,m.Version,m.Files,Security.PackageTrust.ManifestHash(m));
    }
}
