using System.Net.WebSockets;
using System.Text.Json.Nodes;

using AIF.Controller;
using Mpai.Core;
using Mpai.Mmm;
using Mpai.Mmm.Client;

// A STAND-IN FOR A HUMAN PASSENGER (2026-11-28, M3329). An H-User - as a human's Thalia would be - that registers, walks its Persona to a taxi, finds the CAV Process that drives it (the
// Process the Taxi Service gave the Right to move the taxi) and says what a human would, through the Process Actions the viewer uses for a microphone: UM-Capture of the speech, Identify,
// MM-Send of the Speech Object to that Process, and the speech's data over its stream. What the taxi answers - Messages "Taxi-<User>-<n>" with its words and its speech - is printed, and
// its speech saved as WAV files in Downloads\MMM taxi dialogue.
//
//   dotnet run --project Test\MmmPassenger -- --mmm http://127.0.0.1:7198 --who Anna --taxi Taxi-1 "Please take me to the Roundabout." "Yes, let's go."
static string Arg(string[] args, string name, string fallback) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
var root = Root();
var mmm = new Uri(Arg(args, "--mmm", "http://127.0.0.1:7198"));
var who = Arg(args, "--who", "Anna");
var taxi = Arg(args, "--taxi", "Taxi-1");
var lines = args.Where((a, i) => !a.StartsWith("--", StringComparison.Ordinal) && (i == 0 || !args[i - 1].StartsWith("--", StringComparison.Ordinal))).ToList();
if (lines.Count == 0) lines = ["Please take me to the Roundabout.", "Yes, let's go."];
var outDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "MMM taxi dialogue");
Directory.CreateDirectory(outDir);

using var http = new HttpClient { BaseAddress = mmm };
var profile = new JsonObject
{
    ["Header"] = "MMM-PPR-V2.2", ["MInstanceID"] = "MI1", ["humanID"] = $"{who}-human", ["PersonalProfileID"] = $"Profile-{who}-human",
    ["PersonalProfile"] = new JsonObject
    {
        ["Credential"] = Arg(args, "--credential", "secret1"), ["AccountLevel"] = "Participant",
        ["Data"] = new JsonArray(new JsonObject { ["FirstName"] = who, ["LastName"] = "Passenger", ["Organisation"] = "Test", ["Email"] = $"{who.ToLowerInvariant()}@example.com" }),
        ["Processes"] = new JsonArray(new JsonObject { ["ProcessIDOrProcess"] = new JsonArray(new JsonObject { ["ProcessID"] = who }), ["PersonaIDOrPersona"] = new JsonArray(new JsonObject { ["PersonaID"] = $"{who}-Persona", ["PersonaChoice"] = Arg(args, "--persona", "Female_Adult_05") }) })
    }
};
var reg = await new MmmClient(http, $"{who}-human").PerformAsync("Register", new Complements { With = [profile] });
if (!reg.Tokens.TryGetValue(who, out var token)) { Console.WriteLine($"Register: {reg.Http} {reg.Response.PaStatus?.Detail}"); return 1; }
var user = new MmmClient(http, who, token);

// the Persona, next to the taxi
var t = (await user.ReadAsync(taxi)).Item;
var at = t?.ToJsonString() ?? "";
var add = await user.PerformAsync("MM-Add", Uc2.C($"{who}-Persona", at: "MVS", with: Uc2.SpatialAttitude($"SA-{who}", -18, -24)));
Console.WriteLine($"{who}: Persona at the taxi rank ({add.Http}).");

// the CAV that drives the taxi: the Process holding the Right to move it
using var rq = new HttpRequestMessage(HttpMethod.Get, $"/api/v2.2/items/{taxi}/rights"); rq.Headers.Authorization = new("Bearer", token);
var holders = JsonNode.Parse(await (await http.SendAsync(rq)).Content.ReadAsStringAsync()) as JsonArray ?? [];
var driver = holders.OfType<JsonObject>().FirstOrDefault(h => (string?)h["processID"] != Taxis.Service && h["rights"]!.ToJsonString().Contains($"Taxi-Hire-{taxi}-"))?["processID"]?.GetValue<string>();
if (driver is null) { Console.WriteLine($"{taxi} has no driver: no CAV has hired it."); return 1; }
Console.WriteLine($"{taxi} is driven by {driver}.");

var heard = 0; var n = 0;
foreach (var line in lines)
{
    var pcm = Speak(root, line);
    var id = $"Speech-{who}-{++n}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    var q = new JsonObject
    {
        ["Header"] = "TFA-SPQ-V1.5", ["MInstanceID"] = "MI1", ["SpeechQualifierID"] = id + "-Q", ["SubTypes"] = new JsonObject(),
        ["Formats"] = new JsonObject { ["ContentFormats"] = new JsonObject { ["RawData"] = new JsonObject { ["Header"] = "TFA-PCM-V1.5", ["PCM"] = new JsonObject { ["SamplingFrequency"] = 16000, ["SamplePrecision"] = 16 } } }, ["TransportFormats"] = new JsonObject { ["StreamFormat"] = "WebSocket" } },
        ["Attributes"] = new JsonObject { ["Source"] = "Real", ["Device"] = new JsonObject { ["DeviceRole"] = "Capture", ["DeviceType"] = "Microphone", ["CaptureConfiguration"] = new JsonObject { ["ChannelCount"] = 1, ["SamplingMode"] = "Mono" } } }
    };
    var c = await user.PerformAsync("UM-Capture", Uc2.C(id, at: $"ULoc-{who}", with: q));
    var i2 = await user.PerformAsync("Identify", Uc2.C(id, with: q.DeepClone()));
    var s = await user.PerformAsync("MM-Send", Uc2.C($"Say-{id}", to: driver, with: new JsonObject { ["Header"] = "OSD-BSO-V1.5", ["MInstanceID"] = "MI1", ["BasicSpeechObjectID"] = id }));
    Console.WriteLine($"{who} says \"{line}\" (UM-Capture {c.Http}, Identify {i2.Http}, MM-Send {s.Http})");
    using (var ws = new ClientWebSocket())
    {
        await ws.ConnectAsync(new UriBuilder(mmm) { Scheme = "ws", Path = $"/ws/v2.2/speech/{id}", Query = "token=" + Uri.EscapeDataString(token) }.Uri, default);
        for (var k = 0; k < pcm.Length; k += 2048) await ws.SendAsync(pcm.AsMemory(k, Math.Min(2048, pcm.Length - k)), WebSocketMessageType.Binary, true, default);
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "said", default);
    }
    // what the taxi answers, until it has said something after this line
    var waited = DateTime.UtcNow; var got = false;
    while (DateTime.UtcNow - waited < TimeSpan.FromMinutes(3))
    {
        var (h, item) = await user.ReadAsync($"Taxi-{who}-{heard + 1}");
        if (h == 200 && item is not null)
        {
            heard++; got = true;
            var data = item["ItemValueOrRef"]?[0]?["MessageData"]?[0]?["MessageData"]?.GetValue<string>();
            var reply = data is null ? null : JsonNode.Parse(data);
            var say = (string?)reply?["Say"]; var speech = (string?)reply?["Speech"];
            Console.WriteLine($"  {taxi} says: \"{say}\"{(speech is null ? " (no speech)" : $" (speech {Convert.FromBase64String(speech).Length / 32000.0:0.0} s)")}");
            if (speech is not null) File.WriteAllBytes(Path.Combine(outDir, $"Taxi-{who}-{heard}.wav"), Wav(Convert.FromBase64String(speech)));
            continue;
        }
        if (got) break;
        await Task.Delay(500);
    }
    if (!got) Console.WriteLine($"  {taxi} said nothing within 3 minutes.");
    await Task.Delay(1500);
}
return 0;

static string Root()
{
    for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar)))
        if (Directory.Exists(Path.Combine(dir, "AIMs", "AMDs")) && Directory.Exists(Path.Combine(dir, "schemas"))) return dir;
    throw new DirectoryNotFoundException("the repository is not above this program");
}

// What a human would say, in a Piper voice, as 16 kHz, 16-bit mono PCM - as a microphone gives it.
static byte[] Speak(string root, string text)
{
    var all = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "AIMs", "aim-settings.json")))!;
    var plain = (all["1MMC-TTS-V2.5-I01"] as JsonObject ?? new JsonObject()).ToDictionary(p => p.Key, p => (string)p.Value!);
    var settings = new Dictionary<string, string>(ModelFiles.SettingsOf(root, "1MMC-TTS-V2.5-I01", plain));
    settings["VoiceModel"] = settings["Voice:en-US"]; settings["VoiceConfig"] = settings["VoiceConfig:en-US"];
    foreach (var k in settings.Keys.Where(k => k.StartsWith("Voice:") || k.StartsWith("VoiceConfig:")).ToList()) settings.Remove(k);
    var wav = Mpai.Aims.Tts.TtsFactory.Create(settings).ProcessAsync(BasicTextObject.FromText(text)).GetAwaiter().GetResult().Data;
    var rate = BitConverter.ToInt32(wav, 24); byte[] pcm = [];
    for (var pos = 12; pos + 8 <= wav.Length;)
    {
        var id = System.Text.Encoding.ASCII.GetString(wav, pos, 4); var length = BitConverter.ToInt32(wav, pos + 4);
        if (id == "data") { pcm = wav[(pos + 8)..Math.Min(wav.Length, pos + 8 + length)]; break; }
        pos += 8 + length + (length & 1);
    }
    var n = pcm.Length / 2; var m = (int)((long)n * 16000 / rate); var o = new byte[m * 2];
    for (var i = 0; i < m; i++) { var x = (double)i * rate / 16000; var k = (int)x; var f = x - k; var v = k + 1 < n ? BitConverter.ToInt16(pcm, k * 2) * (1 - f) + BitConverter.ToInt16(pcm, (k + 1) * 2) * f : BitConverter.ToInt16(pcm, Math.Min(k, n - 1) * 2); BitConverter.TryWriteBytes(o.AsSpan(i * 2), (short)Math.Round(v)); }
    return o;
}

static byte[] Wav(byte[] pcm)
{
    using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
    w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
    w.Write("data"u8); w.Write(pcm.Length); w.Write(pcm); w.Flush(); return ms.ToArray();
}
