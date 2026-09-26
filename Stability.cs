using System.IO;
namespace DriverPilot;

public class MaintenanceControls
{
    public int[] Days {get;set;}=[0];
    public int StartHour {get;set;}=12;
    public int EndHour {get;set;}=17;
    public int ObservationDays {get;set;}=14;
    public string PauseUntil {get;set;}="";
    public List<string> BlockedDevices {get;set;}=[];
    public List<string> BlockedUpdates {get;set;}=[];
    public List<string> BusyProcesses {get;set;}=[];
    public int NetworkLimitMB {get;set;}=2;
    public static MaintenanceControls Load()=>Store.Read<MaintenanceControls>("controls.json")??new();
    public void Save()=>Store.Save("controls.json",this);
}
public record Observation(string Device,string Id,string Before,string After,DateTime DetectedAt,List<string> Baseline,List<string> Evidence,DateTime? LastChecked=null)
{
    public string Status=>DateTime.Now<DetectedAt.AddHours(24)?"24-Stunden-Beobachtung läuft":"Beobachtungszeit abgelaufen";
    public string Detail=>$"{Before} → {After}\nErkannt: {DetectedAt:dd.MM.yyyy HH:mm} · zuletzt geprüft: {LastChecked:dd.MM.yyyy HH:mm}\n"+(Evidence.Count==0?"Bisher keine zusätzlichen Befunde in den erfolgten Prüfungen.":string.Join("\n",Evidence))+"\nZeitlicher Zusammenhang, kein Nachweis einer Treiberursache. Überwachung nur während DriverPilot läuft.";
}
public static class Stability
{
    public static List<Observation> Compare(Snapshot? before,Snapshot after,List<Observation> observations,DateTime now)
    {
        if(before!=null){var old=before.Devices.GroupBy(d=>d.Id).ToDictionary(g=>g.Key,g=>g.First());
            foreach(var d in after.Devices)
                if(old.TryGetValue(d.Id,out var previous)&&previous.Version!=d.Version&&!string.IsNullOrEmpty(previous.Version)&&!string.IsNullOrEmpty(d.Version))
                    observations.Insert(0,new(d.Name,d.Id,previous.Version,d.Version,now,before.Findings.Select(f=>f.Key).ToList(),[]));
        }
        for(var i=0;i<observations.Count;i++){
            var o=observations[i];if(now>o.DetectedAt.AddHours(24))continue;
            // Include increases in an existing event group, but never old events from before detection.
            var evidence=after.Findings.Where(f=>f.LastSeen is {} last?last>o.DetectedAt:!o.Baseline.Contains(f.Key))
                .Where(f=>f.Severity!="Info"||f.Key.StartsWith("event:"))
                .Select(f=>$"{f.Time} · {f.Title} · {f.Severity} · {f.Count} Meldung(en)");
            observations[i]=o with{Evidence=o.Evidence.Concat(evidence).Distinct().TakeLast(80).ToList(),LastChecked=now};
        }
        return observations.Take(100).ToList();
    }
}
public record BackupRow(string Name,string Path,int Packages)
{
    public string Detail=>$"{Name} · {Packages} INF-Dateien";
    public static List<BackupRow> Load(){var root=System.IO.Path.Combine(Store.Shared,"Backups");return Directory.Exists(root)?Directory.EnumerateDirectories(root).OrderDescending().Select(p=>new BackupRow(System.IO.Path.GetFileName(p),p,Directory.EnumerateFiles(p,"*.inf",SearchOption.AllDirectories).Count())).ToList():[];}
}
public record AiAction(DateTime Time,string Tool,string Status,string Result);
