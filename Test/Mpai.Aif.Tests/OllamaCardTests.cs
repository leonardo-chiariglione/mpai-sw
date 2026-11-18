using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Mpai.Mmc.Edp;

namespace Mpai.Aif.Tests;

// THE CARD IS SHARED, AND LLAMA 3.2 HAS ONE NAME (2026-11-18). On the 4 GB card a model that another App had loaded, and that Ollama kept for as long as it ran, stopped the
// model of the dialogue from loading: "Talk to your avatar" waited more than a minute. The two were the same Llama 3.2 3B under two names ("llama3.2:3b" and the model created
// from Access, mpai-014912763a85), and Ollama keeps a copy for each name. Now whoever asks for "llama3.2:3b" is given the model from Access when Ollama has it; a model is kept
// 30 minutes after its last use, not for ever; and a model that is not loaded is loaded beside the others when it fits, else they are unloaded first. Here Ollama is a stand-in
// that records what it is asked: the real one is not touched.
[Trait("Group", "Fast")]
public class OllamaCardTests
{
    private const string FromAccess = "mpai-014912763a85";

    private sealed class FakeOllama : IDisposable
    {
        private readonly HttpListener listener = new();
        public readonly List<(string Path, JsonObject? Body)> Asked = [];
        public (string Name, long Vram)[] Loaded = [];
        public (string Name, long Size)[] Models = [];
        public string Url { get; }

        public FakeOllama()
        {
            var free = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); free.Start();
            var port = ((IPEndPoint)free.LocalEndpoint).Port; free.Stop();          // a port the system says is free
            Url = $"http://127.0.0.1:{port}";
            listener.Prefixes.Add(Url + "/");
            listener.Start();
            _ = Task.Run(Serve);
        }

        private async Task Serve()
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                var text = await new StreamReader(ctx.Request.InputStream).ReadToEndAsync();
                var body = text.Length > 0 ? JsonNode.Parse(text) as JsonObject : null;
                lock (Asked) Asked.Add((ctx.Request.Url!.AbsolutePath, body));
                var status = 200;
                var answer = ctx.Request.Url.AbsolutePath switch
                {
                    "/api/ps" => new JsonObject { ["models"] = new JsonArray(Loaded.Select(m => (JsonNode)new JsonObject { ["name"] = m.Name, ["size_vram"] = m.Vram }).ToArray()) }.ToJsonString(),
                    "/api/tags" => new JsonObject { ["models"] = new JsonArray(Models.Select(m => (JsonNode)new JsonObject { ["name"] = m.Name, ["size"] = m.Size }).ToArray()) }.ToJsonString(),
                    "/api/show" => Models.Any(m => m.Name == (string?)body?["model"] || m.Name == (string?)body?["model"] + ":latest") ? "{}" : Fail(out status),
                    "/api/chat" => """{"message":{"role":"assistant","content":"Hello."}}""",
                    _ => "{}"
                };
                var bytes = Encoding.UTF8.GetBytes(answer);
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        }

        private static string Fail(out int status) { status = 404; return """{"error":"not found"}"""; }

        public JsonObject Chat => Asked.Single(a => a.Path == "/api/chat").Body!;
        public void Dispose() { listener.Stop(); listener.Close(); }
    }

    private static bool CardKnown()
    {
        try { using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("nvidia-smi", "-L") { RedirectStandardOutput = true, UseShellExecute = false }); p!.WaitForExit(5000); return p.ExitCode == 0; }
        catch { return false; }
    }

    [Fact]
    public async Task Llama32HasOneNameTheModelFromAccess()
    {
        using var ollama = new FakeOllama { Models = [(FromAccess + ":latest", 2_000_000_000), ("llama3.2:3b", 2_000_000_000)] };
        using var llm = new OllamaClient("llama3.2:3b", ollama.Url);
        await llm.ChatAsync("system", "user");
        Assert.Equal(FromAccess, (string?)ollama.Chat["model"]);
        Assert.Equal(FromAccess, OllamaClient.OneNameOf("llama3.2:3b"));
        Assert.Null(OllamaClient.OneNameOf("llama3.1"));
    }

    [Fact]
    public async Task WithoutTheModelFromAccessTheNameIsKept()
    {
        using var ollama = new FakeOllama { Models = [("llama3.2:3b", 2_000_000_000)] };       // a machine where no App has created it yet
        using var llm = new OllamaClient("llama3.2:3b", ollama.Url);
        await llm.ChatAsync("system", "user");
        Assert.Equal("llama3.2:3b", (string?)ollama.Chat["model"]);
    }

    [Fact]
    public async Task AModelThatDoesNotFitUnloadsTheOthersFirst()
    {
        using var ollama = new FakeOllama { Loaded = [(FromAccess + ":latest", 2_400_000_000)], Models = [("llama3.1:latest", 4_900_000_000)] };   // more than any card of this size holds beside it
        using var llm = new OllamaClient("llama3.1", ollama.Url);
        Assert.Equal("Hello.", await llm.ChatAsync("system", "user"));

        var unload = ollama.Asked.Single(a => a.Path == "/api/generate");
        Assert.Equal(FromAccess + ":latest", (string?)unload.Body!["model"]);
        Assert.Equal(0, (int)unload.Body["keep_alive"]!);
        Assert.Equal(1800, (int)ollama.Chat["keep_alive"]!);                    // 30 minutes, not for ever (-1)
        Assert.True(ollama.Asked.FindIndex(a => a.Path == "/api/generate") < ollama.Asked.FindIndex(a => a.Path == "/api/chat"));
    }

    [SkippableFact]
    public async Task AModelThatFitsIsLoadedBesideTheOthers()
    {
        Skip.IfNot(CardKnown(), "No NVIDIA card here: a card that cannot be measured is taken as full.");
        using var ollama = new FakeOllama { Loaded = [("small:latest", 100_000_000)], Models = [("tiny:latest", 100_000_000)] };
        using var llm = new OllamaClient("tiny", ollama.Url);
        await llm.ChatAsync("system", "user");
        Assert.DoesNotContain(ollama.Asked, a => a.Path == "/api/generate");     // 0.1 GB beside 0.1 GB: nothing is unloaded
    }

    [Fact]
    public async Task ItsOwnModelLoadedNothingIsUnloaded()
    {
        using var ollama = new FakeOllama { Loaded = [(FromAccess + ":latest", 2_400_000_000), ("llama3.1:latest", 1_000_000_000)], Models = [(FromAccess + ":latest", 2_000_000_000)] };
        using var llm = new OllamaClient(FromAccess, ollama.Url);
        await llm.ChatAsync("system", "user");
        Assert.DoesNotContain(ollama.Asked, a => a.Path == "/api/generate");
    }
}
