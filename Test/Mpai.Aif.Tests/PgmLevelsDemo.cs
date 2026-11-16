using System.Diagnostics;
using System.Text.Json.Nodes;

using Mpai.Pgm;

namespace Mpai.Aif.Tests;

// THE DEMONSTRATION OF THE LEVELS OF BASIC KNOWLEDGE with a real language model (Ollama, PGM_LLM_MODEL, default llama3.2:3b): the same human sentences through the A-User at Level 0
// (A-User Control asks for no function), Level 1 (functions; Behaviour Planning chooses the Behaviours) and Level 2 (Basic Knowledge also chooses the Behaviours). It writes a page
// that shows, side by side, what Basic Knowledge said, the functions it gave, the Behaviours A-User Formation planned and timed on the words, and its report - and plays them on a
// drawn avatar. Test\Results\2026-11-16\pgm-bkn-levels.html (and .json).
[Trait("Group", "Models")]
[Trait("Blocks", "No")]
public class PgmLevelsDemo
{
    public static readonly string[] Sentences =
    [
        "go and open the doors",
        "hello! who are you?",
        "is the lamp on the table?",
        "close the window, it is cold",
        "no, stop, wait a moment"
    ];

    [SkippableFact]
    public async Task TheLevelsSideBySide()
    {
        var model = new OllamaModel(Environment.GetEnvironmentVariable("PGM_LLM_MODEL") is { Length: > 0 } m ? m : "llama3.2:3b");
        try { await model.AnswerAsync("Answer in JSON.", "ping", new JsonObject { ["type"] = "object" }); }
        catch (Exception e) { Skip.If(true, "Ollama is not available: " + e.Message); }

        var levels = new[] { (Auc: 0, Bkn: 2), (Auc: 1, Bkn: 2), (Auc: 2, Bkn: 2) };
        var runs = new JsonArray();
        var problems = new List<string>();
        foreach (var sentence in Sentences)
            foreach (var (aucLevel, bknLevel) in levels)
            {
                var clock = Stopwatch.StartNew();
                var rig = await PgmLevelsTests.RunAsync(model, sentence, aucLevel, bknLevel);
                var seconds = clock.Elapsed.TotalSeconds;
                var bad = rig.Seen.Where(s => s.Verdict != "valid").Select(s => $"{s.Aim} {s.DataType}: {s.Verdict}").Distinct().ToList();
                var run = new JsonObject { ["Sentence"] = sentence, ["LevelRequested"] = aucLevel, ["RunSeconds"] = Math.Round(seconds, 1), ["Invalid"] = new JsonArray(bad.Select(b => (JsonNode)b).ToArray()) };
                if (!rig.Latest.TryGetValue("PGM-FRS-V1.0", out var frsJson))
                {
                    run["Error"] = "no Final Response: " + (PgmLevelsTests.Auc(rig).FunctionsOutcome?.ToJsonString() ?? rig.At("PGM-HCM-V1.0").LastOrDefault()?.ToJsonString());
                    runs.Add(run); problems.Add($"{sentence} L{aucLevel}: no Final Response"); continue;
                }
                var final = (JsonObject)JsonNode.Parse(frsJson)!;
                var text = PgmAim.TextOf(final["Text"] as JsonObject) ?? "";
                var auf = PgmLevelsTests.Auf(rig);
                var speech = auf.LastSpeechSeconds ?? Math.Max(0.5, text.Length / 15.0);
                run["Reply"] = text;
                run["Words"] = new JsonArray(Communicative.WordTimes(text, speech).Select((t, i) => (JsonNode)new JsonObject
                    { ["Word"] = Communicative.Words(text)[i], ["Start"] = t.Start, ["End"] = t.End }).ToArray());
                run["SpeechSeconds"] = Math.Round(speech, 2);
                run["FunctionsOutcome"] = PgmLevelsTests.Auc(rig).FunctionsOutcome?.DeepClone();
                run["Functions"] = final["CommunicativeFunctions"]?.DeepClone() ?? new JsonArray();
                run["Plan"] = auf.LastPlan?["Functions"]?.DeepClone() ?? new JsonArray();
                run["FunctionStatus"] = PgmLevelsTests.Auc(rig).FunctionStatus?.DeepClone() ?? new JsonArray();
                run["ProcessActions"] = new JsonArray(((final["ProcessActions"]?["ProcessActions"] as JsonObject)?.Select(kv =>
                    (JsonNode)$"{kv.Key} {(string?)kv.Value?[kv.Key.Replace("-", "") + "PARQ"]?["RQ1"]?["ItemID"]}") ?? []).ToArray());
                run["Actions"] = final["Actions"]?.DeepClone();
                run["Conformance"] = new JsonArray(Communicative.Check(final, aucLevel, Communicative.Functions, ["doors", "window", "table", "lamp"]).Select(p => (JsonNode)p).ToArray());
                run["FaceAnimation"] = auf.LastFaceAnimation?["AnimationObjectData"]?.DeepClone();
                run["BodyAnimation"] = auf.LastBodyAnimation?["AnimationObjectData"]?.DeepClone();
                runs.Add(run);
                if (bad.Count > 0) problems.Add($"{sentence} L{aucLevel}: " + string.Join("; ", bad));
                if (run["Conformance"]!.AsArray().Count > 0) problems.Add($"{sentence} L{aucLevel}: not conforming: " + run["Conformance"]!.ToJsonString());
            }

        var data = new JsonObject { ["Model"] = model.Model, ["Made"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), ["Runs"] = runs };
        var folder = Path.Combine(Repository.Root, "Test", "Results", "2026-11-16");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "pgm-bkn-levels.json"), data.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        var page = File.ReadAllText(Path.Combine(Repository.Root, "Test", "Mpai.Aif.Tests", "PgmLevelsDemo.html")).Replace("/*DATA*/null", data.ToJsonString());
        File.WriteAllText(Path.Combine(folder, "pgm-bkn-levels.html"), page);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
