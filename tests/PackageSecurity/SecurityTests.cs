using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using DriverPilot;
using DriverPilot.Security;
static class SecurityTests {
public static void Run(){
var folder=Path.Combine(Environment.GetEnvironmentVariable("TMPDIR")??Path.GetTempPath(),"DriverPilot-test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
using var key=RSA.Create(2048);
using var cert=new CertificateRequest("CN=Ephemeral test only",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
int passed=0;
void Reject(Action action,string label){try{action();}catch{Console.WriteLine("PASS "+label);passed++;return;}throw new Exception("SECURITY: accepted "+label);}
try {
 foreach(var name in new[]{"DriverPilot.exe","DriverPilot.dll","DriverPilot.deps.json","DriverPilot.runtimeconfig.json","Setup.ps1","PackageTrust.cs","DriverPilot.cer"})File.Copy(typeof(App).Assembly.Location,Path.Combine(folder,name));
 const string candidateScript="throw 'Candidate Setup.ps1 must never execute during verification'";
 File.WriteAllText(Path.Combine(folder,"Setup.ps1"),candidateScript);
 File.WriteAllText(Path.Combine(folder,"PackageTrust.cs"),"INVALID CANDIDATE C# - must not be compiled for verification");
 var files=Directory.GetFiles(folder).ToDictionary(p=>Path.GetFileName(p)!,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
 var manifest=Path.Combine(folder,PackageTrust.ManifestName);var sig=Path.Combine(folder,PackageTrust.SignatureName);
 string original=JsonSerializer.Serialize(new{Product="DriverPilot",Version="0.3.2",Files=files});
 void Sign(string text){File.WriteAllText(manifest,text);File.WriteAllBytes(sig,key.SignData(File.ReadAllBytes(manifest),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));}
 VerifiedPackage Verify()=>PackageTrust.VerifyCore(folder,cert,"0.3.1",true,DateTime.UtcNow);
 Sign(original);var good=Verify();Console.WriteLine("PASS valid authenticated package");passed++;
 Reject(()=>PackageVerifier.Verify(folder,false),"fixture signer not authorized in production");
 File.WriteAllText(Path.Combine(folder,"Setup.ps1"),"evil");Reject(()=>Verify(),"changed payload");
 files["Setup.ps1"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,"Setup.ps1"))));
 File.WriteAllText(manifest,JsonSerializer.Serialize(new{Product="DriverPilot",Version="0.3.2",Files=files}));Reject(()=>Verify(),"rehashed manifest without authorized signature");
 File.WriteAllText(Path.Combine(folder,"Setup.ps1"),candidateScript);Sign(original);
 var b=File.ReadAllBytes(sig);b[0]^=1;File.WriteAllBytes(sig,b);Reject(()=>Verify(),"corrupt signature");Sign(original);
 using var other=RSA.Create(2048);File.WriteAllBytes(sig,other.SignData(File.ReadAllBytes(manifest),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));Reject(()=>Verify(),"wrong key");Sign(original);
 Reject(()=>PackageTrust.VerifyCore(folder,cert,"0.3.2",true,DateTime.UtcNow),"same version");
 Reject(()=>PackageTrust.VerifyCore(folder,cert,"0.4.0",true,DateTime.UtcNow),"downgrade");
 Reject(()=>PackageTrust.VerifyCore(folder,cert,"0.3.1",true,DateTime.UtcNow.AddDays(2)),"expired signer");
 Reject(()=>PackageTrust.VerifyCore(folder,cert,"0.3.1",true,DateTime.UtcNow.AddDays(-2)),"not-yet-valid signer");
 foreach(var text in new[]{original.Replace("0.3.2","0.3.3"),original.Replace("0.3.2","0.3.2.0"),original.Replace("\"Product\":","\"Product\":\"DriverPilot\",\"Product\":"),original.Replace("\"Files\":","\"Unknown\":\"x\",\"Files\":"),original+"{}",original.Replace("DriverPilot.exe","../outside.exe"),original.Replace("DriverPilot.exe","CON.exe"),original.Replace("DriverPilot.exe","DriverPilot.exe."),original.Replace("DriverPilot.exe","DriverPilot.exe:stream"),original.Replace("DriverPilot.exe","driverpilot.dll")}) {Sign(text);Reject(()=>Verify(),"strict schema/path/version");}
 Sign(original);File.WriteAllText(Path.Combine(folder,"unlisted.dll"),"extra");Reject(()=>Verify(),"unlisted payload");File.Delete(Path.Combine(folder,"unlisted.dll"));
 var stage=folder+"-stage";Directory.CreateDirectory(stage);
 try{good=Verify();File.WriteAllText(manifest,"changed after verify");PackageTrust.CopyVerified(folder,stage,good);PackageTrust.VerifyCore(stage,cert,"0.3.1",true,DateTime.UtcNow);Console.WriteLine("PASS exact authenticated metadata preserved during staging");passed++;}
 finally{Directory.Delete(stage,true);}
 Sign(original);good=Verify();var approved=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifest)));
 Sign(original.Replace("{\"Product\"","{ \"Product\""));var swapped=Verify();
 var method=typeof(PackageTrust).GetMethod("AssertApproval");
 if(method==null)throw new Exception("SECURITY: no approved-manifest binding at privileged boundary");
 method.Invoke(null,new object[]{good,approved});Reject(()=>method.Invoke(null,new object[]{swapped,approved}),"different signed manifest after approval");
 foreach(var attack in new Dictionary<string,string>{
  ["wrong product"]=original.Replace("\"Product\":\"DriverPilot\"","\"Product\":\"Other\""),
  ["empty inventory"]="{\"Product\":\"DriverPilot\",\"Version\":\"0.3.2\",\"Files\":{}}",
  ["missing required executable"]=original.Replace("DriverPilot.exe","Unused.exe"),
  ["UTF-8 BOM forbidden"]="\uFEFF"+original,
  ["null field"]=original.Replace("\"Version\":\"0.3.2\"","\"Version\":null"),
  ["leading zero version"]=original.Replace("0.3.2","00.3.2"),
  ["case-aliased duplicate root"]=original.Replace("\"Product\":","\"product\":\"DriverPilot\",\"Product\":"),
  ["metadata self-inventory"]=original.Replace("DriverPilot.exe","package-manifest.json"),
  ["trailing space path"]=original.Replace("DriverPilot.exe","DriverPilot.exe ")
 }){Sign(attack.Value);Reject(()=>Verify(),attack.Key);}
 Sign(original);Directory.CreateDirectory(Path.Combine(folder,"unexpected"));Reject(()=>Verify(),"unlisted directory");Directory.Delete(Path.Combine(folder,"unexpected"));
 Sign(original.Replace("\"Product\"","\"product\""));Reject(()=>Verify(),"noncanonical top-level property case");Sign(original);
 Reject(()=>PackageTrust.AssertInstalledUpdater(folder),"portable updater elevation");
 var export=Environment.GetEnvironmentVariable("DRIVERPILOT_TEST_EXPORT");
 if(!string.IsNullOrEmpty(export)) {
  Directory.CreateDirectory(export);var exported=Path.Combine(export,"package");Directory.CreateDirectory(exported);
  Sign(original);var fixture=Verify();PackageTrust.CopyVerified(folder,exported,fixture);
  File.WriteAllBytes(Path.Combine(export,"test-signer.cer"),cert.Export(X509ContentType.Cert));
  Console.WriteLine("Fixture exported: "+export);
 }
 Console.WriteLine($"PASS {passed} security checks");
} finally {Directory.Delete(folder,true);}
}}
