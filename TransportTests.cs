using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
namespace DriverPilot;
internal static class TransportTests
{
    const string Sample="Grüße ÄÖÜ äöü ß – „Treiber prüfen“ 🛠";
    static readonly string Complete=Sample+"\n"+new string('x',8000)+"\nDiese Bewertung basiert ausschließlich auf lokalen Messdaten. ENDE.";
    public static async Task EmitFixture()
    {
        using var input=new StreamReader(Console.OpenStandardInput(),new UTF8Encoding(false,true));
        if(await input.ReadLineAsync()!=Sample)throw new Exception("UTF-8 stdin mismatch / BOM.");
        using var output=Console.OpenStandardOutput();
        var options=new JsonSerializerOptions{Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
        object[] events=[
            new{method="item/agentMessage/delta",@params=new{itemId="message1",delta=Sample}},
            new{method="item/completed",@params=new{item=new{type="agentMessage",id="message1",text=Complete}}},
            new{method="item/agentMessage/delta",@params=new{itemId="message1",delta="MUST NOT APPEAR"}},
            new{method="item/completed",@params=new{item=new{type="agentMessage",id="message2",text="Zweite Nachricht vollständig."}}}
        ];
        foreach(var item in events)
        {
            var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(item,options)+"\n");
            // Trennt auch Mehrbytezeichen mitten in ihrer UTF-8-Sequenz.
            for(int i=0;i<bytes.Length;i+=3)await output.WriteAsync(bytes.AsMemory(i,Math.Min(3,bytes.Length-i)));
            await output.FlushAsync();
        }
        using var error=Console.OpenStandardError();await error.WriteAsync(Encoding.UTF8.GetBytes(Sample));
    }
    public static async Task<string> Run()
    {
        var info=CodexClient.CreatePipeStartInfo(Environment.ProcessPath!);info.ArgumentList.Add("--pipe-fixture");
        using var child=Process.Start(info)??throw new Exception("Test transport did not start.");
        var error=child.StandardError.ReadToEndAsync();
        await child.StandardInput.WriteLineAsync(Sample);await child.StandardInput.FlushAsync();child.StandardInput.Close();
        var reply=new AgentReply();int count=0;
        while(await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)) is {} line)
        {
            using var doc=JsonDocument.Parse(line);reply.Apply(doc.RootElement.GetProperty("method").GetString(),doc.RootElement.GetProperty("params"));
            if(++count==1&&reply.Text!=Sample)throw new Exception("UTF-8 stdout was corrupted.");
        }
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        if(child.ExitCode!=0)throw new Exception("Transport child failed.");
        if(await error!=Sample)throw new Exception("UTF-8 stderr was corrupted.");
        if(reply.Text!=Complete+"\n\nZweite Nachricht vollständig.")throw new Exception("Final answer incomplete, duplicated or reordered.");
        reply.Clear();if(reply.Text!="")throw new Exception("Previous turn leaked into next response.");
        return "PASS: UTF-8 stdin/stdout/stderr, split multibyte characters, umlauts, ß, quotes and emoji.\nPASS: 8 KB final reply replaces incomplete stream; late delta ignored; separate messages preserved; next turn cleared.\nNo Codex, Git, network request or driver action was started.";
    }
}
