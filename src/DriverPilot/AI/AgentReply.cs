using System.Text.Json;
namespace DriverPilot;

// Der endgültige Item-Text ersetzt die Vorschau aus Streaming-Fragmenten.
// So bleiben Nachrichten vollständig, auch wenn nicht jedes Delta geliefert wird.
internal sealed class AgentReply
{
    readonly Dictionary<string,string> messages=new();
    readonly List<string> order=[];
    readonly HashSet<string> completed=[];
    public string Text=>string.Join("\n\n",order.Select(id=>messages[id]));
    public void Clear(){messages.Clear();order.Clear();completed.Clear();}
    public bool Apply(string? method,JsonElement args)
    {
        if(method=="item/agentMessage/delta")
        {
            var id=args.GetProperty("itemId").GetString()!;
            if(completed.Contains(id))return false;
            Ensure(id);messages[id]+=args.GetProperty("delta").GetString();return true;
        }
        if(method=="item/completed"&&args.TryGetProperty("item",out var item))return Complete(item);
        if(method=="turn/completed"&&args.GetProperty("turn").TryGetProperty("items",out var items)&&items.ValueKind==JsonValueKind.Array)
        {
            bool changed=false;foreach(var entry in items.EnumerateArray())changed=Complete(entry)||changed;return changed;
        }
        return false;
    }
    bool Complete(JsonElement item)
    {
        if(!item.TryGetProperty("type",out var type)||type.GetString()!="agentMessage")return false;
        var id=item.GetProperty("id").GetString()!;Ensure(id);messages[id]=item.GetProperty("text").GetString()??"";completed.Add(id);return true;
    }
    void Ensure(string id){if(messages.TryAdd(id,""))order.Add(id);}
}
