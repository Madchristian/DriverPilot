using System.Security.Cryptography;
using System.Text.Json;
using DriverPilot;
var folder=Path.Combine(Environment.GetEnvironmentVariable("TMPDIR")??Path.GetTempPath(),"DriverPilot-test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try {
 foreach(var name in new[]{"DriverPilot.exe","DriverPilot.dll","DriverPilot.deps.json","DriverPilot.runtimeconfig.json","Setup.ps1"})File.Copy(typeof(App).Assembly.Location,Path.Combine(folder,name));
 var files=Directory.GetFiles(folder).ToDictionary(p=>Path.GetFileName(p)!, p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
 File.WriteAllText(Path.Combine(folder,"package-manifest.json"),JsonSerializer.Serialize(new{Product="DriverPilot",Version="0.3.2",Files=files}));
 bool rejected=false;try{PackageVerifier.Verify(folder,false);}catch{rejected=true;}
 if(!rejected)throw new Exception("SECURITY: unsigned package accepted");
 Console.WriteLine("PASS unsigned package rejected");
} finally {Directory.Delete(folder,true);}
SecurityTests.Run();
namespace DriverPilot {public class App {} public static class Store {public static JsonSerializerOptions Json=new();}}
