using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

using Mpai.Mmm;
using Mpai.Mmm.Client;
using Mpai.Mmm.Server;
using Mpai.Xrv;
using Mpai.Xrv.Acg;
using Mpai.Xrv.End;
using Mpai.Xrv.VirtualVenue;

namespace Mpai.Aif.Tests;

// N-26 (2026-11-15): THE LONGER EVENING ON TWO METAVERSES. What the Status Interpretation AIMs now read in the Descriptors (the body: raising arms, sitting; the applause; where one
// faces), how a Cue target is met (an Intensity is a minimum; a list of numbers is a tuple), what the Module says of why it acted (the Cue and the Condition met, in the DescrMetadata of
// the Action ID), the venue device on MMM-TEC (MmmVenue) and the show documents (Test/Data/XRV/LongerEvening).
[Trait("Group", "Fast")]
public class XrvTwoMetaversesTests
{
    private static readonly string Folder = Path.Combine(Repository.Root, "Test", "Data", "XRV", "LongerEvening");
    private static JsonObject Doc(string file) => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Folder, file)))!;

    // The applause of the Audience in the Theatre's plan (since 2026-11-16 the voices of the performers are Audio Objects of the plan too, and come first).
    private static JsonNode AudienceAudio(JsonObject plan) => plan["Actions"]!.AsArray().OfType<JsonObject>().First(a => a["Audio"] is not null && (string?)a["Nil"] == "Audience")["Audio"]!.DeepClone();

    private static JsonObject Body(double la, double ra, double hip = 0, double knee = 0) => (JsonObject)JsonNode.Parse(
        ProcessActionPlayer.Frame("b", new JsonObject { ["LeftArmElevation"] = la, ["RightArmElevation"] = ra, ["HipFlexion"] = hip, ["KneeFlexion"] = knee }))!;

    private static short[] Pcm(string wav) { var b = File.ReadAllBytes(wav); var p = new short[(b.Length - 44) / 2]; Buffer.BlockCopy(b, 44, p, 0, p.Length * 2); return p; }

    [Fact]
    public void TheBodyGivesRaisingArmsAndStandingOrSitting()
    {
        var up = Behaviour.JointsOf(Body(170, 165))!;
        var visual = Behaviour.Visual("v1", up, 0, "UE1");
        Assert.Empty(XrvSchemas.Violations("XRV1/V1.0/data/VisualBehaviour.json", visual));
        Assert.Equal("Raising arms", visual["Behaviour"]!["Gestures"]![0]!["Gesture"]!.GetValue<string>());
        Assert.True(visual["Behaviour"]!["StandSit"]!.GetValue<bool>());                                  // standing
        Assert.Null(Behaviour.Visual("v2", Behaviour.JointsOf(Body(170, 40))!, 0, null)["Behaviour"]!["Gestures"]);   // one arm up is not 'Raising arms'
        Assert.False(Behaviour.Visual("v3", Behaviour.JointsOf(Body(0, 0, 90, 90))!, 0, null)["Behaviour"]!["StandSit"]!.GetValue<bool>());   // sitting
        Assert.Null(Behaviour.JointsOf(new JsonObject { ["Header"] = "PAF-BDO-V1.6", ["BodyDescriptorsObjectID"] = "x", ["BodyDescriptorsData"] = new JsonArray(new JsonObject { ["Data"] = "BVH ..." }) }));
    }

    [Fact]
    public void TheApplauseIsClappingAndAToneOrSilenceIsNot()
    {
        Assert.True(Behaviour.ClappingIntensity(Pcm(Path.Combine(Folder, "applause.wav"))) >= 60);
        Assert.Equal(0, Behaviour.ClappingIntensity(new short[16000]));
        var tone = new short[32000];
        for (var i = 0; i < tone.Length; i++) tone[i] = (short)(12000 * (Math.Sin(2 * Math.PI * 180 * i / 16000) + 0.5 * Math.Sin(2 * Math.PI * 360 * i / 16000) + 0.3 * Math.Sin(2 * Math.PI * 540 * i / 16000)) / 1.8);
        Assert.True(Behaviour.ClappingIntensity(tone) < 10);                                              // a voiced, harmonic sound
    }

    [Fact]
    public void AnIntensityIsAMinimumAndAListOfNumbersIsATuple()
    {
        JsonNode Clap(double v) => new JsonObject { ["AudioBehaviour"] = new JsonObject { ["BehaviourData"] = new JsonObject { ["ClappingIntensity"] = v } } };
        Assert.True(JsonMatch.Meets(Clap(60), Clap(74)));
        Assert.False(JsonMatch.Meets(Clap(60), Clap(40)));
        JsonNode Facing(double yaw) => new JsonObject { ["Orientation"] = new JsonArray(yaw, 0.0, 0.0) };
        Assert.True(JsonMatch.Meets(Facing(0), Facing(0)));
        Assert.False(JsonMatch.Meets(Facing(0), Facing(180)));                                             // was met before 2026-11-15: [0,0,0] read as a set
        Assert.True(JsonMatch.Meets(new JsonObject { ["L"] = new JsonArray("a") }, new JsonObject { ["L"] = new JsonArray("b", "a") }));   // other lists as before
    }

    // From a Data In to a Status: END puts the body, the Spatial Attitude and the sound into valid Descriptors; the Status Interpretation reads them into a valid Status.
    [Fact]
    public void FromDataInToStatusTheBodyTheApplauseAndTheFacingAreRead()
    {
        var sva = ScriptVenueAssociations.Read(File.ReadAllText(Path.Combine(Folder, "ScriptVenueAssociations.json")));
        static string Sa(double yaw) => new JsonObject
        {
            ["Header"] = "OSD-OSA-V1.5", ["ObjectSpatialAttitudeID"] = "sa",
            ["Position"] = new JsonObject { ["Header"] = "OSD-OPS-V1.5", ["PositionID"] = "p", ["CartPosition"] = new JsonArray(0.1, 0.9, 0.0) },
            ["Orientation"] = new JsonObject { ["Header"] = "OSD-OOR-V1.5", ["OrientationID"] = "o", ["Orientation"] = new JsonArray(yaw, 0.0, 0.0) }
        }.ToJsonString();
        var audience = AudienceAudio(Doc("ProcessActions-Theatre.json"));
        var dataIn = new JsonObject
        {
            ["Header"] = "XRV-RDI-V1.0", ["UEnvironmentID"] = "UE-Theatre", ["REDataInID"] = "rdi", ["REDataInTime"] = XrvTime.Simple("t", 0),
            ["REDataIn"] = new JsonObject
            {
                ["MoCapObjects"] = new JsonArray(new JsonObject { ["MoCapObjectTime"] = XrvTime.Simple("m", 0), ["MoCapObject"] = new JsonObject
                {
                    ["Header"] = "PAF-MCO-V1.6", ["MoCapObjectID"] = "Anna", ["MoCapData"] = new JsonArray(new JsonObject { ["Data"] = Sa(-10) }, new JsonObject { ["Data"] = Body(170, 170).ToJsonString() })
                } }),
                ["AudioObjects"] = new JsonArray(new JsonObject { ["AudioObjectTime"] = XrvTime.Simple("a", 0), ["AudioObject"] = audience })
            }
        };
        Assert.Empty(XrvSchemas.Violations("XRV1/V1.0/data/REDataIn.json", dataIn));
        var made = EndAimProcessor.Describe(dataIn, true, EndAimProcessor.Sources(sva, true), "end-1", 0);
        var performance = made.Single(m => m.Family == Family.Performance).Descriptors;
        var participants = made.Single(m => m.Family == Family.Participant).Descriptors;
        Assert.Empty(XrvSchemas.Violations("XRV1/V1.0/data/REPerformanceDescriptors.json", performance));
        Assert.Empty(XrvSchemas.Violations("XRV1/V1.0/data/REParticipantDescriptors.json", participants));

        var anna = Interpretation.Derive(DescriptorsObject.Read(StatusKind.REPerformance, performance.ToJsonString()), "s1", 0);
        Assert.Empty(anna.Violations());
        var a = anna.Data!["IndividualStatuses"]![0]!;
        Assert.Equal("Raising arms", a["VisualBehaviour"]!["Behaviour"]!["Gestures"]![0]!["Gesture"]!.GetValue<string>());
        Assert.Equal("[0,1,0]", a["SpatialAttitude"]!["Position"]!["CartPosition"]!.ToJsonString());   // to the nearest metre
        Assert.Equal(0, a["SpatialAttitude"]!["Orientation"]!["Orientation"]![0]!.GetValue<double>());  // -10 degrees to the nearest 45: 0

        var hall = Interpretation.Derive(DescriptorsObject.Read(StatusKind.REParticipant, participants.ToJsonString()), "s2", 0);
        Assert.Empty(hall.Violations());
        Assert.True(hall.Data!["IndividualStatuses"]![0]!["AudioBehaviour"]!["BehaviourData"]!["ClappingIntensity"]!.GetValue<double>() >= 60);

        // THE VIRTUAL SIDE: the body in the Metaverse Object -> the Portable Avatar of the VE Participant Descriptors -> the Visual Behaviour of the Status
        var vdi = new JsonObject
        {
            ["Header"] = "XRV-VDI-V1.0", ["UEnvironmentID"] = "UE-Square", ["VEDataInID"] = "vdi", ["VEDataInTime"] = XrvTime.Simple("t", 0),
            ["VEDataIn"] = new JsonObject { ["MetaverseObjects"] = new JsonArray(new JsonObject { ["MetaverseObject"] = new JsonObject
            {
                ["Header"] = "XRV-MVO-V1.0", ["MetaverseObjectID"] = "Guest2", ["UEnvironmentID"] = "UE-Square", ["MetaverseObjectTime"] = XrvTime.Simple("g", 0),
                ["MetaverseObjectData"] = new JsonArray(new JsonObject { ["ID"] = "Guest2" }, new JsonObject { ["Data"] = Sa(0) }, new JsonObject { ["Data"] = Body(160, 170).ToJsonString() }),
                ["MetaverseQualifier"] = new JsonObject { ["Header"] = "TFA-MAQ-V1.5", ["MetaverseQualifierID"] = "q", ["SubTypes"] = new JsonObject(), ["Formats"] = new JsonObject() }
            } }) }
        };
        Assert.Empty(XrvSchemas.Violations("XRV1/V1.0/data/VEDataIn.json", vdi));
        var guests = EndAimProcessor.Describe(vdi, false, EndAimProcessor.Sources(sva, false), "end-2", 0).Single().Descriptors;
        Assert.Empty(XrvSchemas.Violations("XRV1/V1.0/data/VEParticipantDescriptors.json", guests));
        var g = Interpretation.Derive(DescriptorsObject.Read(StatusKind.VEParticipant, guests.ToJsonString()), "s3", 0);
        Assert.Empty(g.Violations());
        Assert.Equal("Raising arms", g.Data!["IndividualStatuses"]![0]!["VisualBehaviour"]!["Behaviour"]!["Gestures"]![0]!["Gesture"]!.GetValue<string>());
    }

    [Fact]
    public void TheShowDocumentsAreValid()
    {
        foreach (var (file, schema) in new[] { ("Script.json", "Script"), ("ActionTable.json", "ActionTable"), ("ScriptVenueAssociations.json", "ScriptVenueAssociations"),
                                              ("REVenueSpecification.json", "REVenueSpecification"), ("VEVenueSpecification.json", "VEVenueSpecification"), ("ParticipantData.json", "ParticipantData") })
            Assert.True(XrvSchemas.Violations($"XRV1/V1.0/data/{schema}.json", Doc(file)) is { Count: 0 }, $"{file}: {string.Join("; ", XrvSchemas.Violations($"XRV1/V1.0/data/{schema}.json", Doc(file)))}");
        var script = Script.Read(File.ReadAllText(Path.Combine(Folder, "Script.json")));
        Assert.Equal(["C0", "C1", "C2", "C3", "C4", "C5", "C6a", "END"], script.Cues.Select(c => c.Id));
        Assert.True(script.HasOperator);
    }

    // THE CUE ENGINE says why: each Action carries the Cue and, in plain words with its Data Type, what met the Conditions; a Cue with no group ends the show (group 2 of C5).
    [Fact]
    public void TheEngineSaysWhyAndACueWithNoGroupEndsTheShow()
    {
        var script = Script.Read(File.ReadAllText(Path.Combine(Folder, "Script.json")));
        var table = ActionTable.Read(File.ReadAllText(Path.Combine(Folder, "ActionTable.json")));
        var engine = new CueEngine(script, table);
        Assert.Empty(engine.Start());                                                                      // an operator: nothing before Go
        var go = engine.Control("Go");
        Assert.Contains(go, e => e.Kind == EventKind.REAction && e.Id == "RE-HOUSE-DOWN" && e.Why!.StartsWith("Cue C0 (Real)"));
        Assert.Equal("C1", engine.CurrentCue);

        StatusObject Status(StatusKind kind, JsonObject individual)
        {
            var info = KindInfo.Of(kind);
            return StatusObject.Read(kind, new JsonObject { ["Header"] = info.StatusHeader, [info.StatusIdKey] = "s", ["UEnvironmentID"] = "UE", [info.StatusTimeKey] = XrvTime.Simple("t", 0),
                [info.StatusDataKey] = new JsonObject { ["IndividualStatuses"] = new JsonArray(individual) } }.ToJsonString());
        }
        engine.Receive(Status(StatusKind.REPerformance, new JsonObject { ["REPerformerID"] = "Anna" }));
        var c1 = engine.Receive(Status(StatusKind.VEParticipant, new JsonObject { ["VEParticipantID"] = "Guest1" }));
        var lamps = c1.Single(e => e.Id == "RE-LAMPS-FULL");
        Assert.Contains("REPerformanceStatus (XRV-RFS-V1.0): Anna is present", lamps.Why);
        Assert.Contains("VEParticipantStatus (XRV-VTS-V1.0): Guest1 is present", lamps.Why);

        // straight to C5 (Skip three times) and Anna sits: group 2 fades to black and the show ends at END, a Cue with no group
        engine.Control("Skip"); engine.Control("Skip"); engine.Control("Skip");
        Assert.Equal("C5", engine.CurrentCue);
        var sits = engine.Receive(Status(StatusKind.REPerformance, new JsonObject
        {
            ["REPerformerID"] = "Anna", ["VisualBehaviour"] = Behaviour.Visual("vb", Behaviour.JointsOf(Body(0, 0, 90, 90))!, 0, "UE")
        }));
        Assert.Contains(sits, e => e.Id == "RE-FADE-BLACK" && e.Why!.Contains("Anna sitting"));
        Assert.Equal(ShowState.Ended, engine.State);
    }

    // THE VENUE ON MMM-TEC (MmmVenue, Real side): a Persona on the stage animated by a stream is given with its body and where it faces; the applause with its data; the console's
    // light is a Go; a DMX command lights the Fixture, a Colour command colours it.
    [Fact]
    public async Task TheTheatreVenueGivesBodiesApplauseAndGoAndLightsItsFixtures()
    {
        var plan = Doc("ProcessActions-Theatre.json");
        var m = new MInstance(Repository.Schemas, "Theatre", "ME-Theatre");
        foreach (var l in plan["Locations"]!.AsArray().OfType<JsonObject>())
            m.AddLocation(l["ID"]!.GetValue<string>(), owner: "TheatreVenue", isPublic: true, x: l["X"]!.GetValue<double>(), y: l["Y"]!.GetValue<double>(), size: l["Size"]!.GetValue<double>(), at: l["At"]?.GetValue<string>());
        var tokens = new Dictionary<string, string> { ["TheatreVenue"] = m.AddService("TheatreVenue"), ["AnnaUser"] = m.AddService("AnnaUser", "Anna"), ["Audience"] = m.AddService("Audience"), ["Operator"] = m.AddService("Operator") };
        await using var app = MmmServer.Build(m, "http://127.0.0.1:0");
        await app.StartAsync();
        var url = app.Urls.First();
        using var http = new HttpClient { BaseAddress = new Uri(url) };
        var venue = new MmmVenue(new MmmClient(http, "TheatreVenue", tokens["TheatreVenue"]), true, "Theatre", "TheatreStage",
                                 new Dictionary<string, double> { ["FX1"] = 0, ["FX2"] = 0 }, "UE-Theatre", "Theatre", url.Replace("http", "ws"), ["Console"], ["TheatreStage", "Hall"], TimeSpan.FromMilliseconds(50));
        var given = new List<JsonObject>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var reading = Task.Run(async () => { await foreach (var (_, json) in venue.ReadAsync(stop.Token)) lock (given) given.Add((JsonObject)JsonNode.Parse(json)!); });
        async Task<bool> Until(Func<JsonObject, bool> seen) { for (var i = 0; i < 200; i++) { lock (given) if (given.Any(seen)) return true; await Task.Delay(50); } return false; }

        var player = new ProcessActionPlayer(new JsonObject
        {
            ["MInstance"] = "Theatre", ["Root"] = "Theatre",
            ["Actions"] = new JsonArray(
                new JsonObject { ["t"] = 0.0, ["Process"] = "Operator", ["Action"] = "MM-Add", ["Nil"] = "Console", ["At"] = "Booth", ["Model3D"] = true },
                new JsonObject { ["t"] = 0.1, ["Process"] = "AnnaUser", ["Action"] = "MM-Add", ["Nil"] = "Anna", ["At"] = "TheatreStage", ["X"] = 0.0, ["Y"] = 1.0, ["Yaw"] = 180.0 },
                new JsonObject { ["t"] = 0.2, ["Process"] = "AnnaUser", ["Action"] = "Animate", ["Nil"] = "Anna", ["Stream"] = "Anna-motion" },
                new JsonObject { ["t"] = 0.4, ["Process"] = "AnnaUser", ["Action"] = "Frame", ["Stream"] = "Anna-motion", ["Joints"] = new JsonObject { ["LeftArmElevation"] = 170, ["RightArmElevation"] = 170, ["HipFlexion"] = 0, ["KneeFlexion"] = 0 } },
                new JsonObject { ["t"] = 0.6, ["Process"] = "Audience", ["Action"] = "MM-Add", ["Nil"] = "Audience", ["At"] = "Hall", ["Audio"] = AudienceAudio(plan) },
                new JsonObject { ["t"] = 0.8, ["Process"] = "Operator", ["Action"] = "Property Change", ["Nil"] = "Console", ["Intensity"] = 1.0 })
        }, p => new MmmClient(http, p, tokens[p]), url.Replace("http", "ws"));
        await player.RunAsync(DateTimeOffset.UtcNow, stop.Token);
        Assert.Empty(player.Failed);

        Assert.True(await Until(d => d["REDataIn"]?["MoCapObjects"]?.AsArray().Any(o => o?["MoCapObject"]?["MoCapData"]?.AsArray().Any(x => x?["Data"]?.GetValue<string>().Contains("RightArmElevation") == true) == true) == true),
                    "no MoCap Object with the body of Anna");
        Assert.True(await Until(d => d["REDataIn"]?["AudioObjects"] is JsonArray { Count: 1 }), "the applause was not given");
        Assert.True(await Until(d => d["REDataIn"]?["ControlObjects"]?[0]?["ControlObject"]?["ControlData"]?.GetValue<string>() == "Go"), "no Go");
        lock (given) foreach (var d in given) Assert.Empty(XrvSchemas.Violations("XRV1/V1.0/data/REDataIn.json", d));

        await venue.DeliverAsync(MmmVenue.RDO, new JsonObject { ["REDataOut"] = new JsonObject { ["ControlObjects"] = new JsonArray(
            new JsonObject { ["ControlObject"] = new JsonObject { ["ControllerID"] = "FX1", ["ControlData"] = "{\"Protocol\":\"DMX\",\"Universe\":1,\"Channel\":2,\"Value\":255}" } },
            new JsonObject { ["ControlObject"] = new JsonObject { ["ControlData"] = "{\"Object\":\"FX2\",\"Property\":\"Colour\",\"Value\":\"FFB000\"}" } }) } }.ToJsonString(), CancellationToken.None);
        Assert.Equal(1.0, m.Get("FX1")!.LightSource!["Intensity"]!.GetValue<double>());
        Assert.Equal("FFB000", m.Get("FX2")!.LightSource!["ColourInformation"]!["ColourData"]!.GetValue<string>());
        Assert.Empty(venue.Unperformed);
        stop.Cancel();
        try { await reading; } catch (OperationCanceledException) { }
        await player.CloseAsync();
        await venue.SafeStopAsync();
    }
}

// THE WHOLE SHOW (N-26), through the User Agent, the Controller API and the Module 1XRV-LTP-V1.0-I01, on the two M-Instances, at a fifth of its time: the seven Cues fire in order, each
// for the reason the Script gives, and nothing is reported. The demo program (Apps/XRV/LTP/TwoMetaverses) is run as it is, without its video.
[Trait("Group", "Slow")]
public class XrvTwoMetaversesShowTests
{
    [Fact]
    public async Task TheLongerEveningRunsOnTwoMetaversesAndEveryCueFiresForItsReason()
    {
        var dll = Path.Combine(Repository.Root, "Apps", "XRV", "LTP", "TwoMetaverses", "bin", "Debug", "net10.0", "XrvTwoMetaverses.dll");
        Assert.True(File.Exists(dll), "the demo is not built: " + dll);
        var output = Path.Combine(Path.GetTempPath(), "xrv-two-" + Guid.NewGuid().ToString("N"));
        try
        {
            var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" --scale 0.2 --no-video --out \"{output}\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(4));
            var text = await stdout + await stderr;
            var fired = text.Split('\n').Where(l => l.Contains("Module ->")).ToList();
            string[] order = ["RE-HOUSE-DOWN", "RE-LAMPS-FULL", "RE-AMBER-SPOT", "RE-SCRIM-HALF", "RE-LAMPS-OFF", "RE-SCRIM-FULL", "RE-FINALE"];
            var seen = fired.Where(l => l.Contains("-> theatre")).Select(l => order.FirstOrDefault(o => l.Contains(o))).OfType<string>().ToList();
            Assert.True(order.SequenceEqual(seen), "the RE Actions: " + string.Join(", ", seen) + "\n" + text[^Math.Min(4000, text.Length)..]);
            Assert.Contains(fired, l => l.Contains("VE-FIREWORKS") && l.Contains("Cue C6a (Real)") && l.Contains("Anna facing 0 degrees"));
            Assert.Contains(fired, l => l.Contains("RE-AMBER-SPOT") && l.Contains("XRV-RTS-V1.0") && l.Contains("Audience clapping"));
            Assert.Contains(fired, l => l.Contains("VE-FIREFLIES") && l.Contains("Guest1 raising arms; Guest2 raising arms; Guest3 raising arms"));
            Assert.Contains("problems: 0", text);
            Assert.Equal(0, p.ExitCode);
            Assert.True(File.Exists(Path.Combine(output, "log.json")));
        }
        finally { try { Directory.Delete(output, true); } catch { } }
    }
}
