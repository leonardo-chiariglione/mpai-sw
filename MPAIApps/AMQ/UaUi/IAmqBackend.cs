using System.Threading;
using System.Threading.Tasks;

using Mpai.Core;

namespace Mpai.Mas.Rca;

// The operations the AMQ UI needs, independent of WHERE the AIF runs.
//
//  * In-process backend: calls the local Controller via MPAI_AIFU_* (the app
//    we have today - Controller + AMQ + models all local).
//  * MAS backend: calls a remote SCI via the MPAI-MAS Remote API (the RCA - the
//    UI is a thin client; Controller + AMQ + models live on the server).
//
// The UI depends only on this interface, so the same UI runs as a local app OR
// as a Remote Client Application by swapping the backend implementation.
public interface IAmqBackend
{
    // Prepare the backend (load models locally, or create a remote SCI + start
    // the AIW). Called once; may be slow (model load) for the in-process case.
    Task PrepareAsync(CancellationToken ct = default);

    // Ask a question about an image. Exactly one of questionText / questionAudio
    // is supplied (text mode vs voice mode); the other is null.
    // Returns the answer text, plus the spoken-answer WAV bytes if produced.
    Task<AmqAnswer> AskAsync(
        BasicVisualObject image,
        BasicTextObject?  questionText,
        BasicAudioObject? questionAudio,
        CancellationToken ct = default);

    bool IsReady { get; }
}

public sealed class AmqAnswer
{
    public required string  Text       { get; init; }
    public          byte[]? SpokenWav  { get; init; }   // OutputAudio, if produced
    public          byte[]? FrameBytes { get; init; }   // OutputVisual, if produced
}
