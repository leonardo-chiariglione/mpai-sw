using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;
using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Mmc.Sir;

// MMC-SIR-V2.5 - Speaker Identity Recognition. Self-contained IAimProcessor.
// Reads its own port names from 1MMC-SIR-V2.5-I01.json at startup.
//
// Consumes a Basic Speech Object (OSD-BSO-V1.5) - the mirror of what MMC-SOA
// produces - and an optional Speech Time (OSD-STM-V1.5) delimiting the span to
// analyse. Embeds the speech with ECAPA, matches it against the shared
// SubjectGallery, and emits the speaker identity as an Instance Identifier
// (OSD-IID-V1.5): a ranked candidate list at the speaker layer, or the coarse
// "speech" layer when no subject matches.
//
// The recognition logic is the verified SpeakerIdentityRecognitionAim unchanged;
// this processor only bridges it to the AIF - reading the port, decoding the
// speech bytes to samples, and packaging the identity as a Message.
public sealed class SirAimProcessor : IAimProcessor
{
    private readonly string                        _speechPort;
    private readonly string                        _speechTimePort;
    private readonly string                        _outputPort;
    private readonly SpeakerIdentityRecognitionAim _sir;

    public string InstanceId { get; }

    public SirAimProcessor(
        string instanceId,
        SpeakerEmbedder embedder,
        SubjectGallery gallery,
        AimPortReader ports)
    {
        InstanceId      = instanceId;
        _sir            = new SpeakerIdentityRecognitionAim(embedder, gallery);
        _speechPort     = ports.Input("OSD-BSO-V1.5");
        // Speech Time is optional for a first version (analyse the whole clip);
        // resolve its port if declared, else a harmless default that simply
        // won't be present in the incoming Ports dictionary.
        _speechTimePort = ports.InputOrDefault("OSD-STM-V1.5", "InputSpeechTime");
        _outputPort     = ports.Output("OSD-IID-V1.5");
    }

    public async Task<Message> ProcessAsync(Message message)
    {
        // Read the Basic Speech Object delivered on the speech input port.
        if (!message.Ports.TryGetValue(_speechPort, out var speechJson) ||
            string.IsNullOrWhiteSpace(speechJson))
        {
            return Message.Error(message.MessageId, InstanceId,
                $"MMC-SIR-V2.5: no speech on input port '{_speechPort}'.");
        }

        var speech = MpaiJson.FromJson<BasicSpeechObject>(speechJson);
        if (speech is null || speech.Data.Length == 0)
        {
            return Message.Error(message.MessageId, InstanceId,
                "MMC-SIR-V2.5: speech object carried no audio data.");
        }

        // Decode the in-memory WAV to 16 kHz mono samples and identify the speaker.
        // (Speech Time, if delivered on _speechTimePort, could window the samples
        // here; the first version analyses the whole clip.)
        float[] samples = WavReader.ReadMono16k(speech.Data);
        InstanceIdentifier identity = _sir.Identify(samples);

        var json = MpaiJson.ToJson(identity);
        await Task.CompletedTask;

        return new Message
        {
            MessageId   = message.MessageId,
            MessageType = "InstanceIdentifier",
            DataType    = "OSD-IID-V1.5",
            Payload     = json,
            Ports       = new Dictionary<string, string> { [_outputPort] = json }
        };
    }
}
