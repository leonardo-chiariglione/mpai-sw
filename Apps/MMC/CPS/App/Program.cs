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

namespace Mpai.Mmc.Cps.App;

// CONVERSATION WITH PERSONAL STATUS, the App.
//
//   CpsApp [--controller http://127.0.0.1:5059] (--camera | --face face.jpg ...) (--microphone [--turns 3] | (--text "words" | --speech said.wav) ...) --out folder
//
// --camera      the face is what the Camera Unit of the Physical Layer sees, each turn
// --face        a picture of the face of the person, instead of the camera. One for each turn; the last one serves the turns that remain
// --microphone  what the person says is what the Microphone Unit hears: it waits for speech and ends when the speaker stops. Say nothing and the conversation ends
// --turns       how many turns the conversation has at most when the microphone is used (3 by default)
// --text        what the person types in a turn, instead of the microphone
// --speech      what the person says in a turn, a WAV file (16-bit PCM), instead of the microphone
// --controller  the Controller this User Agent talks to (CpsController, started by whoever launches the App)
// --quiet       the machine does not say its answer aloud (the speech is still made and kept as answer-N.wav)
// --out         the folder where the Portable Avatar that answers each turn is written (avatar-1.json, avatar-2.json ...)
//
// Without the microphone the turns are the --text and --speech given, in the order they are given.
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var faces = new List<string>(); var turns = new List<(bool Speech, string Value)>(); string? output = null; var controller = "http://127.0.0.1:5059";
            var camera = false; var microphone = false; var quiet = false; var turnLimit = 3;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--camera": camera = true; continue;
                    case "--microphone": microphone = true; continue;
                    case "--quiet": quiet = true; continue;
                }
                if (i + 1 >= args.Length) throw new ArgumentException($"{args[i]} needs a value.");
                var value = args[++i];
                switch (args[i - 1])
                {
                    case "--face": faces.Add(value); break;
                    case "--text": turns.Add((false, value)); break;
                    case "--speech": turns.Add((true, value)); break;
                    case "--out": output = value; break;
                    case "--controller": controller = value; break;
                    case "--turns": turnLimit = int.TryParse(value, out var n) && n > 0 ? n : throw new ArgumentException("--turns takes a number above 0."); break;
                    default: throw new ArgumentException($"I do not know {args[i - 1]}. The options are: --controller --camera --face --microphone --turns --text --speech --quiet --out.");
                }
            }
            if (!camera && faces.Count == 0) throw new ArgumentException("--camera or --face is needed.");
            if (camera && faces.Count > 0) throw new ArgumentException("--camera and --face are alternatives.");
            if (!microphone && turns.Count == 0) throw new ArgumentException("--microphone, --text or --speech is needed.");
            if (microphone && turns.Count > 0) throw new ArgumentException("--microphone and --text or --speech are alternatives.");
            if (output is null) throw new ArgumentException("--out is needed.");
            foreach (var f in faces) if (!File.Exists(f)) throw new FileNotFoundException($"There is no file {f}.");
            foreach (var t in turns) if (t.Speech && !File.Exists(t.Value)) throw new FileNotFoundException($"There is no file {t.Value}.");
            Directory.CreateDirectory(output);
            var total = microphone ? turnLimit : turns.Count;

            using var api = new RemoteControllerApi(controller);
            using var voice = new Mpai.Mmc.Voice.MachineVoice(api, "MPAI as a Service");           // the machine's voice: the Module that makes speech from the words of the answer

            // THE SAME WORKFLOW AS FOR ANY CLIENT: the face, then what the person says, turn after turn. The face comes from the Camera Unit and the speech from the Microphone Unit, or from files or
            // typed words; the source of what does not come waits until the workflow abandons it. The workflow repeats until it is stopped: the App stops it when every turn is answered, or
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
                            Console.WriteLine("Looking at you...");
                            var seen = await cameraUnit.AcquireFaceAsync() ?? throw new InvalidOperationException("The camera gave nothing: is there a camera on this PC, and may this program use it?");
                            return MpaiJson.ToJson(seen);
                        }
                        var path = faces[Math.Min(turn, faces.Count - 1)];
                        return MpaiJson.ToJson(BasicVisualObject.FromFile(Path.GetFileName(path), File.ReadAllBytes(path), "Face"));
                    }
                    try { await Task.Delay(Timeout.Infinite, abandon); } catch (OperationCanceledException) { }
                    return null;
                })
                .RegisterAcquire("OSD-BAO-V1.5", async (bool viaVad, string? wanted, CancellationToken abandon) =>
                {
                    if (microphone)
                    {
                        int turn; lock (nextLock) turn = next;
                        if (turn < total && !silent)
                        {
                            Console.WriteLine("Say something (I stop when you say nothing)...");
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
                    }
                    else lock (nextLock) if (next < turns.Count && turns[next].Speech) return Microphone(File.ReadAllBytes(turns[next++].Value));
                    try { await Task.Delay(Timeout.Infinite, abandon); } catch (OperationCanceledException) { }
                    return null;
                })
                .RegisterAcquire("OSD-BTO-V1.5", async (bool viaVad, string? wanted, CancellationToken abandon) =>
                {
                    if (!microphone) lock (nextLock) if (next < turns.Count && !turns[next].Speech) return MpaiJson.ToJson(BasicTextObject.FromText(turns[next++].Value));
                    try { await Task.Delay(Timeout.Infinite, abandon); } catch (OperationCanceledException) { }
                    return null;
                })
                .RegisterPresent("file", async d =>
                {
                    if (d.TryGetValue("Prompt", out var said)) Console.WriteLine(said);
                    if (d.TryGetValue("PAF-PAV-V1.6", out var avatar))
                    {
                        answered++;
                        var file = Path.Combine(output, $"avatar-{answered}.json");
                        File.WriteAllText(file, avatar);
                        Console.WriteLine($"Turn {answered}: the Portable Avatar is in {file}");
                        Console.WriteLine(Said(avatar));
                        // THE MACHINE SAYS ITS ANSWER: the words of the Portable Avatar are spoken (and the speech kept as answer-N.wav).
                        if (Mpai.Aif.Api.AvatarUtterance.TextOfPortableAvatar(avatar) is { } words)
                            if (await voice.SayAsync(words, Path.Combine(output, $"answer-{answered}.wav"), play: !quiet)) Console.WriteLine($"  Spoken: answer-{answered}.wav");
                        if (answered == total) done.Cancel();
                    }
                });
            Console.WriteLine("Talking...");
            try { new WorkflowRun(api, Path.Combine(Root(), "Apps", "MMC", "CPS", "MMC-CPS.orch"), devices, Environment.GetEnvironmentVariable("MPAI_DIAG") == "1" ? Console.WriteLine : null).Run(done.Token); }
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
