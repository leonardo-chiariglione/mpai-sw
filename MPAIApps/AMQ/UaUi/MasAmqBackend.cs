using System;
using System.Threading;
using System.Threading.Tasks;

using Mpai.Core;

namespace Mpai.Mas.Rca;

// The RCA's AMQ backend: drives a remote SCI over the MPAI-MAS Remote API.
//
// The UI (Client Application) calls Ask; this backend performs the MAS dance:
//   1. Create Controller (SCI)                          [once, in PrepareAsync]
//   2. Start AIW "MMC-AMQ-V2.5"                          [once, in PrepareAsync]
//   3. POST Input/InputVisual  (the image, as MPAI/port-data)
//   4. POST Input/InputText  OR  Input/InputAudio        (the question)
//   5. GET  Output/OutputText  (the answer)
//   6. GET  Output/OutputAudio (the spoken answer)       [best-effort]
//   7. GET  Output/OutputVisual (the frame)              [best-effort]
//
// Port objects are serialised to/from MPAI/port-data by MpaiPortData.
//
// The heavy models live server-side with the SCI; this client holds none.
public sealed class MasAmqBackend : IAmqBackend
{
    private const string ModuleName = "MMC-AMQ-V2.5";

    // AMQ boundary port names (current, Audio-based AMQ).
    private const string PortInVisual  = "InputVisual";
    private const string PortInText    = "InputText";
    private const string PortInAudio   = "InputAudio";
    private const string PortOutText   = "OutputText";
    private const string PortOutAudio  = "OutputAudio";
    private const string PortOutVisual = "OutputVisual";

    private readonly MasApiClient _api;
    private string _moduleId = string.Empty;

    public bool IsReady { get; private set; }

    public MasAmqBackend(string baseUrl) => _api = new MasApiClient(baseUrl);

    public async Task PrepareAsync(CancellationToken ct = default)
    {
        await _api.CreateControllerAsync(ct);          // create the SCI
        var start = await _api.StartAiwAsync(ModuleName, ct);
        _moduleId = start.ModuleId;
        IsReady = true;
    }

    public async Task<AmqAnswer> AskAsync(
        BasicVisualObject image,
        BasicTextObject?  questionText,
        BasicAudioObject? questionAudio,
        CancellationToken ct = default)
    {
        if (!IsReady) throw new InvalidOperationException("Backend not prepared.");

        // 1. Send the image to the visual input port.
        await _api.SendInputAsync(_moduleId, PortInVisual,
            MpaiPortData.FromVisual(image), ct);

        // 2. Send the question on exactly one branch.
        if (questionText is not null)
            await _api.SendInputAsync(_moduleId, PortInText,
                MpaiPortData.FromText(questionText), ct);
        else if (questionAudio is not null)
            await _api.SendInputAsync(_moduleId, PortInAudio,
                MpaiPortData.FromAudio(questionAudio), ct);
        else
            throw new ArgumentException("Provide either questionText or questionAudio.");

        // 3. Receive the text answer (required).
        var answerBytes = await _api.ReceiveOutputAsync(_moduleId, PortOutText, ct);
        var answer = MpaiPortData.ToText(answerBytes).GetText();

        // 4. Receive the spoken answer and frame (best-effort - may be absent).
        byte[]? spokenWav = await TryReceiveAudioAsync(PortOutAudio, ct);
        byte[]? frame     = await TryReceiveVisualAsync(PortOutVisual, ct);

        return new AmqAnswer { Text = answer, SpokenWav = spokenWav, FrameBytes = frame };
    }

    private async Task<byte[]?> TryReceiveAudioAsync(string port, CancellationToken ct)
    {
        try
        {
            var bytes = await _api.ReceiveOutputAsync(_moduleId, port, ct);
            return MpaiPortData.ToAudio(bytes).Data;   // raw WAV bytes for playback
        }
        catch { return null; }
    }

    private async Task<byte[]?> TryReceiveVisualAsync(string port, CancellationToken ct)
    {
        try
        {
            var bytes = await _api.ReceiveOutputAsync(_moduleId, port, ct);
            return MpaiPortData.ToVisual(bytes).Data;
        }
        catch { return null; }
    }
}
