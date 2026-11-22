using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Mpai.Mas.Client;
using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.PhysicalLayer;
using Mpai.Ua;

namespace Mpai.Mmc.Cas.App;

// CONVERSATION ABOUT A SCENE, the App.
//
//   CasApp [--controller http://127.0.0.1:5061] (--camera | --scene scene.jpg ...) (--microphone [--turns 3] | --speech said.wav ...) --out folder
//
// --camera      the scene is what the Camera Unit of the Physical Layer sees, each turn
// --scene       a picture of the scene, instead of the camera. One for each turn; the last one serves the turns that remain
// --microphone  what the person says about the scene is what the Microphone Unit hears: it waits for speech and ends when the speaker stops. Say nothing and the conversation ends
// --turns       how many turns the conversation has at most when the microphone is used (3 by default)
// --speech      what the person says in a turn, a WAV file (16-bit PCM), instead of the microphone. Each is one turn, in the order given
// --controller  the Controller this User Agent talks to (CasController, started by whoever launches the App)
// --out         the folder where the Portable Avatar that answers each turn is written (avatar-1.json, avatar-2.json ...)
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var scenes = new List<string>(); var files = new List<string>(); string? output = null; var controller = "http://127.0.0.1:5061";
            var camera = false; var microphone = false; var turnLimit = 3;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--camera": camera = true; continue;
                    case "--microphone": microphone = true; continue;
                }
                if (i + 1 >= args.Length) throw new ArgumentException($"{args[i]} needs a value.");
                var value = args[++i];
                switch (args[i - 1])
                {
                    case "--scene": scenes.Add(value); break;
                    case "--speech": files.Add(value); break;
                    case "--out": output = value; break;
                    case "--controller": controller = value; break;
                    case "--turns": turnLimit = int.TryParse(value, out var n) && n > 0 ? n : throw new ArgumentException("--turns takes a number above 0."); break;
                    default: throw new ArgumentException($"I do not know {args[i - 1]}. The options are: --controller --camera --scene --microphone --turns --speech --out.");
                }
            }
            if (!camera && scenes.Count == 0) throw new ArgumentException("--camera or --scene is needed.");
            if (camera && scenes.Count > 0) throw new ArgumentException("--camera and --scene are alternatives.");
            if (!microphone && files.Count == 0) throw new ArgumentException("--microphone or --speech is needed.");
            if (microphone && files.Count > 0) throw new ArgumentException("--microphone and --speech are alternatives.");
            if (output is null) throw new ArgumentException("--out is needed.");
            foreach (var f in scenes.Concat(files)) if (!File.Exists(f)) throw new FileNotFoundException($"There is no file {f}.");
            Directory.CreateDirectory(output);
            var total = microphone ? turnLimit : files.Count;

            using var api = new RemoteControllerApi(controller);

            // THE SAME WORKFLOW AS FOR ANY CLIENT: the scene, then what the person says about it, turn after turn. The scene comes from the Camera Unit and the speech from the Microphone Unit, or both
            // from files; the source of what does not come waits until the workflow abandons it. The workflow repeats until it is stopped: the App stops it when every turn is answered, or
            // when the person says nothing.
            var next = 0; var answered = 0; var silent = false; var nextLock = new object();
            using var done = new CancellationTokenSource();
            var cameraUnit = new CameraUnit(); var microphoneUnit = new MicrophoneUnit();
            var devices = new DeviceRegistry()
                .RegisterAcquire("OSD-BVO-V1.5", async (bool viaVad, string? wanted, CancellationToken abandon) =>
                {
                    int turn; lock (nextLock) turn = next;
                    if (turn < total && !silent)
                    {
                        if (camera)
                        {
                            Console.WriteLine("Looking at the scene...");
                            var seen = await cameraUnit.AcquireFaceAsync() ?? throw new InvalidOperationException("The camera gave nothing: is there a camera on this PC, and may this program use it?");
                            return MpaiJson.ToJson(seen);
                        }
                        var path = scenes[Math.Min(turn, scenes.Count - 1)];
                        return MpaiJson.ToJson(BasicVisualObject.FromFile(Path.GetFileName(path), File.ReadAllBytes(path)));
                    }
                    try { await Task.Delay(Timeout.Infinite, abandon); } catch (OperationCanceledException) { }
                    return null;
                })
                .RegisterAcquire("OSD-BAO-V1.5", async (bool viaVad, string? wanted, CancellationToken abandon) =>
                {
                    int turn; lock (nextLock) turn = next;
                    if (turn < total && !silent)
                    {
                        if (microphone)
                        {
                            Console.WriteLine("Say something about the scene (I stop when you say nothing)...");
                            var heard = await microphoneUnit.AcquireSpeechAsync(cancel: abandon);
                            if (heard.Data.Length == 0)
                            {
                                Console.WriteLine("I heard nothing: the conversation ends.");
                                silent = true; done.Cancel();
                                return null;
                            }
                            lock (nextLock) next++;
                            return Microphone(heard.Data, microphoneUnit.SamplingFrequency);
                        }
                        lock (nextLock) { var wav = File.ReadAllBytes(files[next++]); return Microphone(wav); }
                    }
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
                        if (answered == total) done.Cancel();
                    }
                    return Task.CompletedTask;
                });
            Console.WriteLine("Talking...");
            try { new WorkflowRun(api, Path.Combine(Root(), "Apps", "MMC", "CAS", "MMC-CAS.orch"), devices, Environment.GetEnvironmentVariable("MPAI_DIAG") == "1" ? Console.WriteLine : null).Run(done.Token); }
            catch (OperationCanceledException) when (answered == total || (silent && answered > 0)) { }
            catch (OperationCanceledException) when (silent) { throw new InvalidOperationException("Nothing was said."); }

            if (answered < total && !silent) throw new InvalidOperationException($"The Module answered {answered} of {total} turns.");
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
        if (string.IsNullOrWhiteSpace(text)) return "  The machine did not understand what was said, and gave no answer.";
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
        return Microphone(pcm, rate);
    }

    // What the Microphone Unit heard (raw 16-bit PCM, mono) as the Basic Audio Object a microphone gives.
    private static string Microphone(byte[] pcm, int rate)
    {
        var qualifier = new AudioQualifier
        {
            AudioQualifierID = Guid.NewGuid().ToString(),
            Formats = new AudioFormats { ContentFormat = new AudioContentFormat { RawData = new AudioRawData { SampleSpace = new Pcm { SamplingFrequency = rate, Precision = 16 } } } },
            Attributes = new AudioAttributes { Source = "Real", Device = new AudioDevice { DeviceRole = "Capture", DeviceType = "Microphone", CaptureConfiguration = new CaptureConfiguration { ChannelCount = 1, SamplingMode = "Mono" } } }
        };
        return MpaiJson.ToJson(BasicAudioObject.FromData(pcm, qualifier));
    }
}
