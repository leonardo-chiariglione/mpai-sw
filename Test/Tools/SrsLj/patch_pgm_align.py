"""N-31 (night 2026-11-18): Behaviour Planning times the words on the Machine Speech itself (silences and pauses of the signal), not on its duration alone."""
from pathlib import Path

P = Path(r"D:\AI\AIMs\PGM\V1.0\Communicative.cs")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")

OLD = "    // The start and end of each word of the Text in the Machine Speech. THE ALIGNMENT OF THE REFERENCE SOFTWARE:"
NEW = r'''    // The samples of a Speech Object (16-bit PCM, mono): from a WAV file in its Data (its own rate), or raw PCM at the Qualifier's sampling frequency; null if it is neither.
    public static short[]? SamplesOf(JsonObject? speech, out int rate)
    {
        rate = 0;
        if (speech is null) return null;
        byte[] data;
        try
        {
            data = speech["Data"] is JsonValue v && v.TryGetValue<string>(out var b64) ? Convert.FromBase64String(b64)
                 : speech["BasicSpeechData"]?[0]?["Data"] is JsonValue w && w.TryGetValue<string>(out var b) ? Convert.FromBase64String(b)
                 : speech["BasicSpeechObjectData"]?[0]?["Data"] is JsonValue x && x.TryGetValue<string>(out var c) ? Convert.FromBase64String(c) : [];
        }
        catch (FormatException) { return null; }
        if (data.Length < 4) return null;
        int start, channels = 1, bits = 16, length;
        if (data.Length > 44 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F')
        {
            var at = 12; start = -1; length = 0;
            while (at + 8 <= data.Length)
            {
                var id = System.Text.Encoding.ASCII.GetString(data, at, 4); var size = BitConverter.ToInt32(data, at + 4);
                if (id == "fmt ") { channels = BitConverter.ToInt16(data, at + 10); rate = BitConverter.ToInt32(data, at + 12); bits = BitConverter.ToInt16(data, at + 22); }
                else if (id == "data") { start = at + 8; length = Math.Min(size, data.Length - start); break; }
                at += 8 + size + (size & 1);
            }
            if (start < 0 || bits != 16 || channels < 1 || rate <= 0) return null;
        }
        else
        {
            rate = (int)(Find(speech, "SamplingFrequency") ?? 0);
            bits = (int)(Find(speech, "Precision") ?? Find(speech, "SamplePrecision") ?? 16);
            if (rate <= 0 || bits != 16) return null;
            start = 0; length = data.Length;
        }
        var frames = length / (2 * channels);
        var samples = new short[frames];
        for (var i = 0; i < frames; i++) samples[i] = BitConverter.ToInt16(data, start + i * 2 * channels);
        return samples;
    }

    // THE WORDS ALIGNED WITH THE MACHINE SPEECH ITSELF (M3308 Q4: the word times are computed in A-User Formation). The speech is read as 10 ms frames: where its level is far below its
    // loud part it is silent. The silence before the first word and after the last is given to no word; each pause inside the speech (120 ms or more of silence) falls between two words -
    // the word boundary whose share of the letters before it is closest to the share of the spoken time before the pause, a boundary after a punctuation mark preferred; within each
    // stretch of speech between pauses the time is shared among its words in proportion to their letters. Where the speech cannot be read, its duration is shared by the letters alone.
    public static (double Start, double End)[] WordTimes(string text, short[] samples, int rate)
    {
        var words = Words(text);
        if (words.Length == 0) return [];
        if (samples.Length == 0 || rate <= 0) return WordTimes(text, 0.5);
        var hop = Math.Max(1, rate / 100);
        var count = samples.Length / hop;
        if (count < 3) return WordTimes(text, samples.Length / (double)rate);
        var db = new double[count];
        for (var f = 0; f < count; f++)
        {
            double sum = 0;
            for (var i = f * hop; i < (f + 1) * hop; i++) { var v = samples[i] / 32768.0; sum += v * v; }
            db[f] = 10 * Math.Log10(sum / hop + 1e-12);
        }
        var loud = db.OrderByDescending(d => d).Skip(Math.Min(count - 1, count / 20)).First();      // the 95th percentile, not a click
        var threshold = Math.Max(-50, loud - 30);
        var voiced = db.Select(d => d > threshold).ToArray();
        var first = Array.IndexOf(voiced, true); var last = Array.LastIndexOf(voiced, true);
        if (first < 0) return WordTimes(text, samples.Length / (double)rate);
        // gaps shorter than 80 ms are inside words (stops, closures)
        var gap = 0;
        for (var f = first; f <= last; f++)
        {
            if (!voiced[f]) { gap++; continue; }
            if (gap > 0 && gap < 8) for (var k = f - gap; k < f; k++) voiced[k] = true;
            gap = 0;
        }
        // the stretches of speech between pauses of 120 ms or more
        var stretches = new List<(int From, int To)>();
        var from = first; gap = 0;
        for (var f = first; f <= last; f++)
        {
            if (!voiced[f]) { gap++; continue; }
            if (gap >= 12) { stretches.Add((from, f - gap)); from = f; }
            gap = 0;
        }
        stretches.Add((from, last + 1));
        var cumulative = new double[words.Length + 1];
        for (var i = 0; i < words.Length; i++) cumulative[i + 1] = cumulative[i] + words[i].Count(char.IsLetterOrDigit) + 1;
        double spoken = stretches.Sum(s => s.To - s.From);
        // each pause between stretch k and k+1 goes to a word boundary b (1..N-1), in order; a pause that finds no boundary left joins the stretches around it
        var bounds = new List<int> { 0 };
        var groups = new List<(int From, int To)> { stretches[0] };
        double before = 0;
        for (var k = 0; k + 1 < stretches.Count; k++)
        {
            before += stretches[k].To - stretches[k].From;
            var share = before / spoken;
            int best = -1; var bestCost = double.MaxValue;
            for (var b = bounds[^1] + 1; b < words.Length; b++)
            {
                var cost = Math.Abs(cumulative[b] / cumulative[^1] - share) - (",.;:!?".Contains(words[b - 1][^1]) ? 0.05 : 0);
                if (cost < bestCost) { bestCost = cost; best = b; }
            }
            if (best < 0) { groups[^1] = (groups[^1].From, stretches[k + 1].To); continue; }
            bounds.Add(best); groups.Add(stretches[k + 1]);
        }
        bounds.Add(words.Length);
        var times = new (double, double)[words.Length];
        for (var k = 0; k < groups.Count; k++)
        {
            var (a, z) = groups[k];
            int w0 = bounds[k], w1 = bounds[k + 1];
            var total = cumulative[w1] - cumulative[w0];
            for (var i = w0; i < w1; i++)
                times[i] = (Math.Round((a + (z - a) * (cumulative[i] - cumulative[w0]) / total) * hop / (double)rate, 3),
                            Math.Round((a + (z - a) * (cumulative[i + 1] - cumulative[w0]) / total) * hop / (double)rate, 3));
        }
        return times;
    }

    // The start and end of each word of the Text in the Machine Speech, from its duration alone (where the speech cannot be read). THE ALIGNMENT OF THE REFERENCE SOFTWARE until 2026-11-18:'''

assert s.count(OLD) == 1
s = s.replace(OLD, NEW)

OLD2 = """        var seconds = Communicative.SecondsOf(Read(message, _speech)) ?? Math.Max(0.5, words.Length * 0.4);
        var times = Communicative.WordTimes(text, seconds);"""
NEW2 = """        var speech = Read(message, _speech);
        var seconds = Communicative.SecondsOf(speech) ?? Math.Max(0.5, words.Length * 0.4);
        // the words aligned with the Machine Speech where its samples can be read; otherwise its duration shared by the letters
        var times = Communicative.SamplesOf(speech, out var rate) is { Length: > 0 } samples ? Communicative.WordTimes(text, samples, rate) : Communicative.WordTimes(text, seconds);"""
assert s.count(OLD2) == 1
s = s.replace(OLD2, NEW2)
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
