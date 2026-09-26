using System.IO;
namespace DriverPilot;
public static class FeatureTests
{
    public static string Run()
    {
        var now=DateTime.Now;var device=new Device("GPU","Display","1","Vendor","",0,"test");
        var oldEvent=new Finding("old","Display 4101","old event","Warnung","",LastSeen:now.AddMinutes(-1));
        var before=new Snapshot("Windows","CPU","Board","BIOS",32,[device],[oldEvent],[]);
        var after=before with{Devices=[device with{Version="2"}]};
        var rows=Stability.Compare(before,after,[],now);
        if(rows.Count!=1||rows[0].Evidence.Count!=0)throw new Exception("Existing event incorrectly attributed to update.");
        var fresh=after with{Findings=[oldEvent with{LastSeen=now.AddMinutes(2),Count=2}]};
        rows=Stability.Compare(after,fresh,rows,now.AddMinutes(3));
        if(rows.Count!=1||rows[0].Evidence.Count!=1)throw new Exception("Repeated new event not detected.");
        rows=Stability.Compare(fresh,fresh,rows,now.AddMinutes(4));
        if(rows[0].Evidence.Count!=1)throw new Exception("Duplicate observation evidence.");
        var expired=Stability.Compare(fresh,fresh with{Findings=[oldEvent with{LastSeen=now.AddHours(26)}]},rows,now.AddHours(26));
        if(expired[0].Evidence.Count!=1)throw new Exception("Expired observation changed.");
        var packageStatus="Package checks skipped (no manifest in build directory).";
        if(File.Exists(Path.Combine(AppContext.BaseDirectory,"package-manifest.json")))
        {
            var m=PackageVerifier.Verify(AppContext.BaseDirectory,false);
            void Reject(PackageManifest candidate,string expected,bool newer=false){
                try{PackageVerifier.Validate(AppContext.BaseDirectory,candidate,newer);}
                catch(Exception ex) when(ex.Message.Contains(expected)){return;}
                throw new Exception("Invalid package accepted: "+expected);
            }
            var badHash=new Dictionary<string,string>(m.Files);badHash[badHash.Keys.First()]=new string('0',64);
            Reject(m with{Files=badHash},"Prüfsumme");
            var traversal=new Dictionary<string,string>{{"../outside.dll",new string('0',64)}};foreach(var pair in m.Files)traversal[pair.Key]=pair.Value;
            Reject(m with{Files=traversal},"Paketpfad");
            Reject(m with{Files=[]},"gültiges");
            Reject(m with{Product="AnotherProduct"},"gültiges");
            Reject(m,"nicht neuer",true);
            packageStatus="Package: real hashes, tampering, traversal, empty manifest, wrong product and same-version rejection passed.";
        }
        return "Stability: version change, pre-existing events, repeated new events, deduplication, 24h expiry passed. "+packageStatus;
    }
}
