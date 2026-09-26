using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
namespace DriverPilot;
public record PackageManifest(string Product,string Version,Dictionary<string,string> Files);
public static class PackageVerifier
{
    public static PackageManifest Verify(string folder,bool requireNewer=true)
    {
        if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new Exception("Paketordner darf keine Verknüpfung sein.");
        var manifestPath=Path.Combine(folder,"package-manifest.json");
        if((File.GetAttributes(manifestPath)&FileAttributes.ReparsePoint)!=0)throw new Exception("Manifest darf keine Verknüpfung sein.");
        var m=JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(manifestPath),Store.Json)??throw new Exception("Paketmanifest fehlt.");
        return Validate(folder,m,requireNewer);
    }
    internal static PackageManifest Validate(string folder,PackageManifest m,bool requireNewer)
    {
        if(m.Product!="DriverPilot"||!Version.TryParse(m.Version,out var version)||m.Files.Count==0||m.Files.Count>1000)throw new Exception("Kein gültiges DriverPilot-Paket.");
        if(requireNewer&&version<=typeof(App).Assembly.GetName().Version)throw new Exception("Das Paket ist nicht neuer als die laufende App.");
        foreach(var required in new[]{"DriverPilot.exe","DriverPilot.dll","DriverPilot.deps.json","DriverPilot.runtimeconfig.json","Setup.ps1"})if(!m.Files.ContainsKey(required))throw new Exception("Paket unvollständig: "+required);
        foreach(var (name,hash) in m.Files){
            if(Path.GetFileName(name)!=name||name.Contains(':')||name.Contains('/')||name.Contains('\\')||name=="package-manifest.json"||!new[]{".exe",".dll",".json",".ps1"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase))throw new Exception("Ungültiger Paketpfad.");
            var path=Path.Combine(folder,name);if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new Exception("Verknüpfung im Paket.");
            using var stream=File.OpenRead(path);if(!Convert.ToHexString(SHA256.HashData(stream)).Equals(hash,StringComparison.OrdinalIgnoreCase))throw new Exception("Prüfsumme stimmt nicht: "+name);
        }
        if(Version.Parse(System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(folder,"DriverPilot.dll")).FileVersion!)!=new Version(version.Major,version.Minor,Math.Max(version.Build,0),0))throw new Exception("Programmversion und Manifest stimmen nicht überein.");
        return m;
    }
}
