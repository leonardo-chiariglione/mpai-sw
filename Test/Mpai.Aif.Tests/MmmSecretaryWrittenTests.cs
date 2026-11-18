using System.Diagnostics;
using System.Text.Json.Nodes;
using Mpai.Mmm;
using Mpai.Mmm.Client;
using Mpai.Mmm.Server;

namespace Mpai.Aif.Tests;

// THE SECRETARY, WRITTEN TO (the author, 2026-11-18): the meeting runs with no speech recognition (no Whisper) and no language model (no Ollama, no summary). A participant writes its request
// (a Message ask:<words> to the Secretary); the requests of the list are carried out as they are written, anything else is answered with "only the requests of the list". The page is told by
// its seat (;speech=0) that it writes, and what the participant says still goes to the room.
[Trait("Group", "Fast")]
[Collection("MmmMeeting")]
[Trait("Blocks", "Yes")]
public class MmmSecretaryWrittenTests
{
    private static JsonObject Profile(string name) => new()
    {
        ["Header"] = "MMM-PPR-V2.2", ["MInstanceID"] = "MI1", ["humanID"] = $"{name}-human", ["PersonalProfileID"] = $"Profile-{name}-human",
        ["PersonalProfile"] = new JsonObject
        {
            ["Data"] = new JsonArray(new JsonObject { ["FirstName"] = name }),
            ["Processes"] = new JsonArray(new JsonObject
            {
                ["ProcessIDOrProcess"] = new JsonArray(new JsonObject { ["ProcessID"] = name }),
                ["PersonaIDOrPersona"] = new JsonArray(new JsonObject { ["PersonaID"] = $"{name}-Persona" })
            })
        }
    };

    private static async Task<MmmClient> Register(HttpClient http, string name)
    {
        var reg = await new MmmClient(http, $"{name}-human").PerformAsync("Register", new Complements { With = [Profile(name)] });
        var user = new MmmClient(http, name, reg.Tokens[name]);
        Assert.Equal(200, (await user.PerformAsync("MM-Add", Uc2.C($"{name}-Persona", at: "MVS", with: Uc2.SpatialAttitude($"SA-{name}", -2, -2)))).Http);
        return user;
    }

    private static async Task<Dictionary<string, string>> Sit(MeetingServices services, MmmClient user)
    {
        var persona = $"{user.ProcessID}-Persona";
        var ask = $"Join-{user.ProcessID}";
        await user.PerformAsync("MM-Send", Uc2.C(ask, to: Meeting.Avs, with: Uc2.Message(ask, Meeting.JoinText(persona))));
        await services.PumpAsync();
        var seat = Meeting.ParseSeat(Meeting.TextOf((await user.ReadAsync($"Seat-{ask}")).Item)!);
        var move = await user.PerformAsync("MM-Move", Uc2.C(persona, from: "MVS", to: seat["seat"],
            with: [MeetingServices.SeatedAt($"SA-{persona}-Seat", 0), Meeting.SitPA(persona, seat["seat"]), Uc2.Rights($"Seat-Move-{persona}", "May", "Acquired", Meeting.SitID(persona))]));
        Assert.Equal(200, move.Http);
        return seat;
    }

    [Fact]
    public async Task EveryRequestOfTheListWrittenIsCarriedOutWithoutWhisperOrALanguageModel()
    {
        var m = new MInstance(Repository.Schemas);
        Uc2.Setup(m);
        Meeting.Setup(m);
        await using var app = MmmServer.Build(m, "http://127.0.0.1:0");
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()), Timeout = Timeout.InfiniteTimeSpan };
        var services = new MeetingServices(http, m);          // no recogniser, no Interpret, no Summarise, no Synthesise: what --no-asr --no-summary gives

        var anna = await Register(http, "Anna");
        var bruno = await Register(http, "Bruno");
        await services.StartAsync(); await services.PumpAsync(); await services.PumpAsync();
        foreach (var (id, text) in new[] { ("Convene-Anna", "convene"), ("Invite-Bruno", "invite;user=Bruno") })
            await anna.PerformAsync("MM-Send", Uc2.C(id, to: Meeting.Avs, with: Uc2.Message(id, text)));
        await services.PumpAsync();
        var annaSeat = await Sit(services, anna);
        await Sit(services, bruno);
        await services.PumpAsync();
        Assert.Equal("0", annaSeat.GetValueOrDefault("speech"));                                       // the page is told to write

        var n = new Dictionary<string, int>();
        var slowest = 0L;
        async Task<JsonObject> Write(MmmClient who, string words)
        {
            var k = n[who.ProcessID] = n.GetValueOrDefault(who.ProcessID) + 1;
            var id = $"Ask-{who.ProcessID}-{k}";
            var clock = Stopwatch.StartNew();
            Assert.Equal(200, (await who.PerformAsync("MM-Send", Uc2.C(id, to: Meeting.Secretary, with: Uc2.Message(id, "ask:" + words)))).Http);
            var reply = $"Secretary-{who.ProcessID}-{k}";
            for (var i = 0; i < 50 && (await who.ReadAsync(reply)).Http != 200; i++) { await services.PumpAsync(); await services.PumpDialogueAsync(); }
            slowest = Math.Max(slowest, clock.ElapsedMilliseconds);
            var told = (await who.ReadAsync(reply)).Item;
            Assert.True(told is not null, $"no answer to \"{words}\"");
            return (JsonObject)JsonNode.Parse(Meeting.TextOf(told)!)!;
        }

        var r = await Write(anna, "Raise my hand.");
        Assert.Equal("OK. Your hand is up.", (string?)r["Say"]);
        Assert.Contains("Anna", services.HandsUp);
        Assert.NotEqual(200, (await bruno.ReadAsync("Secretary-Anna-1")).Http);                        // the answer is Anna's only

        r = await Write(anna, "please lower my hand");
        Assert.Equal("OK. Your hand is down.", (string?)r["Say"]);
        Assert.DoesNotContain("Anna", services.HandsUp);

        r = await Write(anna, "mute me");
        Assert.Equal("Off", (string?)r["Do"]!["Microphone"]);
        r = await Write(anna, "unmute me");
        Assert.Equal("On", (string?)r["Do"]!["Microphone"]);

        r = await Write(anna, "share my screen");
        Assert.Equal("Share", (string?)r["Do"]!["Screen"]);
        r = await Write(anna, "Stop sharing");
        Assert.Equal("Stop", (string?)r["Do"]!["Screen"]);

        r = await Write(anna, "summarise the meeting");                                                 // the summary is for later: said so, not promised
        Assert.Equal("OK. There is no summary in this meeting.", (string?)r["Say"]);

        r = await Write(anna, "answer out loud");                                                       // no voice in this meeting: written all the same
        Assert.Null((string?)r["Speech"]);
        r = await Write(anna, "answer in writing");
        Assert.StartsWith("OK.", (string?)r["Say"]);

        r = await Write(anna, "could you open the window");                                             // not of the list: no OK
        Assert.StartsWith("Sorry, I understand only the requests of the list", (string?)r["Say"]);

        r = await Write(bruno, "raise my hand");                                                        // another participant, its own answers
        Assert.Equal("OK. Your hand is up.", (string?)r["Say"]);
        Assert.Contains("Bruno", services.HandsUp);
        r = await Write(bruno, "close the meeting");
        Assert.Contains("Only the convener", (string?)r["Say"]);
        Assert.False(services.Ended);

        r = await Write(anna, "thank you");
        Assert.Equal("meeting", (string?)r["Do"]!["Mode"]);

        r = await Write(anna, "close the meeting");                                                     // the convener: asked to be sure
        Assert.Contains("close the meeting for everybody", (string?)r["Say"]);
        Assert.False(services.Ended);
        r = await Write(anna, "yes");
        Assert.True(services.Ended);

        Assert.True(slowest < 3000, $"the slowest answer took {slowest} ms");
    }
}
