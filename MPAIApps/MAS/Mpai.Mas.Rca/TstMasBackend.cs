using System;
using System.Threading;
using System.Threading.Tasks;

using Mpai.Core;

namespace Mpai.Mas.Rca;

// MMC-TST-V2.5 over MPAI-MAS, from the Remote Client Application's side.
//
// The shape of the exchange is set by the API: inputs are POSTed one port at a
// time, and the server runs the AIW on the FIRST Output GET, with every input it
// has buffered. So all ports go first, then the outputs are read.
//
// Where acquisition and delivery live: on the CLIENT, which is how AMQ answered
// the same question - UaUi records with its own recorder and posts the bytes.
// That is not a breach of zero trust, because the RCA sits outside the AIF
// altogether; the boundary being protected is the server's Controller. The
// server's MMC-SOA receives a Speech Object that already carries data and passes
// it through, which is exactly the case it was written for.
public sealed class TstMasBackend : IDisposable
{
    private const string Module = "MMC-TST-V2.5";

    private const string PortLanguageSelector = "LanguageSelector";
    private const string PortMediaSelector    = "MediaSelector";
    private const string PortInputText        = "InputText";
    private const string PortInputSpeech      = "InputSpeech";
    private const string PortOutputText       = "OutputText";
    private const string PortOutputSpeech     = "OutputSpeech";

    private readonly MasApiClient _api;
    private string _moduleId = string.Empty;

    public TstMasBackend(string baseUrl) => _api = new MasApiClient(baseUrl);

    public bool IsReady { get; private set; }

    public async Task PrepareAsync(CancellationToken ct = default)
    {
        await _api.CreateControllerAsync(ct);

        var started = await _api.StartAiwAsync(Module, ct);
        _moduleId   = started.ModuleId;
        IsReady     = true;
    }

    public sealed class Translation
    {
        public string  Text   { get; init; } = string.Empty;
        public byte[]? Speech { get; init; }
    }

    public Task<Translation> TranslateTextAsync(
        string text, string from, string into, CancellationToken ct = default) =>
        TranslateAsync(BasicTextObject.FromText(text), null, from, into, ct);

    // The captured WAV, as a Speech Object carrying the source language on its
    // Qualifier - the same contract the local pipeline uses, since MMC-ASR reads
    // the language from there whether the AIF is here or on a server.
    public Task<Translation> TranslateSpeechAsync(
        byte[] wav, string from, string into, CancellationToken ct = default)
    {
        var speech = BasicSpeechObject.FromData(
            wav,
            new SpeechQualifier
            {
                SpeechQualifierID = Guid.NewGuid().ToString(),
                Attributes = new SpeechAttributes
                {
                    Source = SpeechSource.Real,
                    Metadata = new SpeechMetadata
                    {
                        Language = new Language
                        {
                            LanguageCode   = from,
                            LanguageFormat = LanguageFormat.Iso639_1
                        }
                    }
                }
            });

        return TranslateAsync(null, speech, from, into, ct);
    }

    private async Task<Translation> TranslateAsync(
        BasicTextObject?   text,
        BasicSpeechObject? speech,
        string from,
        string into,
        CancellationToken ct)
    {
        if (!IsReady) throw new InvalidOperationException("PrepareAsync has not been called.");

        await _api.SendInputAsync(_moduleId, PortLanguageSelector,
            MpaiSelectorData.FromSelector(BasicSelectorObject.Languages(from, into)), ct);

        if (text is not null)
        {
            await _api.SendInputAsync(_moduleId, PortInputText, MpaiPortData.FromText(text), ct);

            // Both text and speech would otherwise be ambiguous on the server,
            // where the previous run's inputs may still be buffered.
            await _api.SendInputAsync(_moduleId, PortMediaSelector,
                MpaiSelectorData.FromSelector(BasicSelectorObject.Source(TextSource.InputText)), ct);
        }

        if (speech is not null)
        {
            await _api.SendInputAsync(_moduleId, PortInputSpeech, MpaiPortData.FromSpeech(speech), ct);

            await _api.SendInputAsync(_moduleId, PortMediaSelector,
                MpaiSelectorData.FromSelector(BasicSelectorObject.Source(TextSource.RecognisedText)), ct);
        }

        // The first Output GET is what runs the AIW.
        var textBytes = await _api.ReceiveOutputAsync(_moduleId, PortOutputText, ct);

        byte[]? spokenWav = null;
        try
        {
            var speechBytes = await _api.ReceiveOutputAsync(_moduleId, PortOutputSpeech, ct);
            spokenWav = MpaiPortData.ToSpeech(speechBytes).Data;
        }
        catch
        {
            // A language with no voice on the server produces no speech. The
            // translation is the result; the sound is a bonus.
        }

        return new Translation
        {
            Text   = MpaiPortData.ToText(textBytes).GetText(),
            Speech = spokenWav is { Length: > 0 } ? spokenWav : null
        };
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!IsReady) return;

        try { await _api.StopAiwAsync(_moduleId, ct); } catch { }
        try { await _api.DeleteControllerAsync(ct); } catch { }

        IsReady = false;
    }

    public void Dispose() => _api.Dispose();
}