using System.Net.WebSockets;

namespace Mpai.Mmm.Server;

// A SHARED SCREEN, AS THE MMM CARRIES IT. The User with the Right to share places in the room a Visual Object that is a stream (MM-Add) and feeds it as it captures its screen: a WebSocket,
// /ws/v2.2/screen/{id}, whose binary messages are the picture and the sound; the first byte says which:
//   3 video configuration (JSON: codec, width, height)         4 video chunk (flags: bit 0 = key frame; time in microseconds, float64; the compressed data)
//   5 sound configuration (JSON: codec, sampleRate, channels)   6 sound chunk (time in microseconds, float64; the compressed data: Opus)
//   1 a picture as JPEG (sent by the Screen Helper, a program outside the browser)    2 sound as raw 16-bit PCM, 24 kHz mono (older)
// Whoever may see it - whoever has an Item in the room - opens /ws/v2.2/screen/{id}/watch and receives them. The server does not decode anything: it keeps the configurations and the video since the
// last key frame, so that a watcher that comes in, or one that fell behind, starts from a key frame; the sound it is given as from when it came, all, in order (it is what is heard). A JPEG picture is
// given as the latest one, not the ones in between. Nothing is kept when the stream is over: the Visual Object is gone.
public sealed class ScreenStreams
{
    private sealed class Live
    {
        public byte[]? Latest;                                // the latest JPEG picture
        public long Sequence;
        public byte[]? VideoConfig, SoundConfig;              // the last configurations (3, 5)
        public readonly List<byte[]> Video = [];              // the video chunks since the last key frame
        public long VideoBase;                                // how many chunks came before the first of them
        public readonly List<byte[]> Sound = [];              // the sound chunks (2, 6), the last ones only
        public long SoundBase;                                // how many chunks were dropped from the front
        public bool Closed;
        public TaskCompletionSource Signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly Dictionary<string, Live> live = new(StringComparer.Ordinal);
    private Live Of(string id) { lock (live) return live.TryGetValue(id, out var l) ? l : live[id] = new Live(); }

    private void Push(string id, byte[] message)
    {
        var l = Of(id);
        lock (l)
        {
            switch (message[0])
            {
                case 2 or 6: l.Sound.Add(message); if (l.Sound.Count > 400) { l.Sound.RemoveRange(0, 200); l.SoundBase += 200; } break;
                case 3: l.VideoConfig = message; l.VideoBase += l.Video.Count; l.Video.Clear(); break;            // a new configuration starts the video again
                case 5: l.SoundConfig = message; break;
                case 4:
                    if (message.Length > 10 && (message[1] & 1) == 1) { l.VideoBase += l.Video.Count; l.Video.Clear(); }   // a key frame: what went before is of no use to a newcomer
                    if (l.Video.Count > 1000) { l.VideoBase += l.Video.Count; l.Video.Clear(); }                       // a sender that never sends a key frame: nothing is kept (the watchers wait for one)
                    l.Video.Add(message); break;
                default: l.Latest = message; l.Sequence++; break;
            }
            var s = l.Signal; l.Signal = new(TaskCreationOptions.RunContinuationsAsynchronously); s.TrySetResult();
        }
    }

    private void Close(string id)
    {
        var l = Of(id);
        lock (l) { l.Closed = true; var s = l.Signal; l.Signal = new(TaskCreationOptions.RunContinuationsAsynchronously); s.TrySetResult(); }
    }

    // The feed: the sharer's WebSocket. It ends when the sharer closes it (or the connection is lost); the Visual Object is then gone.
    public async Task FeedAsync(WebSocket socket, string id, MInstance m, CancellationToken stop)
    {
        var buffer = new byte[256 * 1024];
        try
        {
            while (socket.State == WebSocketState.Open && !stop.IsCancellationRequested && m.ScreenLive(id))
            {
                var got = new List<byte>();
                WebSocketReceiveResult r;
                do { r = await socket.ReceiveAsync(buffer, stop); got.AddRange(buffer.AsSpan(0, r.Count).ToArray()); } while (!r.EndOfMessage);
                if (r.MessageType == WebSocketMessageType.Close) break;
                if (r.MessageType == WebSocketMessageType.Binary && got.Count > 1 && got[0] is >= 1 and <= 6) Push(id, got.ToArray());
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            m.ScreenEnded(id); Close(id);
            try { if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived) await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "over", CancellationToken.None); } catch { }
            lock (live) live.Remove(id);
        }
    }

    // A watcher: the configurations and the video from the last key frame at once, then each chunk that follows, and the sound from when it came, until the screen is over (or the meeting ends: the Visual Object is gone, or may not be read).
    public async Task WatchAsync(WebSocket socket, string id, Func<bool> may, MInstance m, CancellationToken stop)
    {
        var l = Of(id);
        long seen = 0, sound, video = -1;
        byte[]? sentVideoConfig = null, sentSoundConfig = null;
        lock (l) sound = l.SoundBase + l.Sound.Count;                      // what was said before it came is not replayed
        try
        {
            while (socket.State == WebSocketState.Open && !stop.IsCancellationRequested)
            {
                byte[]? frame = null; var send = new List<byte[]>(); bool closed; Task wait;
                lock (l)
                {
                    if (l.VideoConfig is not null && !ReferenceEquals(l.VideoConfig, sentVideoConfig)) { sentVideoConfig = l.VideoConfig; send.Add(l.VideoConfig); video = l.VideoBase; }   // a (new) configuration: from the first chunk now kept, which is a key frame
                    if (l.SoundConfig is not null && !ReferenceEquals(l.SoundConfig, sentSoundConfig)) { sentSoundConfig = l.SoundConfig; send.Add(l.SoundConfig); }
                    if (sentVideoConfig is not null)
                    {
                        if (video < l.VideoBase) video = l.VideoBase;      // it fell behind: it goes on from the last key frame
                        for (; video < l.VideoBase + l.Video.Count; video++) send.Add(l.Video[(int)(video - l.VideoBase)]);
                    }
                    if (l.Sequence != seen) { frame = l.Latest; seen = l.Sequence; }
                    if (sound < l.SoundBase) sound = l.SoundBase;
                    for (; sound < l.SoundBase + l.Sound.Count; sound++) send.Add(l.Sound[(int)(sound - l.SoundBase)]);
                    closed = l.Closed; wait = l.Signal.Task;
                }
                foreach (var c in send) await socket.SendAsync(c, WebSocketMessageType.Binary, true, stop);
                if (frame is not null) await socket.SendAsync(frame, WebSocketMessageType.Binary, true, stop);
                if (frame is not null || send.Count > 0) continue;
                if (closed || !m.ScreenLive(id) || !may()) break;
                await Task.WhenAny(wait, Task.Delay(1000, stop));
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally { try { if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived) await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "over", CancellationToken.None); } catch { } }
    }
}
