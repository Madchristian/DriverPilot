// Shared with the INSTALLED Windows PowerShell updater. Keep compatible with C# 5/.NET Framework 4.8.
#if NET
#pragma warning disable 8618, 8600, 8602, 8604
#pragma warning disable SYSLIB0057 // Shared with Windows PowerShell/.NET Framework.
#endif
using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
namespace DriverPilot.Security
{
    public sealed class VerifiedPackage
    {
        public string Product;
        public string Version;
        public Dictionary<string,string> Files;
        internal byte[] ManifestBytes;
        internal byte[] SignatureBytes;
    }
    public static class PackageTrust
    {
        // SHA-256 of the exact authorized DER certificate, NOT a package-provided identity.
        public const string CertificateHash = "2C9F679EA51A77CDBC9D64E171B6C12FC9E31EBE623C8C6E357CDE5B6C9F929B";
        public const string ManifestName = "package-manifest.json";
        public const string SignatureName = "package-manifest.sig";
        public static VerifiedPackage Verify(string folder, string certificatePath, string currentVersion, bool requireNewer)
        {
            NoReparse(certificatePath);
            var der = File.ReadAllBytes(certificatePath);
            using(var sha=SHA256.Create())
                if(Hex(sha.ComputeHash(der)) != CertificateHash) throw new Exception("Unauthorized update certificate.");
            using(var cert=new X509Certificate2(der)) return VerifyCore(folder,cert,currentVersion,requireNewer,DateTime.UtcNow);
        }
        internal static VerifiedPackage VerifyCore(string folder,X509Certificate2 cert,string currentVersion,bool requireNewer,DateTime now)
        {
            NoReparse(folder);
            NoReparse(Path.Combine(folder,ManifestName));
            NoReparse(Path.Combine(folder,SignatureName));
            byte[] bytes=ReadLimited(Path.Combine(folder,ManifestName),1024*1024);
            byte[] signature=ReadLimited(Path.Combine(folder,SignatureName),16384);
            if(now<cert.NotBefore.ToUniversalTime() || now>cert.NotAfter.ToUniversalTime()) throw new Exception("Update certificate is not currently valid.");
            using(var rsa=cert.GetRSAPublicKey()) {
                if(rsa==null || rsa.KeySize<2048 || !rsa.VerifyData(bytes,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)) throw new Exception("Invalid package manifest signature.");
            }
            var root=new ManifestReader(new UTF8Encoding(false,true).GetString(bytes)).Read();
            if(root.Count!=3 || !root.ContainsKey("Product") || !root.ContainsKey("Version") || !root.ContainsKey("Files"))throw new Exception("Invalid manifest schema.");
            foreach(var name in root.Keys) if(name!="Product" && name!="Version" && name!="Files")throw new Exception("Noncanonical manifest field.");
            var product=root["Product"] as string;var version=root["Version"] as string;var entries=root["Files"] as Dictionary<string,object>;
            if(product!="DriverPilot" || version==null || !Regex.IsMatch(version,@"\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z") || entries==null || entries.Count<7 || entries.Count>1000)throw new Exception("Invalid package.");
            var parsed=new Version(version);
            if(requireNewer && parsed<=new Version(currentVersion))throw new Exception("Package is not newer than installed version.");
            var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(var pair in entries) {
                string name=pair.Key, hash=pair.Value as string;
                if(!Regex.IsMatch(name,@"\A[A-Za-z0-9_][A-Za-z0-9_.-]*\.(exe|dll|json|ps1|cs|cer|md|txt)\z",RegexOptions.IgnoreCase) || name.Contains("..") || name.Equals(ManifestName,StringComparison.OrdinalIgnoreCase) || name.Equals(SignatureName,StringComparison.OrdinalIgnoreCase) || Regex.IsMatch(name,@"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])\.",RegexOptions.IgnoreCase) || hash==null || !Regex.IsMatch(hash,@"\A[0-9A-Fa-f]{64}\z") || files.ContainsKey(name))throw new Exception("Invalid package path/hash.");
                files.Add(name,hash);
            }
            foreach(string required in new[]{"DriverPilot.exe","DriverPilot.dll","DriverPilot.deps.json","DriverPilot.runtimeconfig.json","Setup.ps1","PackageTrust.cs","DriverPilot.cer"}) if(!files.ContainsKey(required))throw new Exception("Incomplete package: "+required);
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string path in Directory.GetFileSystemEntries(folder)) {
                NoReparse(path);
                string name=Path.GetFileName(path);
                if(!seen.Add(name) || Directory.Exists(path) || (!files.ContainsKey(name) && name!=ManifestName && name!=SignatureName))throw new Exception("Unlisted package entry: "+name);
            }
            foreach(var pair in files) {
                var path=Path.Combine(folder,pair.Key);NoReparse(path);
                using(var stream=File.OpenRead(path)) using(var sha=SHA256.Create()) if(!Hex(sha.ComputeHash(stream)).Equals(pair.Value,StringComparison.OrdinalIgnoreCase))throw new Exception("Package hash mismatch: "+pair.Key);
            }
            string binaryVersion=System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(folder,"DriverPilot.dll")).FileVersion;
            if(binaryVersion!=version+".0")throw new Exception("Binary/manifest version mismatch.");
            return new VerifiedPackage{Product=product,Version=version,Files=files,ManifestBytes=bytes,SignatureBytes=signature};
        }
        public static void AssertInstalledUpdater(string folder)
        {
            var expected=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"DriverPilot");
            if(!Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new Exception("Portable app updates are disabled. Use the verified signed installer.");
            NoReparse(folder);
            AssertAcl(new DirectoryInfo(Path.GetDirectoryName(expected)).GetAccessControl());
            AssertAcl(new DirectoryInfo(expected).GetAccessControl());
            foreach(var path in Directory.GetFileSystemEntries(expected)) {
                NoReparse(path);
                if(Directory.Exists(path))throw new Exception("Unexpected installed directory.");
                AssertAcl(new FileInfo(path).GetAccessControl());
            }
        }
        static void AssertAcl(System.Security.AccessControl.FileSystemSecurity acl)
        {
            var trusted=new HashSet<string>{"S-1-5-18","S-1-5-32-544","S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"};
            if(!trusted.Contains(acl.GetOwner(typeof(System.Security.Principal.SecurityIdentifier)).Value))throw new Exception("Untrusted updater owner.");
            var write=System.Security.AccessControl.FileSystemRights.Write|System.Security.AccessControl.FileSystemRights.Delete|System.Security.AccessControl.FileSystemRights.DeleteSubdirectoriesAndFiles|System.Security.AccessControl.FileSystemRights.ChangePermissions|System.Security.AccessControl.FileSystemRights.TakeOwnership;
            foreach(System.Security.AccessControl.FileSystemAccessRule rule in acl.GetAccessRules(true,true,typeof(System.Security.Principal.SecurityIdentifier)))
                if(rule.AccessControlType==System.Security.AccessControl.AccessControlType.Allow && (rule.PropagationFlags&System.Security.AccessControl.PropagationFlags.InheritOnly)==0 && (((long)rule.FileSystemRights&((long)write|0x50000000L))!=0) && !trusted.Contains(rule.IdentityReference.Value))throw new Exception("Writable updater ACL.");
        }
        public static string ManifestHash(VerifiedPackage package) {using(var sha=SHA256.Create())return Hex(sha.ComputeHash(package.ManifestBytes));}
        public static void AssertApproval(VerifiedPackage package,string approvedHash)
        {
            if(approvedHash==null || !Regex.IsMatch(approvedHash,@"\A[0-9A-Fa-f]{64}\z") || !ManifestHash(package).Equals(approvedHash,StringComparison.OrdinalIgnoreCase))throw new Exception("Package differs from approved manifest.");
        }
        // Copy only an authenticated inventory and the EXACT authenticated metadata. Caller owns a protected empty destination.
        public static void CopyVerified(string source,string destination,VerifiedPackage package)
        {
            NoReparse(source);NoReparse(destination);
            if(Directory.GetFileSystemEntries(destination).Length!=0)throw new Exception("Stage must be empty.");
            foreach(var pair in package.Files) {var path=Path.Combine(source,pair.Key);NoReparse(path);File.Copy(path,Path.Combine(destination,pair.Key),false);}
            File.WriteAllBytes(Path.Combine(destination,ManifestName),package.ManifestBytes);
            File.WriteAllBytes(Path.Combine(destination,SignatureName),package.SignatureBytes);
        }
        public static void NoReparse(string path)
        {
            var full=Path.GetFullPath(path);
            for(string p=full;!String.IsNullOrEmpty(p);p=Path.GetDirectoryName(p)) if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new Exception("Reparse point in package/installation path.");
        }
        static byte[] ReadLimited(string path,int max) {using(var f=File.OpenRead(path)){if(f.Length==0 || f.Length>max)throw new Exception("Invalid metadata size.");var b=new byte[(int)f.Length];int n=0,r;while(n<b.Length && (r=f.Read(b,n,b.Length-n))>0)n+=r;if(n!=b.Length)throw new Exception("Incomplete metadata.");return b;}}
        static string Hex(byte[] bytes){return BitConverter.ToString(bytes).Replace("-","");}
        // Deliberately narrow JSON profile: objects and printable ASCII strings without escapes.
        // No duplicate keys (including case aliases), comments, BOM, nulls, arrays or unknown top-level fields.
        sealed class ManifestReader
        {
            readonly string text;int at;
            public ManifestReader(string value){text=value;}
            void Space(){while(at<text.Length && (text[at]==' '||text[at]=='\t'||text[at]=='\r'||text[at]=='\n'))at++;}
            void Expect(char c){Space();if(at>=text.Length || text[at++]!=c)throw new Exception("Invalid manifest JSON.");}
            string String(){Expect('"');int start=at;while(at<text.Length && text[at]!='"'){char c=text[at++];if(c<' '||c>'~'||c=='\\')throw new Exception("Unsupported manifest string.");}if(at>=text.Length)throw new Exception("Unterminated manifest string.");string s=text.Substring(start,at-start);at++;return s;}
            Dictionary<string,object> Object(int depth){if(depth>1)throw new Exception("Manifest nesting.");Expect('{');var result=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);Space();if(at<text.Length && text[at]=='}'){at++;return result;}while(true){var key=String();Expect(':');Space();object value=at<text.Length && text[at]=='{'?(object)Object(depth+1):String();if(result.ContainsKey(key))throw new Exception("Duplicate manifest key.");result.Add(key,value);Space();if(at<text.Length && text[at]=='}'){at++;return result;}Expect(',');}}
            public Dictionary<string,object> Read(){var result=Object(0);Space();if(at!=text.Length)throw new Exception("Trailing manifest JSON.");return result;}
        }
    }
}
