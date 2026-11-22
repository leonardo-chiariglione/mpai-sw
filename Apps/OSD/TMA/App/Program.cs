using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Mpai.Core;
using Mpai.Mas.Client;
using Mpai.Ua;

namespace Mpai.Osd.Tma.App;

// TELEVISION MEDIA ANALYSIS, the App.
//
//   TmaApp [--controller http://127.0.0.1:5060] --video programme.mp4 [--seconds 30] [--text "what the programme is about"] [--idle 150] --out folder
//
// --video       the programme: a video file with its sound
// --seconds     how much of it, from the start (30 by default)
// --text        the auxiliary text of the programme: what it is about, who is in it
// --idle        the seconds without a new Event Descriptors after which the analysis is taken as finished (150 by default)
// --controller  the Controller this User Agent talks to (TmaController, started by whoever launches the App)
// --out         the folder where the Multimodal Event Descriptors are written (event-descriptors.json)
//
// The App is TV Splitting's stand-in: it splits the video with ffmpeg into its pictures (5 a second, 960 x 540) and its sound (16 kHz, mono) and gives them to the Module as the workflow says.
internal static class Program
{
    private const double Fps = 5;

    private static int Main(string[] args)
    {
        string? tempFolder = null;
        try
        {
            string? video = null, output = null, text = null; var seconds = 30.0; var idle = 150.0; var controller = "http://127.0.0.1:5060";
            for (var i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length) throw new ArgumentException($"{args[i]} needs a value.");
                var value = args[i + 1];
                switch (args[i])
                {
                    case "--video": video = value; break;
                    case "--text": text = value; break;
                    case "--out": output = value; break;
                    case "--controller": controller = value; break;
                    case "--seconds": seconds = Number(args[i], value); break;
                    case "--idle": idle = Number(args[i], value); break;
                    default: throw new ArgumentException($"I do not know {args[i]}. The options are: --controller --video --seconds --text --idle --out.");
                }
            }
            if (video is null) throw new ArgumentException("--video is needed.");
            if (output is null) throw new ArgumentException("--out is needed.");
            if (!File.Exists(video)) throw new FileNotFoundException($"There is no file {video}.");
            var ffmpeg = Ffmpeg() ?? throw new InvalidOperationException("ffmpeg is not on this PC (the programme is split with it).");
            Directory.CreateDirectory(output);

            // TV SPLITTING: the pictures and the sound of the programme.
            tempFolder = Path.Combine(Path.GetTempPath(), "mpai-tma-app-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(tempFolder);
            var frames = Path.Combine(tempFolder, "frames"); Directory.CreateDirectory(frames);
            var span = seconds.ToString(CultureInfo.InvariantCulture);
            Console.WriteLine($"Splitting the programme (the first {seconds:F0} s)...");
            RunTool(ffmpeg, $"-v error -y -t {span} -i \"{video}\" -an -vf fps={Fps.ToString(CultureInfo.InvariantCulture)},scale=960:540 -q:v 4 \"{Path.Combine(frames, "f%05d.jpg")}\"");
            var wav = Path.Combine(tempFolder, "audio.wav");
            RunTool(ffmpeg, $"-v error -y -t {span} -i \"{video}\" -vn -ac 1 -ar 16000 -c:a pcm_s16le \"{wav}\"");
            var jpgs = Directory.GetFiles(frames, "*.jpg").Order(StringComparer.Ordinal).ToList();
            if (jpgs.Count == 0) throw new InvalidOperationException("The video has no pictures.");
            var played = new FileInfo(wav).Length / 32000.0;
            Console.WriteLine($"{jpgs.Count} pictures and {played:F1} s of sound.");

            var visual = new JsonObject
            {
                ["Header"] = "OSD-BVO-V1.5", ["MInstanceID"] = "tma-app", ["BasicVisualObjectID"] = Path.GetFileNameWithoutExtension(video),
                ["BasicVisualObjectData"] = new JsonArray(jpgs.Select(f => (JsonNode)new JsonObject { ["Data"] = Convert.ToBase64String(File.ReadAllBytes(f)) }).ToArray()),
                ["BasicVisualObjectProperties"] = new JsonObject { ["FrameRate"] = Fps }
            }.ToJsonString();
            var speech = new JsonObject
            {
                ["Header"] = "OSD-BSO-V1.5", ["MInstanceID"] = "tma-app", ["BasicSpeechObjectID"] = "programme-speech",
                ["BasicSpeechObjectData"] = new JsonArray(new JsonObject { ["Data"] = Convert.ToBase64String(File.ReadAllBytes(wav)) }),
                // The sound of a programme as TV Splitting gives it: its time is that of the programme, from 0.
                ["BasicSpeechObjectTime"] = new JsonObject
                {
                    ["Header"] = "OSD-STM-V1.5", ["SimpleTimeID"] = "programme",
                    ["SimpleTimeData"] = new JsonArray(new JsonObject { ["FlagsByte"] = 0, ["StartTime"] = 0.0, ["EndTime"] = played, ["AccuracyMode"] = "single", ["AccuracyPlusMinus"] = 0.0, ["TimeType"] = false, ["TimeUnit"] = "00" })
                }
            }.ToJsonString();
            string? auxiliary = text is null ? null : new JsonObject
            {
                ["Header"] = "OSD-TXO-V1.5", ["TextObjectID"] = "auxiliary",
                ["BasicTextObjects"] = new JsonArray(JsonNode.Parse(MpaiJson.ToJson(BasicTextObject.FromText(text)))!)
            }.ToJsonString();

            using var api = new RemoteControllerApi(controller);
            var events = new Events();
            var devices = new DeviceRegistry()
                .RegisterDevice("Programme", new Programme(auxiliary, visual, speech))
                .RegisterDevice("Events", events)
                .RegisterPresent("file", d => { if (d.TryGetValue("Prompt", out var said)) Console.WriteLine(said); return Task.CompletedTask; });

            using var done = new CancellationTokenSource();
            var interpreter = Task.Run(() =>
            {
                try { new WorkflowRun(api, Path.Combine(Root(), "Apps", "OSD", "TMA", "OSD-TMA.orch"), devices, Environment.GetEnvironmentVariable("MPAI_DIAG") == "1" ? Console.WriteLine : null).Run(done.Token); }
                catch (OperationCanceledException) { }
            });
            Console.WriteLine("Analysing (it takes a while)...");

            // The Event Descriptors grow as the scenes arrive and the last is the whole: the analysis is finished when no new one has come for --idle seconds.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!interpreter.IsCompleted)
            {
                Thread.Sleep(1000);
                if (events.Count > 0 && clock.Elapsed.TotalSeconds - events.LastAt > idle) break;
                if (events.Count == 0 && clock.Elapsed.TotalMinutes > 60) throw new InvalidOperationException("The Module gave no Event Descriptors in an hour.");
            }
            done.Cancel();
            try { interpreter.Wait(TimeSpan.FromSeconds(30)); } catch (AggregateException) { }
            if (!interpreter.IsCompleted || events.Last is null)
            {
                if (events.Last is null) throw new InvalidOperationException("The Module gave no Event Descriptors" + (interpreter.IsFaulted ? ": " + interpreter.Exception?.GetBaseException().Message : "."));
            }

            var file = Path.Combine(output, "event-descriptors.json");
            File.WriteAllText(file, events.Last!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"The Multimodal Event Descriptors are in {file}");
            Console.WriteLine(Summary(events.Last!));
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally { if (tempFolder is not null) try { Directory.Delete(tempFolder, true); } catch { } }
    }

    private static double Number(string name, string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : throw new ArgumentException($"{name} takes a number above 0.");

    // WHAT THE EVENT DESCRIPTORS SAY: the scenes, the utterances with their speaker and words, the faces.
    private static string Summary(JsonObject mev)
    {
        var scenes = mev["MultimodalSceneDescriptors"] as JsonArray ?? [];
        string Words(JsonNode? n)
        {
            if (n is JsonObject o)
            {
                if (o["Data"] is JsonValue v && v.TryGetValue<string>(out var s)) return s;
                foreach (var (_, child) in o) { var found = Words(child); if (found.Length > 0) return found; }
            }
            else if (n is JsonArray a) foreach (var child in a) { var found = Words(child); if (found.Length > 0) return found; }
            return "";
        }
        var utterances = scenes.Select(x => x?["BasicMultimodalSceneDescriptorsData"]?[0]?["BXSOrBXSID"]?["BasicSpeechSceneDescriptors"]?[0]?["ObjectIDOrObject"]?[0]).Where(o => o is not null).ToList();
        var lines = new List<string>
        {
            $"{scenes.Count} scenes: " +
            $"{scenes.Count(x => x?["BasicMultimodalSceneDescriptorsData"]?[0]?["BXSOrBXSID"]?["BasicSpeechSceneDescriptors"] is not null)} of speech, " +
            $"{scenes.Count(x => x?["BasicMultimodalSceneDescriptorsData"]?[0]?["BXSOrBXSID"]?["BasicVisualSceneDescriptors"] is not null)} of pictures."
        };
        foreach (var u in utterances)
        {
            var who = Words(u!["BasicSpeechDataQualifier"]?["Attributes"]?["Metadata"]?["SpeakerIdentity"]);
            var said = Words(u["BasicSpeechDataQualifier"]?["Attributes"]?["Metadata"]?["ContentDescription"]?["TextObject"]);
            lines.Add($"  {(who.Length > 0 ? who : "someone")}: {(said.Length > 0 ? said : "(no words)")}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    // THE DEVICE THE WORKFLOW NAMES "Programme": TV Splitting's stand-in. It gives the auxiliary text (if there is one), the pictures and the sound of the programme, each once, to the Ports
    // the workflow streams them to, and then waits until it is cancelled.
    private sealed class Programme(string? auxiliary, string visual, string speech) : IDevice
    {
        public Task DeliverAsync(string dataType, string json, CancellationToken cancel) => Task.CompletedTask;

        public async IAsyncEnumerable<(string DataType, string Json)> ReadAsync([EnumeratorCancellation] CancellationToken cancel)
        {
            if (auxiliary is not null) yield return ("OSD-TXO-V1.5", auxiliary);
            yield return ("OSD-BVO-V1.5", visual);
            yield return ("OSD-BSO-V1.5", speech);
            try { await Task.Delay(Timeout.Infinite, cancel); } catch (OperationCanceledException) { }
        }

        public Task SafeStopAsync() => Task.CompletedTask;
    }

    // THE DEVICE THE WORKFLOW NAMES "Events": it is handed each Multimodal Event Descriptors the Module gives; the last is the whole.
    private sealed class Events : IDevice
    {
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        public JsonObject? Last { get; private set; }
        public int Count { get; private set; }
        public double LastAt { get; private set; }

        public Task DeliverAsync(string dataType, string json, CancellationToken cancel)
        {
            Last = JsonNode.Parse(json) as JsonObject; Count++; LastAt = clock.Elapsed.TotalSeconds;
            Console.WriteLine($"  Event Descriptors {Count}: {(Last?["MultimodalSceneDescriptors"] as JsonArray)?.Count ?? 0} scenes");
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<(string DataType, string Json)> ReadAsync([EnumeratorCancellation] CancellationToken cancel)
        {
            try { await Task.Delay(Timeout.Infinite, cancel); } catch (OperationCanceledException) { }
            yield break;
        }

        public Task SafeStopAsync() => Task.CompletedTask;
    }

    // The folder of the software: the one that holds AIMs/AMDs.
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "AIMs", "AMDs"))) return d.FullName;
        throw new InvalidOperationException("The folder of the software (the one with AIMs/AMDs) is not above " + AppContext.BaseDirectory);
    }

    // ffmpeg: on the PATH, else where winget puts it.
    private static string? Ffmpeg()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (dir.Length > 0 && File.Exists(Path.Combine(dir, "ffmpeg.exe"))) return Path.Combine(dir, "ffmpeg.exe");
        var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        return Directory.Exists(winget) ? Directory.EnumerateFiles(winget, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault() : null;
    }

    private static void RunTool(string exe, string arguments)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, arguments) { RedirectStandardError = true, UseShellExecute = false })!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(exe)} failed: {err}");
    }
}
