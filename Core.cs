using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
namespace DriverPilot;

public record Device(string Name, string Class, string Version, string Provider, string Date, int Code, string Id)
{ public string Status => Code == 0 ? "Bereit" : Code == 22 ? "Deaktiviert" : $"Problem · Code {Code}"; }
public record Finding(string Key, string Title, string Detail, string Severity, string Time, string DeviceRef="", int Count=1, DateTime? FirstSeen=null, DateTime? LastSeen=null);
public record Snapshot(string Windows, string Cpu, string Board, string Bios, double RamGB, List<Device> Devices, List<Finding> Findings, List<string> Warnings);
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
public static class Store
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DriverPilot");
    public static readonly string Shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DriverPilot");
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    static Store() { Directory.CreateDirectory(Root); }
    public static T? Read<T>(string name) { try { return JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(Root,name)),Json); } catch { return default; } }
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
