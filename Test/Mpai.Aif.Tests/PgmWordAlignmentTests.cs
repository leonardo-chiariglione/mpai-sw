using System.Diagnostics;
using System.Text.Json.Nodes;

using Mpai.Pgm;

namespace Mpai.Aif.Tests;

// THE WORDS OF THE MACHINE SPEECH ALIGNED WITH THE SPEECH ITSELF (N-31, night 2026-11-18): Behaviour Planning times the Behaviours on the words; the words are now placed on the
// stretches of speech of the signal (silence before and after given to no word, each pause between two words), no longer on the duration alone.
[Trait("Group", "Fast")]
public class PgmWordAlignmentTests
{
    private static short[] Bursts(int rate, params (double From, double To)[] bursts)
    {
        var total = bursts.Max(b => b.To) + 0.3;
        var s = new short[(int)(total * rate)];
        foreach (var (from, to) in bursts)
            for (var i = (int)(from * rate); i < (int)(to * rate); i++) s[i] = (short)(8000 * Math.Sin(2 * Math.PI * 180 * i / rate) * (1 + 0.3 * Math.Sin(2 * Math.PI * 4 * i / rate)));
        return s;
    }

    // Three words spoken as three stretches with pauses between them, silence before and after: each word lies in its stretch.
    [Fact]
    public void EachWordLiesInItsStretchOfSpeech()
    {
        var bursts = new[] { (0.25, 0.65), (0.95, 1.55), (1.9, 2.3) };
        var times = Communicative.WordTimes("one, seventeen, two.", Bursts(16000, bursts), 16000);
        Assert.Equal(3, times.Length);
        for (var i = 0; i < 3; i++)
        {
            Assert.InRange(times[i].Start, bursts[i].Item1 - 0.03, bursts[i].Item1 + 0.03);
            Assert.InRange(times[i].End, bursts[i].Item2 - 0.03, bursts[i].Item2 + 0.03);
        }
    }

    // More pauses than word boundaries, and a speech object Behaviour Planning reads (a WAV file in its Data): every word still has a time, in order, within the speech.
    [Fact]
    public void MorePausesThanWordsAndASpeechObjectStillGiveEveryWordInOrder()
    {
        var samples = Bursts(22050, (0.2, 0.4), (0.7, 0.9), (1.2, 1.4), (1.7, 1.9));
        var wav = new Mpai.Osd.Speech.Audio(samples, 22050).ToWav();
        var speech = new JsonObject { ["Header"] = "OSD-BSO-V1.5", ["Data"] = Convert.ToBase64String(wav) };
        var read = Communicative.SamplesOf(speech, out var rate);
        Assert.Equal(22050, rate);
        Assert.Equal(samples.Length, read!.Length);
        var times = Communicative.WordTimes("hello there", read, rate);
        Assert.Equal(2, times.Length);
        Assert.True(times[0].Start >= 0.17 && times[0].End <= times[1].Start + 1e-9 && times[1].End <= 1.95, string.Join(" ", times));
        Assert.Empty(Communicative.WordTimes("", read, rate));
    }
}

// The alignment against Whisper's word times on speech of a Piper voice (Models): the boundaries of the words are nearer Whisper's than those of the letters-only sharing.
[Trait("Group", "Models")]
public class PgmWordAlignmentModelTests
{
    public static readonly string[] Sentences =
    [
        "Hello! I will open the door for you, and then I will come back.",
        "The lamp is on the table, next to the window.",
        "No, stop. Wait a moment, please.",
        "Good evening, welcome to our home. Come in and sit down.",
        "I am sorry, I did not understand. Could you say it again?",
        "The door is now open."
    ];

    [SkippableFact]
    public void TheAlignmentIsNearerWhisperThanTheLettersAlone()
    {
        var root = Repository.Root;
        var piper = Path.Combine(root, "Models", "Piper", "piper_windows_amd64", "piper", "piper.exe");
        var voice = Path.Combine(root, "Models", "Piper", "voices", "en_US-amy-medium", "en_US-amy-medium.onnx");
        var whisper = Path.Combine(root, "Models", "Whisper", "bin", "whisper-cli.exe");
        var model = Path.Combine(root, "Models", "Whisper", "models", "ggml-small.bin");
        Skip.IfNot(File.Exists(piper) && File.Exists(voice) && File.Exists(whisper) && File.Exists(model), "Piper, the amy voice or Whisper is not installed");
        var work = Path.Combine(Path.GetTempPath(), "pgm-align-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(work);
        double aligned = 0, letters = 0; var n = 0;
        try
        {
            foreach (var (sentence, k) in Sentences.Select((s, k) => (s, k)))
            {
                var wav = Path.Combine(work, $"s{k}.wav");
                Run(piper, ["--model", voice, "--output_file", wav], sentence);
                var audio = Mpai.Osd.Speech.Audio.FromWav(File.ReadAllBytes(wav));
                var wav16 = Path.Combine(work, $"s{k}-16k.wav"); File.WriteAllBytes(wav16, audio.At(16000).ToWav());
                Run(whisper, ["-m", model, "-f", wav16, "-l", "en", "-ml", "1", "-sow", "-oj", "-of", Path.Combine(work, $"s{k}"), "-np"], null);
                var heard = JsonNode.Parse(File.ReadAllText(Path.Combine(work, $"s{k}.json")))!["transcription"]!.AsArray()
                    .Select(t => ((string)t!["text"]!).Trim()).Zip(JsonNode.Parse(File.ReadAllText(Path.Combine(work, $"s{k}.json")))!["transcription"]!.AsArray(),
                        (w, t) => (Word: w, From: (double)t!["offsets"]!["from"]! / 1000, To: (double)t["offsets"]!["to"]! / 1000))
                    .Where(t => t.Word.Any(char.IsLetterOrDigit)).ToList();
                var words = Communicative.Words(sentence);
                if (heard.Count != words.Length) continue;                          // Whisper split the words otherwise: not comparable
                var a = Communicative.WordTimes(sentence, audio.Samples, audio.Rate);
                var l = Communicative.WordTimes(sentence, audio.Seconds);
                for (var i = 0; i < words.Length; i++)
                {
                    aligned += Math.Abs(a[i].Start - heard[i].From); letters += Math.Abs(l[i].Start - heard[i].From); n++;
                }
            }
        }
        finally { try { Directory.Delete(work, true); } catch { } }
        Assert.True(n >= 20, $"only {n} words could be compared");
        Console.WriteLine($"mean distance of the word starts from Whisper's: aligned {aligned / n:0.000} s, letters alone {letters / n:0.000} s, over {n} words");
        Assert.True(aligned < letters, $"aligned {aligned / n:0.000} s is not nearer than letters alone {letters / n:0.000} s");
    }

    private static void Run(string exe, IEnumerable<string> args, string? input)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        if (input is not null) p.StandardInput.WriteLine(input);
        p.StandardInput.Close();
        var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(TimeSpan.FromMinutes(2))) { p.Kill(true); throw new TimeoutException(exe); }
        _ = o.Result; _ = e.Result;
    }
}
