using System;
using System.Threading.Tasks;

using Mpai.Core;

namespace Mpai.Amq;

// Answer to Multimodal Question (MMC-AMQ) — a composite AIM that is itself an
// AIM, containing SIX SubAIMs:
//
//   Visual Object Acquisition (CVE-VOA) --\
//   Audio Object Acquisition  (CAE-AOA) -> Automatic Speech Recognition (MMC-ASR)
//   -> Text and Image Query (MMC-TIQ)   -> Text to Speech (MMC-TTS)
//   -> Audio Object Delivery  (CAE-AOD)
//
// The three central SubAIMs are environment-independent; the acquisition and
// delivery edges (VOA, AOA, AOD) are environment-dependent and injected by the
// host. The composite depends only on the AIM interfaces, so it stays portable.
public sealed class AmqCompositeAim : IAmqAim
{
    private readonly IVisualAcquisitionAim _voa;
    private readonly IAudioAcquisitionAim _aoa;
    private readonly IAsrAim _asr;
    private readonly ITiqAim _tiq;
    private readonly ITtsAim _tts;
    private readonly IAudioDeliveryAim _aod;

    public AmqCompositeAim(
        IVisualAcquisitionAim voa,
        IAudioAcquisitionAim aoa,
        IAsrAim asr,
        ITiqAim tiq,
        ITtsAim tts,
        IAudioDeliveryAim aod)
    {
        _voa = voa;
        _aoa = aoa;
        _asr = asr;
        _tiq = tiq;
        _tts = tts;
        _aod = aod;
    }

    // The composite is an AIM: report its own standard name and the standard
    // identifiers of the six SubAIMs it combines.
    public string Describe()
    {
        var self = (IAim)this;
        return
            $"{self.AimName} ({self.AimIdentifier})  SubAIMs: " +
            $"{((IAim)_voa).AimIdentifier}, " +
            $"{((IAim)_aoa).AimIdentifier}, " +
            $"{((IAim)_asr).AimIdentifier}, " +
            $"{((IAim)_tiq).AimIdentifier}, " +
            $"{((IAim)_tts).AimIdentifier}, " +
            $"{((IAim)_aod).AimIdentifier}";
    }

    // ---- Stepwise API (lets a host drive the flow and cue the user) ----

    // CVE-VOA: acquire the image to ask about.
    public Task<BasicVisualObject> AcquireImageAsync(VisualAcquisitionRequest? visual = null)
        => _voa.AcquireAsync(visual ?? new VisualAcquisitionRequest());

    // MMC-TTS + CAE-AOD: speak a short cue to the user (e.g. "Ask your question now").
    public async Task SpeakAsync(string text)
    {
        BasicSpeechObject cue = await _tts.ProcessAsync(BasicTextObject.FromText(text));
        await _aod.DeliverAsync(cue.AsAudio());
    }

    // CAE-AOA -> MMC-ASR -> MMC-TIQ -> MMC-TTS -> CAE-AOD, over an already
    // acquired image. Records the spoken question, answers, speaks the answer.
    public async Task<AmqResult> AnswerAsync(
        BasicVisualObject image,
        AcquisitionRequest? audio = null)
    {
        BasicAudioObject audioIn = await _aoa.AcquireAsync(audio ?? new AcquisitionRequest());
        BasicTextObject question = await _asr.ProcessAsync(audioIn.AsSpeech());
        BasicTextObject answer = await _tiq.ProcessAsync(question, image);
        BasicSpeechObject speechOut = await _tts.ProcessAsync(answer);
        await _aod.DeliverAsync(speechOut.AsAudio());

        return new AmqResult
        {
            Image = image,
            Question = question,
            Answer = answer,
            SpeechAnswer = speechOut
        };
    }

    // Press-to-stop: start recording now (after the cue), then stop + answer.
    public void StartListening()
    {
        if (_aoa is IStartStopAcquisition ss)
            ss.StartAcquire();
        else
            throw new NotSupportedException("The audio acquisition AIM does not support start/stop.");
    }

    public async Task<AmqResult> StopAndAnswerAsync(BasicVisualObject image)
    {
        if (_aoa is not IStartStopAcquisition ss)
            throw new NotSupportedException("The audio acquisition AIM does not support start/stop.");

        BasicAudioObject audioIn = await ss.StopAcquireAsync();
        BasicTextObject question = await _asr.ProcessAsync(audioIn.AsSpeech());
        BasicTextObject answer = await _tiq.ProcessAsync(question, image);
        BasicSpeechObject speechOut = await _tts.ProcessAsync(answer);
        await _aod.DeliverAsync(speechOut.AsAudio());

        return new AmqResult
        {
            Image = image,
            Question = question,
            Answer = answer,
            SpeechAnswer = speechOut
        };
    }

    // One-shot: acquire image, then answer (headless/console convenience).
    public async Task<AmqResult> ProcessAsync(
        VisualAcquisitionRequest? visual = null,
        AcquisitionRequest? audio = null)
    {
        BasicVisualObject image = await AcquireImageAsync(visual);
        return await AnswerAsync(image, audio);
    }
}
