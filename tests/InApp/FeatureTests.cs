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
        var packageStatus="Authenticated package adversarial tests run separately via tests/PackageSecurity and tests/Test-Updater.ps1.";
        return "Stability: version change, pre-existing events, repeated new events, deduplication, 24h expiry passed. "+packageStatus;
    }
}
