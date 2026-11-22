using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Mpai.Mas.Client;
using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Ua;

namespace Mpai.Mmc.Cps.App;

// CONVERSATION WITH PERSONAL STATUS, the App.
//
//   CpsApp [--controller http://127.0.0.1:5059] --face face.jpg [--face other.jpg ...] (--text "words" | --speech said.wav) [(--text ... | --speech ...) ...] --out folder
//
// --face      a picture of the face of the person: the camera's. One for each turn; the last one serves the turns that remain
// --text      what the person types in a turn
// --speech    what the person says in a turn, a WAV file (16-bit PCM)
// --controller the Controller this User Agent talks to (CpsController, started by whoever launches the App)
// --out       the folder where the Portable Avatar that answers each turn is written (avatar-1.json, avatar-2.json ...)
//
// The turns are the --text and --speech given, in the order they are given.
internal static class Program
{

    private static int Main(string[] args)
    {
        try
        {
            var faces = new List<string>(); var turns = new List<(bool Speech, string Value)>(); string? output = null; var controller = "http://127.0.0.1:5059";
            for (var i = 0; i < args.Length; i++)
            {
                if (i + 1 >= args.Length) throw new ArgumentException($"{args[i]} needs a value.");
                var value = args[i + 1];
                switch (args[i])
                {
                    case "--face": faces.Add(value); break;
                    case "--text": turns.Add((false, value)); break;
                    case "--speech": turns.Add((true, value)); break;
                    case "--out": output = value; break;
                    case "--controller": controller = value; break;
                    default: throw new ArgumentException($"I do not know {args[i]}. The options are: --controller --face --text --speech --out.");
                }
                i++;
            }
            if (faces.Count == 0) throw new ArgumentException("--face is needed.");
            if (turns.Count == 0) throw new ArgumentException("--text or --speech is needed.");
            if (output is null) throw new ArgumentException("--out is needed.");
            foreach (var f in faces) if (!File.Exists(f)) throw new FileNotFoundException($"There is no file {f}.");
            foreach (var t in turns) if (t.Speech && !File.Exists(t.Value)) throw new FileNotFoundException($"There is no file {t.Value}.");
            Directory.CreateDirectory(output);

            using var api = new RemoteControllerApi(controller);

            // THE SAME WORKFLOW AS FOR ANY CLIENT: the face, then what the person says, turn after turn. A stand-alone App has no camera and no microphone: the face is a picture, what is said a file
            // or typed words; the source of what does not come waits until the workflow abandons it. The workflow repeats until it is stopped: the App stops it when every turn is answered.
            var next = 0; var answered = 0; var nextLock = new object();
            using var done = new CancellationTokenSource();
            var devices = new DeviceRegistry()
                .RegisterAcquire("OSD-BVO-V1.5", async (bool viaVad, string? wanted, CancellationToken abandon) =>
                {
                    int turn; lock (nextLock) turn = next;
                    if (turn < turns.Count) { var path = faces[Math.Min(turn, faces.Count - 1)]; return MpaiJson.ToJson(BasicVisualObject.FromFile(Path.GetFileName(path), File.ReadAllBytes(path), "Face")); }
                    try { await Task.Delay(Timeout.Infinite, abandon); } catch (OperationCanceledException) { }
                    return null;
                })
                .RegisterAcquire("OSD-BAO-V1.5", async (bool viaVad, string? wanted, CancellationToken abandon) =>
                {
                    lock (nextLock) if (next < turns.Count && turns[next].Speech) return Microphone(File.ReadAllBytes(turns[next++].Value));
                    try { await Task.Delay(Timeout.Infinite, abandon); } catch (OperationCanceledException) { }
                    return null;
                })
                .RegisterAcquire("OSD-BTO-V1.5", async (bool viaVad, string? wanted, CancellationToken abandon) =>
                {
                    lock (nextLock) if (next < turns.Count && !turns[next].Speech) return MpaiJson.ToJson(BasicTextObject.FromText(turns[next++].Value));
                    try { await Task.Delay(Timeout.Infinite, abandon); } catch (OperationCanceledException) { }
                    return null;
                })
                .RegisterPresent("file", d =>
                {
                    if (d.TryGetValue("Prompt", out var said)) Console.WriteLine(said);
                    if (d.TryGetValue("PAF-PAV-V1.6", out var avatar))
                    {
                        answered++;
                        var file = Path.Combine(output, $"avatar-{answered}.json");
                        File.WriteAllText(file, avatar);
                        Console.WriteLine($"Turn {answered}: the Portable Avatar is in {file}");
                        Console.WriteLine(Said(avatar));
                        if (answered == turns.Count) done.Cancel();
                    }
                    return Task.CompletedTask;
                });
            Console.WriteLine("Talking...");
            try { new WorkflowRun(api, Path.Combine(Root(), "Apps", "MMC", "CPS", "MMC-CPS.orch"), devices, Environment.GetEnvironmentVariable("MPAI_DIAG") == "1" ? Console.WriteLine : null).Run(done.Token); }
            catch (OperationCanceledException) when (answered == turns.Count) { }

            if (answered < turns.Count) throw new InvalidOperationException($"The Module answered {answered} of {turns.Count} turns.");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    // What the Portable Avatar says, and how it feels: its Text Object and its Entity State.
    private static string Said(string avatar)
    {
        var o = System.Text.Json.Nodes.JsonNode.Parse(avatar);
        var text = o?["TextObject"]?["BasicTextData"]?[0]?["Data"]?.GetValue<string>();
        var feeling = $"{o?["EntityState"]?["Emotion"]?["GeneralAdjectival"]}, {o?["EntityState"]?["SocialAttitude"]?["GeneralAdjectival"]}".Trim(' ', ',');
        return $"  The machine says: {text}" + (feeling.Length > 0 ? $"{Environment.NewLine}  It feels: {feeling}" : "");
    }

    // The folder of the software: the one that holds AIMs/AMDs.
    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "AIMs", "AMDs"))) return d.FullName;
        throw new InvalidOperationException("The folder of the software (the one with AIMs/AMDs) is not above " + AppContext.BaseDirectory);
    }

    // What a microphone gives, from a 16-bit PCM WAV file: a Basic Audio Object (mono, as captured).
    private static string Microphone(byte[] wav)
    {
        var rate = BitConverter.ToInt32(wav, 24); var pcm = wav[44..];
        for (var pos = 12; pos + 8 <= wav.Length;)
        {
            var length = BitConverter.ToInt32(wav, pos + 4);
            if (System.Text.Encoding.ASCII.GetString(wav, pos, 4) == "data") { pcm = wav[(pos + 8)..Math.Min(wav.Length, pos + 8 + length)]; break; }
            pos += 8 + length + (length & 1);
        }
        var qualifier = new AudioQualifier
        {
            AudioQualifierID = Guid.NewGuid().ToString(),
            Formats = new AudioFormats { ContentFormat = new AudioContentFormat { RawData = new AudioRawData { SampleSpace = new Pcm { SamplingFrequency = rate, Precision = 16 } } } },
            Attributes = new AudioAttributes { Source = "Real", Device = new AudioDevice { DeviceRole = "Capture", DeviceType = "Microphone", CaptureConfiguration = new CaptureConfiguration { ChannelCount = 1, SamplingMode = "Mono" } } }
        };
        return MpaiJson.ToJson(BasicAudioObject.FromData(pcm, qualifier));
    }
}
