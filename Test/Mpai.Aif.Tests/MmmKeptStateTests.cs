using System.Text.Json.Nodes;
using Mpai.Mmm;
using Mpai.Mmm.Client;
using Microsoft.AspNetCore.Builder;
using Mpai.Mmm.Server;

namespace Mpai.Aif.Tests;

// THE WORLD GOES ON FROM ITS LAST RUN (2026-11-26): an M-Instance of the MMM world closes and starts again from the state it had - the Users that registered, where their Personae
// are, their Rights - and not from what was momentary (a speech on its way). The User comes back with a new token and acts as before.
[Trait("Group", "Fast")]
[Collection("MmmMeeting")]
public class MmmKeptStateTests
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

    private static async Task<(MInstance M, WebApplication App, HttpClient Http)> WorldAsync(JsonObject? state = null)
    {
        var m = new MInstance(Repository.Schemas);
        World.Setup(m, Repository.Root);
        m.DefaultModels = ["Persona1.glb", "Persona2.glb"];
        if (state is not null) m.RestoreState(state);
        var app = MmmServer.Build(m, "http://127.0.0.1:0");
        await app.StartAsync();
        return (m, app, new HttpClient { BaseAddress = new Uri(app.Urls.First()) });
    }

    [Fact]
    public async Task AUserThatRegisteredIsThereAfterARestartWhereItsPersonaWas()
    {
        JsonObject saved;
        string folder = Path.Combine(Path.GetTempPath(), "mmm-kept-" + Guid.NewGuid().ToString("N"));
        try
        {
            // FIRST RUN: Anna registers, her Persona arrives at the Square and walks into the atrium (public); a speech of hers is on its way.
            var (m1, app1, http1) = await WorldAsync();
            try
            {
                var reg = await new MmmClient(http1, "Anna-human").PerformAsync("Register", new Complements { With = [Profile("Anna")] });
                var anna = new MmmClient(http1, "Anna", reg.Tokens["Anna"]);
                Assert.Equal(200, (await anna.PerformAsync("MM-Add", Uc2.C("Anna-Persona", at: "MVS", with: Uc2.SpatialAttitude("SA-Anna", 3, 0)))).Http);
                Assert.Equal(200, (await anna.PerformAsync("MM-Move", Uc2.C("Anna-Persona", from: "MVS", to: "Atrium", with: Uc2.SpatialAttitude("SA-Anna-Atrium", 1.5, -2)))).Http);
                Assert.Equal(200, (await anna.PerformAsync("MM-Add", Uc2.C("Speech-Anna-1", at: "Atrium", with: new JsonObject
                {
                    ["Header"] = "OSD-BSO-V1.5", ["MInstanceID"] = "MI1", ["BasicSpeechObjectID"] = "Speech-Anna-1"
                }))).Http);
                // the state, written to its file as the server writes it
                var kept = new KeptState(m1, folder);
                Assert.Contains("first time", kept.Restore(fresh: false));
                kept.Save();
                saved = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "state.json")))!.AsObject();
            }
            finally { http1.Dispose(); await app1.DisposeAsync(); Meeting.Reset(); }

            // SECOND RUN: a new M-Instance, set up as the first, goes on from the state.
            var (m2, app2, http2) = await WorldAsync();
            try
            {
                var said = new KeptState(m2, folder).Restore(fresh: false);
                Assert.Contains("goes on from its last run", said);
                Assert.Null(m2.TokenOf("Anna"));                                         // no token kept: she is given one when she comes back
                var persona = m2.Get("Anna-Persona");
                Assert.NotNull(persona);
                Assert.Equal("Atrium", persona!.Location);
                Assert.Equal(saved["Items"]!.AsArray().OfType<JsonObject>().Single(i => (string?)i["ID"] == "Anna-Persona")["SpatialAttitude"]!.ToJsonString(), persona.SpatialAttitude!.ToJsonString());
                Assert.Null(m2.Get("Speech-Anna-1"));                                    // momentary: not kept
                var back = m2.Resume("Anna");
                Assert.NotNull(back);
                Assert.Equal("Atrium", (string?)back!.Value.Info["Location"]);
                var anna = new MmmClient(http2, "Anna", back.Value.Token);
                Assert.Equal(200, (await anna.PerformAsync("MM-Move", Uc2.C("Anna-Persona", from: "Atrium", to: "MVS", with: Uc2.SpatialAttitude("SA-Anna-Out", 2, 0)))).Http);
            }
            finally { http2.Dispose(); await app2.DisposeAsync(); Meeting.Reset(); }

            // --fresh: the M-Instance starts as the first time; the old state is kept beside.
            var m3 = new MInstance(Repository.Schemas);
            World.Setup(m3, Repository.Root);
            Assert.Contains("as the first time", new KeptState(m3, folder).Restore(fresh: true));
            Assert.Null(m3.Get("Anna-Persona"));
            Assert.Single(Directory.GetFiles(folder, "state-*.json"));
            Meeting.Reset();
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }
}
