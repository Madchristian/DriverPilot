using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
namespace DriverPilot;

public record Device(string Name, string Class, string Version, string Provider, string Date, int Code, string Id)
{ public string Status => Code == 0 ? "Bereit" : Code == 22 ? "Deaktiviert" : $"Problem · Code {Code}"; }
public record Finding(string Key, string Title, string Detail, string Severity, string Time, string DeviceRef="", int Count=1, DateTime? FirstSeen=null, DateTime? LastSeen=null, string Source="", int EventId=0, int Level=0);
/// <summary>Per-area scan completeness: complete, truncated, failed or unavailable.</summary>
public record CollectionStatus(string Hardware="failed", string Devices="failed", string SystemEvents="failed", string WlanEvents="failed");
public record Snapshot(string Windows, string Cpu, string Board, string Bios, double RamGB, List<Device> Devices, List<Finding> Findings, List<string> Warnings, HardwareProfile? Hardware=null, CollectionStatus? Collection=null);
public record UpdateRow(string Id, string Title, string Manufacturer, string Class, string Date, string Status);
public record HistoryRow(string Time, string Action, string Detail);
public class Preferences
{
    public bool Monitor {get;set;} = true;
    public bool Notify {get;set;} = true;
    public HashSet<string> Ignored {get;set;} = [];
    public HashSet<string> Notified {get;set;} = [];
    public HashSet<string> SeenEvents {get;set;} = [];
    public HashSet<string> ManufacturerNotified {get;set;} = [];
}
// Shared read-only boundary: invalid persisted snapshots are unavailable, never repaired into empty scans.
// Independent of Store initialization: explicit-path reads never create application state.
public static class PersistedData
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static T? Read<T>(string path)
    {
        try
        {
            var text=File.ReadAllText(path);
            return typeof(T)==typeof(Snapshot) ? (T)(object)SnapshotData.Parse(text) : JsonSerializer.Deserialize<T>(text,Json);
        }
        catch { return default; }
    }
}
public static class SnapshotData
{
    // Required constructor fields must actually occur in JSON (notably Code=0 must not be invented).
    // Optional metadata/defaulted Finding fields remain compatible with older snapshots.
    static readonly JsonSerializerOptions Json = new(PersistedData.Json) { RespectRequiredConstructorParameters = true };
    public static Snapshot Parse(string json)
    {
        var value=JsonSerializer.Deserialize<Snapshot>(json,Json);
        if(!IsValid(value))throw new JsonException("Gerätedaten fehlen oder sind unvollständig. Bitte erneut prüfen.");
        return value!;
    }
    public static bool IsValid(Snapshot? value)
    {
        if(value is not { Devices: not null, Findings: not null, Warnings: not null,
            Windows: not null, Cpu: not null, Board: not null, Bios: not null }
            || !double.IsFinite(value.RamGB) || value.RamGB<0)return false;
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var d in value.Devices)
            if(d is not { Name: not null, Class: not null, Version: not null, Provider: not null, Date: not null }
                || string.IsNullOrWhiteSpace(d.Id) || !ids.Add(d.Id))return false;
        foreach(var f in value.Findings)
            if(f is not { Title: not null, Detail: not null, Severity: not null, Time: not null, DeviceRef: not null, Source: not null }
                || string.IsNullOrWhiteSpace(f.Key))return false;
        return value.Warnings.All(w=>w!=null);
    }
    // The same read-only startup/comparison processing is used by the UI and isolated tests.
    public static string? DeviceName(Snapshot? value,string id) => value?.Devices.FirstOrDefault(d=>d.Id==id)?.Name;
    public static Dictionary<string,string> PreviousVersions(Snapshot? value) => value?.Devices.ToDictionary(d=>d.Id,d=>d.Version) ?? [];
}
public static class Store
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DriverPilot");
    public static readonly string Shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DriverPilot");
    public static readonly JsonSerializerOptions Json = PersistedData.Json;
    static Store() { Directory.CreateDirectory(Root); }
    public static T? Read<T>(string name) { try { return PersistedData.Read<T>(Path.Combine(Root,name)); } catch { return default; } }
    public static void Save<T>(string name,T data) { lock(Json) { var path=Path.Combine(Root,name); File.WriteAllText(path+".tmp",JsonSerializer.Serialize(data,Json)); File.Move(path+".tmp",path,true); } }
    public static void Log(string action,string detail)
    {
        lock(Json) { var rows=Read<List<HistoryRow>>("history.json")??[]; rows.Insert(0,new(DateTime.Now.ToString("dd.MM.yyyy HH:mm"),action,detail)); Save("history.json",rows.Take(500).ToList()); }
    }
}
public static class PowerShell
{
    public static async Task<string> Run(string resource,int timeoutSeconds=180)
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream($"DriverPilot.Scripts.{resource}.ps1") ?? throw new InvalidOperationException(resource);
        using var reader=new StreamReader(stream);
        var script=await reader.ReadToEndAsync();
        if(resource is "Maintenance" or "PolicyTests")
        { using var policy=Assembly.GetExecutingAssembly().GetManifestResourceStream("DriverPilot.Scripts.Policy.ps1")!;using var policyReader=new StreamReader(policy);script=await policyReader.ReadToEndAsync()+"\n"+script; }
        if(resource is "Maintenance" or "PolicyTests")
        { using var controls=Assembly.GetExecutingAssembly().GetManifestResourceStream("DriverPilot.Scripts.Controls.ps1")!;using var controlsReader=new StreamReader(controls);script=await controlsReader.ReadToEndAsync()+"\n"+script; }
        return await Script(script,timeoutSeconds);
    }
    public static async Task<string> Script(string script,int timeoutSeconds=180)
    {
        var psi=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe")) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
        psi.ArgumentList.Add("-NoProfile");psi.ArgumentList.Add("-NonInteractive");psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes("[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $ErrorActionPreference='Stop';\n"+script)));
        using var p=Process.Start(psi)??throw new Exception("PowerShell konnte nicht gestartet werden.");
        var output=p.StandardOutput.ReadToEndAsync(); var error=p.StandardError.ReadToEndAsync();
        using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { await p.WaitForExitAsync(cts.Token); } catch (OperationCanceledException) { p.Kill(true); throw new TimeoutException("Die Windows-Abfrage hat das Zeitlimit überschritten."); }
        var text=await output; var err=await error;
        if(p.ExitCode!=0) throw new Exception(err.Length>1600?err[..1600]:err);
        return text.Trim('\uFEFF','\r','\n',' ');
    }
}
public static class Rules
{
    public static bool ShouldNotify(Finding f,Preferences p) => f.Severity!="Info"&&!p.Ignored.Contains(f.Key)&&!p.Notified.Contains(f.Key);
}
