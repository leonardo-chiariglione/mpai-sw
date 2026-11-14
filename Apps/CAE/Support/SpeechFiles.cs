using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Mpai.Cae.Apps;

// THE SPEECH OF A STAND-ALONE APP: the WAV files given on its command line, one for each time the workflow acquires speech, in order. The workflow asks for speech "via VAD
// or" a WAV file (the same workflow as in the MAS App); a stand-alone App has no microphone, so it answers only what asks for a WAV file - with the next file - and lets the
// microphone wait until it is abandoned.
public sealed class SpeechFiles(params string[] speechObjects)
{
    private readonly Queue<string> left = new(speechObjects);

    public async Task<string?> AcquireAsync(bool viaVad, string? wanted, CancellationToken abandon)
    {
        if (wanted?.Contains("\"WAV\"", StringComparison.Ordinal) == true)
            lock (left) return left.Count > 0 ? left.Dequeue() : null;
        try { await Task.Delay(Timeout.Infinite, abandon); } catch (OperationCanceledException) { }
        return null;
    }
}
