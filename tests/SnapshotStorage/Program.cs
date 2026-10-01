using System.Text.Json;
using System.Text.Json.Nodes;
using DriverPilot;

// References the actual application assembly, never App/MainWindow/Store initialization.
// All file inputs are synthetic and live under the caller's scratch directory.
var scratch = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "snapshot-storage-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    // Historical pre-fix evidence, not an expectation that the runtime keeps the bug.
    var legacy = JsonSerializer.Deserialize<Snapshot>("{}")!;
    try { _ = legacy.Devices.FirstOrDefault(d => d.Id == "CI-device"); throw new Exception("Historical repro changed"); }
    catch (ArgumentNullException) { Console.WriteLine("Historical RED: raw {} deserialization permits null Devices"); }
    var path = Path.Combine(scratch, "snapshot.json");
    var valid = new Snapshot("Windows 11", "CPU", "Board", "BIOS", 32,
        [new("GPU", "Display", "1", "Vendor", "", 0, "CI-device")],
        [new("event:test", "Title", "Detail", "Info", "12:00")], []);
    var json = JsonSerializer.Serialize(valid, PersistedData.Json);
    var failures = new List<string>();
    var passed = 0;
    void Test(string name, Action action)
    {
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch(Exception ex) { failures.Add(name + ": " + ex.Message); Console.WriteLine("FAIL " + failures[^1]); }
    }
    void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    Snapshot? Read(string text)
    {
        File.WriteAllText(path, text);
        var bytes = File.ReadAllBytes(path);
        var stamp = File.GetLastWriteTimeUtc(path);
        var value = PersistedData.Read<Snapshot>(path);
        Check(bytes.SequenceEqual(File.ReadAllBytes(path)) && stamp == File.GetLastWriteTimeUtc(path), "read modified source");
        return value;
    }
    void Reject(string name, string text) => Test(name, () => Check(Read(text) == null, "invalid snapshot accepted"));
    foreach(var text in new[]{"{}", "null", "{", "[]", "{\"Devices\":[]}", "{\"Devices\":[],\"Findings\":[],\"Warnings\":[]}"}) Reject("invalid JSON/partial: " + text, text);
    foreach(var name in new[]{"Devices", "Findings", "Warnings", "Windows", "Cpu", "Board", "Bios", "RamGB"})
    {
        var node = JsonNode.Parse(json)!.AsObject(); node.Remove(name); Reject("missing " + name, node.ToJsonString());
        node[name] = null; Reject("null " + name, node.ToJsonString());
    }
    foreach(var name in new[]{"Devices", "Findings", "Warnings"})
    {
        var node = JsonNode.Parse(json)!; node[name]!.AsArray().Add((JsonNode?)null); Reject("null member " + name, node.ToJsonString());
    }
    foreach(var name in new[]{"Name", "Class", "Version", "Provider", "Date", "Id"})
    {
        var node=JsonNode.Parse(json)!; node["Devices"]![0]![name]=null; Reject("null device " + name,node.ToJsonString());
    }
    foreach(var name in new[]{"Key", "Title", "Detail", "Severity", "Time", "DeviceRef", "Source"})
    {
        var node=JsonNode.Parse(json)!; node["Findings"]![0]![name]=null; Reject("null finding " + name,node.ToJsonString());
    }
    var duplicate=JsonNode.Parse(json)!; duplicate["Devices"]!.AsArray().Add(duplicate["Devices"]![0]!.DeepClone());
    var missingCode=JsonNode.Parse(json)!;missingCode["Devices"]![0]!.AsObject().Remove("Code");Reject("missing device code must not become healthy",missingCode.ToJsonString());
    var negativeRam=JsonNode.Parse(json)!;negativeRam["RamGB"]=-1;Reject("negative RAM",negativeRam.ToJsonString());
    Reject("duplicate device identity",duplicate.ToJsonString());
    foreach(var id in new[]{"", " "}) { var node=JsonNode.Parse(json)!;node["Devices"]![0]!["Id"]=id;Reject("blank device identity",node.ToJsonString()); }
    Test("missing file",()=>Check(PersistedData.Read<Snapshot>(Path.Combine(scratch,"absent.json"))==null,"missing accepted"));
    Test("normal roundtrip",()=>Check(JsonSerializer.Serialize(Read(json),PersistedData.Json)==json,"roundtrip changed"));
    Test("old snapshot without optional metadata",()=>{
        var node=JsonNode.Parse(json)!.AsObject();node.Remove("Hardware");node.Remove("Collection");
        foreach(var name in new[]{"DeviceRef","Count","FirstSeen","LastSeen","Source","EventId","Level"}) node["Findings"]![0]!.AsObject().Remove(name);
        Check(Read(node.ToJsonString()) is { Hardware:null,Collection:null },"legacy rejected");
    });
    Test("complete empty scan is valid",()=>Check(Read(JsonSerializer.Serialize(valid with{Devices=[],Findings=[]}))!=null,"empty arrays rejected"));
    Test("startup and fresh-scan processing",()=>{
        var previous=Read("{}");
        Check(SnapshotData.DeviceName(previous,"CI-device")==null,"missing baseline must retain ID fallback");
        Check(SnapshotData.PreviousVersions(previous).Count==0,"invalid previous scan leaked");
        Check(Stability.Compare(previous,valid,[],DateTime.Now).Count==0,"invalid baseline invented driver changes");
        Check(SnapshotData.DeviceName(Read(json),"CI-device")=="GPU","valid label lost");
        Check(SnapshotData.PreviousVersions(Read(json))["CI-device"]=="1","valid previous version lost");
        var next=valid with{Devices=[valid.Devices[0] with{Version="2"}]};
        Check(Stability.Compare(Read(json),next,[],DateTime.Now).Count==1,"valid previous scan comparison lost");
    });
    Test("live inventory rejects partial data",()=>{
        try { SnapshotData.Parse("{}"); throw new Exception("partial live scan accepted"); }
        catch(JsonException) { }
        Check(SnapshotData.Parse(json) is Snapshot,"valid live scan rejected");
    });
    if(args.Length>0) Test("actual lifecycle fixture",()=>{
        var value=PersistedData.Read<Snapshot>(Path.Combine(args[0],"snapshot.json"));Check(value!=null,"lifecycle fixture rejected");
        using var controls=JsonDocument.Parse(File.ReadAllText(Path.Combine(args[0],"controls.json")));
        Check(controls.RootElement.GetProperty("BlockedDevices")[0].GetString()=="CI-device","blocked-device regression removed");
        Check(SnapshotData.DeviceName(value,"CI-device")==null,"fixture must use ID fallback");
        Check(SnapshotData.PreviousVersions(value).Count==0,"fixture must keep an empty previous-device map");
    });
    Console.WriteLine($"SnapshotStorage: {passed} passed, {failures.Count} failed");
    if(failures.Count>0) throw new Exception(string.Join(Environment.NewLine,failures));
}
finally { Directory.Delete(scratch, true); }
