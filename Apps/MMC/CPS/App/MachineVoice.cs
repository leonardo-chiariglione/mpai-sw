using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using AIF.Controller;   // AifError

using Mpai.Aif.Api;     // IControllerApi, ControllerApi, AvatarUtterance
using Mpai.Core;
using Mpai.PhysicalLayer;

namespace Mpai.Mmc.Voice;

// THE MACHINE'S VOICE: the words of the Portable Avatar that answers are given, through the Controller API, to the Module that makes machine speech and face from text (1PAF-RSR-V1.6-I01, which this
// App's Controller holds as it holds the Module of the App); the Portable Avatar that comes back carries the speech, which the Loudspeaker Unit of the Physical Layer plays. The App asks the
// Controller and no more. This file is shared by the Apps of Conversation with Personal Status and of Conversation About a Scene. A line that cannot be spoken is said on the console; it does not stop
// the App.
internal sealed class MachineVoice(IControllerApi api, string machineName) : IDisposable
{
    private const string RsrModule = "1PAF-RSR-V1.6-I01";
    private const string BTO = "OSD-BTO-V1.5";
    private const string PAV = "PAF-PAV-V1.6";

    private bool started;
    private int moduleId;
    private readonly LoudspeakerUnit loudspeaker = new();

    // The speech of the words, as the WAV the Module gave; null when it cannot be made.
    public async Task<byte[]?> SpeechOfAsync(string words)
    {
        try
        {
            if (!started)
            {
                var (error, id) = await Task.Run(() => (api.StartFlow(RsrModule, out var begun), begun));
                if (error != AifError.OK) { Console.Error.WriteLine($"The machine's voice could not start ({error})."); return null; }
                moduleId = id; started = true;
            }
            // PAF-RSR takes the Text Object twice (#1 to Text-To-Speech, #2 to Generative Face Description), the Avatar Model and the machine's ID.
            var text = MpaiJson.ToJson(BasicTextObject.FromText(words));
            var inputs = new List<ControllerApi.Datum>
            {
                new(BTO, 1, text),
                new(BTO, 2, text),
                new("OSD-B3O-V1.5", 1, AvatarUtterance.AvatarDatum("cav-avatar.glb")),
                new("string", 1, System.Text.Json.JsonSerializer.Serialize(machineName))
            };
            var result = await Task.Run(() => api.Advance(moduleId, inputs));
            if (!result.Ok) { Console.Error.WriteLine($"The machine's voice gave no speech ({result.Error})."); return null; }
            return AvatarUtterance.FromPortableAvatar(result.ByType(PAV))?.MachineSpeechWav;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Console.Error.WriteLine("The machine's voice failed: " + ex.Message); return null; }
    }

    // The words said aloud on the Loudspeaker Unit; the WAV is also written to wavFile when it is given. True when the speech was made.
    public async Task<bool> SayAsync(string words, string? wavFile = null, bool play = true)
    {
        var wav = await SpeechOfAsync(words);
        if (wav is null || wav.Length == 0) return false;
        if (wavFile is not null) File.WriteAllBytes(wavFile, wav);
        if (!play) return true;
        try
        {
            var speech = BasicSpeechObject.FromData(wav, new SpeechQualifier
            {
                SpeechQualifierID = Guid.NewGuid().ToString(),
                Format = new SpeechFormat { TransportFormats = new SpeechTransportFormats { FileFormat = SpeechFileFormat.Wav } },
                Attributes = new SpeechAttributes { Source = SpeechSource.Synthetic }
            });
            await loudspeaker.DeliverAsync(speech);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Console.Error.WriteLine("The machine's voice could not be played: " + ex.Message); }
        return true;
    }

    public void Dispose()
    {
        try { if (started) api.StopFlow(moduleId); } catch { }
    }
}
