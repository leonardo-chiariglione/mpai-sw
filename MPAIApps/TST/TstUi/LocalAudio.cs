using System;
using System.Threading.Tasks;

using Mpai.Aims.Audio;
using Mpai.Core;

namespace TstUi;

// The microphone and the loudspeaker of a Remote Client Application.
//
// In LOCAL mode nothing here is used: MMC-SOA and MMC-SOD own the devices, and
// this window touches no audio at all. In REMOTE mode the AIF is on a server
// with neither device, so the RCA must hold them - which is how AMQ answered the
// same question, UaUi keeping its own recorder and player.
//
// The device classes are the AIMs' own, used here as plain libraries rather than
// as AIMs. That is the only reason this file needs a platform conditional, and
// it is the same one TstProvider has: two lines, in one place each.
internal sealed class LocalAudio
{
    private readonly IStartStopAcquisition _microphone;
    private readonly IAudioDeliveryAim     _loudspeaker;

    public LocalAudio()
    {
#if WINDOWS_DEVICES
        _microphone  = new WasapiAudioAcquisition();
        _loudspeaker = new WinmmAudioDelivery();
#else
        _microphone  = new AlsaAudioAcquisition();
        _loudspeaker = new AplayAudioDelivery();
#endif
    }

    public void StartRecording() => _microphone.StartAcquire();

    // The WAV bytes, ready to be posted to the server's InputSpeech port.
    public async Task<byte[]> StopRecordingAsync()
    {
        var audio = await _microphone.StopAcquireAsync();
        return audio.Data;
    }

    // Neither delivery AIM insists on a Qualifier - both accept a null one and
    // only object to a format that is declared and is not WAV - so bytes from
    // the server can be played as they arrive.
    public Task PlayAsync(byte[] wav) =>
        wav is { Length: > 0 }
            ? _loudspeaker.DeliverAsync(BasicAudioObject.FromData(wav))
            : Task.CompletedTask;
}