using System.Text.Json;
using System.Text.Json.Nodes;
using AIF.Controller;
using Mpai.Aif.Api;
using Mpai.SpatialAudio;
using Mpai.Cae.Asm;
using Mpai.Providers;
using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Aif.Tests;

// AUDIO SCENE MANAGEMENT AS ONE MODULE, as the author's Reference Model draws it
// (2026/10/04): the audio chain - Audio Object Editing, Audio Scene Editing - and the
// speech chain - Speech Object Editing, Speech Scene Editing - with Text and Speech
// Translation, a session a sequence of runs, the Objects and Scenes in the Module's
// Shared Storage. Capture and hearing are the User Agent's. Judged: a sound the User
// Agent captured enters as an Audio Object and comes back as the open Object;
// given again by its identifier, it is opened from Shared Storage; an Audio Scene
// Command places it, with a second sound, in an Audio Scene heard from the User's
// Point of View - the Scene leaves the Module, and the User Agent's spatial audio
// renders it in stereo; a Speech Object is placed by a Speech Scene Command in a
// Speech Scene, and Speech Scene Editing reports the command; Multimodal Scene
// Editing composes the two Scenes into a Multimodal Scene, and follows the Speech
// Scene when it is edited again; every output valid against its schema. No Port of
// the Module has a Basic Data Type (the author, 2026/10/26): the sounds and the Speech
// Object are given as the Audio Object and the Speech Object that hold them alone.
[Trait("Group", "Models")]
[Trait("Blocks", "Yes")]
public class AsmModuleTests
{
    private const string Asm = AsmProvider.Module;
    private static readonly string Steam = AppContext.BaseDirectory;      // Steam Audio comes with the program: the build puts it beside it

    private static float[] Tone(double hz) => Enumerable.Range(0, 48000).Select(i => (float)(0.3 * Math.Sin(2 * Math.PI * hz * i / 48000.0))).ToArray();
    private static BasicAudioObject Captured(string name, double hz) => new()
    {
        BasicAudioObjectID = name,
        BasicAudioObjectData = [new InlineAudioData(Convert.ToBase64String(SceneAudio.Wav(Tone(hz), 1, 48000)))],
        DescrMetadata = name
    };
    private static SpatialAttitude At(double x, double y) => new()
    {
        ObjectSpatialAttitudeID = Guid.NewGuid().ToString(),
        Position = new Position { PositionID = Guid.NewGuid().ToString(), CartPosition = [x, y, 1.6] },
        Orientation = new Orientation { OrientationID = Guid.NewGuid().ToString(), EulerAngles = [0, 0, 180] }
    };
    private static string Command(UserCommandRQ data) => MpaiJson.ToJson(new UserCommand
    {
        UserCommandID = Guid.NewGuid().ToString(), UserCommandTime = SimpleTime.At(DateTimeOffset.UtcNow), UserCommandRQ = data
    });

    private static string Valid(string schema, string json)
    {
        var all = AIF.Metadata.PublishedSchemas.At(Repository.Schemas);
        var s = all[Path.GetFullPath(Path.Combine(Repository.Schemas, schema))];
        using var doc = JsonDocument.Parse(json);
        lock (AIF.Metadata.PublishedSchemas.Lock) return s.Evaluate(doc.RootElement).IsValid ? "valid" : "not valid";
    }

    // A SPEECH FIRST, IN A SCENE NAMED BY NewScene (2026-11-25). The JSON Interface gives the name of a Scene the first add makes as NewScene (ASE makes the Scene), not as the
    // ObjectID of AddedObjects; Speech Scene Editing must take it from there too, or a speech loaded first is placed nowhere ("a name is needed for the Speech Scene that is made").
    [SkippableFact]
    public void ASpeechAddedFirstGoesInTheSceneThatNewSceneNames()
    {
        Skip.IfNot(SpatialRenderer.Available(Steam), "Models/SteamAudio is absent: Steam Audio is obtained separately (docs/models-provenance.md).");
        var work = Path.Combine(Path.GetTempPath(), "asm-newscene-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var settings = Path.Combine(work, "aim-settings.json");
        File.WriteAllText(settings, JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "AIMs", "aim-settings.json")))!.ToJsonString());
        using var api = new ControllerApi(Repository.Amds, settings, store => new CompositeProvider(new AsmProvider(Repository.Amds), new LeafAimsProvider(store), new VciStandIn()));
        try
        {
            Assert.Equal(AifError.OK, api.StartFlow(Asm, out var id));
            api.SharedStorageInit(id, Path.Combine(work, "assets"));
            var hello = new BasicSpeechObject { BasicSpeechObjectID = "hello", Data = SceneAudio.Wav(Tone(300), 1, 48000) };
            api.InputWrite(id, "CAE-UCM-V1.0", 3, Forms.Acquire(MpaiJson.ToJson(hello)));
            Assert.Equal(AifError.OK, api.OutputRead(id, "OSD-SPO-V1.5", 1, 60_000).Error);
            api.InputWrite(id, "CAE-UCM-V1.0", 4, Command(new UserCommandRQ
            {
                NewScene = "stage",
                AddedObjects = new ObjectPlacements { Objects = [new ObjectPlacement { ObjectID = new ManagedObject { ObjectID = "hello" }, SpatialAttitude = At(0, 0) }] }
            }));
            var report = api.OutputRead(id, "CAE-UCM-V1.0", 4, 60_000);
            Assert.Equal(AifError.OK, report.Error);
            var done = MpaiJson.FromJson<UserCommand>(report.Json!).UserCommandRS!;
            Assert.Contains(done.Actions, a => a.Action == "AddedObjects" && a.Outcome == "Done");
            var speechScene = api.OutputRead(id, "OSD-SSD-V1.5", 1, 60_000);
            Assert.Equal(AifError.OK, speechScene.Error);
            Assert.Single(JsonNode.Parse(Forms.Bss(speechScene.Json!))!["BasicSpeechSceneDescriptors"]!.AsArray());
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    // A SPEECH ADDED TO THE SCENE OF SOUNDS (2026-11-25): the scene exists, so the JSON Interface gives no name; Speech Scene Editing makes its Speech Scene under the name of the one
    // Audio Scene the Module keeps - one scene for the person, of sounds and speech alike.
    [SkippableFact]
    public void ASpeechAddedToTheSceneOfSoundsTakesItsName()
    {
        Skip.IfNot(SpatialRenderer.Available(Steam), "Models/SteamAudio is absent: Steam Audio is obtained separately (docs/models-provenance.md).");
        var work = Path.Combine(Path.GetTempPath(), "asm-samename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var settings = Path.Combine(work, "aim-settings.json");
        File.WriteAllText(settings, JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "AIMs", "aim-settings.json")))!.ToJsonString());
        using var api = new ControllerApi(Repository.Amds, settings, store => new CompositeProvider(new AsmProvider(Repository.Amds), new LeafAimsProvider(store), new VciStandIn()));
        try
        {
            Assert.Equal(AifError.OK, api.StartFlow(Asm, out var id));
            api.SharedStorageInit(id, Path.Combine(work, "assets"));
            api.InputWrite(id, "CAE-UCM-V1.0", 1, Forms.Acquire(Forms.Auo(Captured("low", 440))));
            Assert.Equal(AifError.OK, api.OutputRead(id, "OSD-AUO-V1.5", 1, 60_000).Error);
            api.InputWrite(id, "CAE-UCM-V1.0", 2, Command(new UserCommandRQ
            {
                AddedObjects = new ObjectPlacements { ObjectID = "stage", Objects = [new ObjectPlacement { ObjectID = new ManagedObject { ObjectID = "low" }, SpatialAttitude = At(1, 1) }] }
            }));
            Assert.Equal(AifError.OK, api.OutputRead(id, "OSD-ASD-V1.5", 1, 60_000).Error);
            api.OutputRead(id, "CAE-UCM-V1.0", 2, 5_000);
            var hello = new BasicSpeechObject { BasicSpeechObjectID = "hello", Data = SceneAudio.Wav(Tone(300), 1, 48000) };
            api.InputWrite(id, "CAE-UCM-V1.0", 3, Forms.Acquire(MpaiJson.ToJson(hello)));
            Assert.Equal(AifError.OK, api.OutputRead(id, "OSD-SPO-V1.5", 1, 60_000).Error);
            api.InputWrite(id, "CAE-UCM-V1.0", 4, Command(new UserCommandRQ          // as the JSON Interface gives it when the scene exists: no name
            {
                AddedObjects = new ObjectPlacements { Objects = [new ObjectPlacement { ObjectID = new ManagedObject { ObjectID = "hello" }, SpatialAttitude = At(0, 0) }] }
            }));
            var report = api.OutputRead(id, "CAE-UCM-V1.0", 4, 60_000);
            Assert.Equal(AifError.OK, report.Error);
            Assert.Contains(MpaiJson.FromJson<UserCommand>(report.Json!).UserCommandRS!.Actions, a => a.Action == "AddedObjects" && a.Outcome == "Done");
            var speechScene = api.OutputRead(id, "OSD-SSD-V1.5", 1, 60_000);
            Assert.Equal(AifError.OK, speechScene.Error);
            Assert.Equal("stage", (string?)JsonNode.Parse(speechScene.Json!)!["SpeechSceneDescriptorsID"]);
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    [SkippableFact]
    public void OneModuleOneSession()
    {
        Skip.IfNot(SpatialRenderer.Available(Steam), "Models/SteamAudio is absent: Steam Audio is obtained separately (docs/models-provenance.md).");
        var work = Path.Combine(Path.GetTempPath(), "asm-module-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var settings = Path.Combine(work, "aim-settings.json");
        var all = JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "AIMs", "aim-settings.json")))!.AsObject();
        File.WriteAllText(settings, all.ToJsonString());

        var r = new Dictionary<string, string>();
        using var api = new ControllerApi(Repository.Amds, settings, store => new CompositeProvider(new AsmProvider(Repository.Amds), new LeafAimsProvider(store), new VciStandIn()));
        int id = -1;
        try
        {
            r["the Module starts"] = api.StartFlow(Asm, out id).ToString();
            api.SharedStorageInit(id, Path.Combine(work, "assets"));

            // 1. Two sounds the User Agent captured: each a Basic Audio Object, back as the open Object.
            string Played()
            {
                var read = api.OutputRead(id, "OSD-AUO-V1.5", 1, 60_000);
                return read.Error == AifError.OK ? Forms.Bao(read.Json!) : $"nothing ({read.Error})";
            }
            api.InputWrite(id, "CAE-UCM-V1.0", 1, Forms.Acquire(Forms.Auo(Captured("low", 440))));
            var first = Played();
            api.InputWrite(id, "CAE-UCM-V1.0", 1, Forms.Acquire(Forms.Auo(Captured("high", 880))));
            var second = Played();
            r["a sound the User Agent captured, the open Object"] = first.StartsWith('{') ? $"{JsonNode.Parse(first)!["Header"]}, {Valid("OSD/V1.5/data/BasicAudioObject.json", first)}" : first;
            r["a second sound, the open Object"] = second.StartsWith('{') ? $"{JsonNode.Parse(second)!["Header"]}, {Valid("OSD/V1.5/data/BasicAudioObject.json", second)}" : second;

            // 1b. An Object of Shared Storage, given by its identifier: opened.
            api.InputWrite(id, "CAE-UCM-V1.0", 1, Forms.Acquire(Forms.Auo(first)));
            var stored = Played();
            r["an Object of Shared Storage, opened"] = stored.StartsWith('{') ? $"{JsonNode.Parse(stored)!["Header"]} {JsonNode.Parse(stored)!["BasicAudioObjectID"]}, {Valid("OSD/V1.5/data/BasicAudioObject.json", stored)}" : stored;

            // 2. One User Command: both in a Basic Audio Scene, heard from the User.
            var user = new PointOfView { PointOfViewID = "user", CartPosition = [0, 0, 1.6], Orientation = [0, 0, 0] };
            api.InputWrite(id, "CAE-UCM-V1.0", 2, Command(new UserCommandRQ                     // an Audio Scene command: Port 2 (Audio Scene Editing)
            {
                UserPoV = user,
                AddedObjects = new ObjectPlacements { ObjectID = "stage", Objects = [new ObjectPlacement { ObjectID = new ManagedObject { ObjectID = "low" }, SpatialAttitude = At(-1.5, 1.5) },
                                                                                      new ObjectPlacement { ObjectID = new ManagedObject { ObjectID = "high" }, SpatialAttitude = At(1.5, 1.5) }] }
            }));
            var scene = api.OutputRead(id, "OSD-ASD-V1.5", 1, 60_000);
            // Each editor has its own User Command Port (1 Audio Object, 2 Audio Scene, 3 Speech Object, 4 Speech Scene, 5 Multimodal Scene): the command above reached Audio Scene Editing only, which reports at Port 2.
            api.OutputRead(id, "CAE-UCM-V1.0", 2, 5_000);
            var basScene = scene.Error == AifError.OK ? Forms.Bas(scene.Json!) : null;
            r["the Scene leaving the Module"] = scene.Error == AifError.OK
                ? $"{JsonNode.Parse(basScene!)!["BasicAudioSceneDescriptorsData"]!.AsArray().Count} members, {Valid("OSD/V1.5/data/AudioSceneDescriptors.json", scene.Json!)}"
                : $"nothing ({scene.Error})";
            if (scene.Error == AifError.OK)
            {
                // What the User Agent's Loudspeaker Unit would play.
                var heard = MpaiJson.ToJson(new AudioRendering(Steam).Render(basScene!));
                var wav = MpaiJson.FromJson<BasicAudioObject>(heard)!.Data;
                r["the Scene heard by the User"] = $"{BitConverter.ToInt16(wav, 22)} channels at {BitConverter.ToInt32(wav, 24)} Hz, {Valid("OSD/V1.5/data/BasicAudioObject.json", heard)}";
            }

            // 3. The speech chain: a Speech Object the User Agent captured, placed by a
            // Speech Scene Command in a Basic Speech Scene; Speech Scene Editing reports it.
            var hello = new BasicSpeechObject { BasicSpeechObjectID = "hello", Data = SceneAudio.Wav(Tone(300), 1, 48000) };
            api.InputWrite(id, "CAE-UCM-V1.0", 3, Forms.Acquire(MpaiJson.ToJson(hello)));
            var kept = api.OutputRead(id, "OSD-SPO-V1.5", 1, 60_000);
            r["a Speech Object, the open Object"] = kept.Error == AifError.OK ? $"{JsonNode.Parse(Forms.Bso(kept.Json!))!["BasicSpeechObjectID"]}, {Valid("OSD/V1.5/data/SpeechObject.json", kept.Json!)}" : $"nothing ({kept.Error})";
            api.InputWrite(id, "CAE-UCM-V1.0", 4, Command(new UserCommandRQ                     // a Speech Scene command: Port 4 (Speech Scene Editing)
            {
                UserPoV = user,
                AddedObjects = new ObjectPlacements { ObjectID = "dialogue", Objects = [new ObjectPlacement { ObjectID = new ManagedObject { ObjectID = "hello" }, SpatialAttitude = At(0, 2) }] }
            }));
            var speechScene = api.OutputRead(id, "OSD-SSD-V1.5", 1, 60_000);
            r["the Speech Scene leaving the Module"] = speechScene.Error == AifError.OK
                ? $"{JsonNode.Parse(Forms.Bss(speechScene.Json!))!["BasicSpeechSceneDescriptors"]!.AsArray().Count} member(s), {Valid("OSD/V1.5/data/SpeechSceneDescriptors.json", speechScene.Json!)}"
                : $"nothing ({speechScene.Error})";
            var sseReport = api.OutputRead(id, "CAE-UCM-V1.0", 4, 60_000);
            r["the report of Speech Scene Editing"] = sseReport.Error == AifError.OK && MpaiJson.FromJson<UserCommand>(sseReport.Json!).UserCommandRS is { } done
                ? $"{done.Outcome}; " + string.Join("; ", done.Actions.Select(a => $"{a.Action} {a.Outcome}")) + $", {Valid("CAE3/V1.0/data/UserCommand.json", sseReport.Json!)}"
                : $"nothing ({sseReport.Error})";

            // 4. The two Scenes composed by Multimodal Scene Editing into a Multimodal
            // Scene; then the Speech Scene edited again, and followed.
            string? IdIn(AifError error, string? json, string field) => error == AifError.OK ? (string?)JsonNode.Parse(json!)![field] : null;
            var basId = IdIn(scene.Error, scene.Json, "AudioSceneDescriptorsID");
            var bssId = IdIn(speechScene.Error, speechScene.Json, "SpeechSceneDescriptorsID");
            api.InputWrite(id, "CAE-UCM-V1.0", 5, Command(new UserCommandRQ                     // a Multimodal Scene command: Port 5 (Multimodal Scene Editing)
            {
                UserPoV = user,
                AddedObjects = new ObjectPlacements { ObjectID = "mix", Objects = [new ObjectPlacement { ObjectID = new ManagedObject { ObjectID = basId }, SpatialAttitude = At(0, 0) },
                                                                 new ObjectPlacement { ObjectID = new ManagedObject { ObjectID = bssId }, SpatialAttitude = At(0, 0) }] }
            }));
            string Members(string json)
            {
                var node = JsonNode.Parse(json)!;
                return string.Join(" + ", (node["SubAudioScenes"]?.AsArray() ?? []).Select(m => "Audio Scene " + (string?)m!["SubAudioScene"]!["AudioSceneDescriptorsID"])
                    .Concat((node["SubSpeechScenes"]?.AsArray() ?? []).Select(m => "Speech Scene " + (string?)m!["SubSpeechScene"]!["SpeechSceneDescriptorsID"])));
            }
            var bms = api.OutputRead(id, "OSD-MSD-V1.5", 1, 60_000);
            r["the Multimodal Scene leaving the Module"] = bms.Error == AifError.OK ? $"{Members(bms.Json!)}, {Valid("OSD/V1.5/data/MultimodalSceneDescriptors.json", bms.Json!)}" : $"nothing ({bms.Error})";
            var bmeReport = api.OutputRead(id, "CAE-UCM-V1.0", 5, 60_000);
            r["the report of Multimodal Scene Editing"] = bmeReport.Error == AifError.OK && MpaiJson.FromJson<UserCommand>(bmeReport.Json!).UserCommandRS is { } bmeDone
                ? $"{bmeDone.Outcome}; " + string.Join("; ", bmeDone.Actions.Select(a => $"{a.Action} {a.Outcome}"))
                : $"nothing ({bmeReport.Error})";

            api.InputWrite(id, "CAE-UCM-V1.0", 4, Command(new UserCommandRQ                     // the Speech Scene edited again: Port 4
            {
                MovedObjects = new ObjectMovements { Objects = [new ObjectMovement { ObjectID = new ManagedObject { ObjectID = "hello" }, NewSpatialAttitude = At(1, 3) }] }
            }));
            var movedSpeech = api.OutputRead(id, "OSD-SSD-V1.5", 1, 60_000);
            var followed = api.OutputRead(id, "OSD-MSD-V1.5", 1, 60_000);
            // The Speech Scene keeps its name when it is edited: the Multimodal Scene holds it by that name, as it now is.
            r["the Speech Scene edited again, followed"] = followed.Error != AifError.OK || movedSpeech.Error != AifError.OK ? $"nothing ({followed.Error})"
                : JsonNode.Parse(followed.Json!)!["SubSpeechScenes"]!.AsArray().Any(m => JsonNode.DeepEquals(m!["SubSpeechScene"], JsonNode.Parse(movedSpeech.Json!)) && !JsonNode.DeepEquals(JsonNode.Parse(speechScene.Json!), JsonNode.Parse(movedSpeech.Json!)))
                    ? "the Multimodal Scene holds the new version" : "the Multimodal Scene holds the old version";
        }
        finally
        {
            api.StopFlow(id);
            try { Directory.Delete(work, true); } catch { }
        }
        Expected.Match("asm-module.json", r);
    }
}
