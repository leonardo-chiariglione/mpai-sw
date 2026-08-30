using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Mmc.Nlu;   // NluAimProcessor
using Mpai.Mmc.Esi;   // EsiAimProcessor
using Mpai.Mmc.Efi;   // EfiAimProcessor
using Mpai.Mmc.Psm;   // PsmAimProcessor
using Mpai.Mmc.Sir;   // SpeakerEmbedder (for ESI's WavReader path is static; embedder not needed here)

namespace Pse.Host;

// Composition root for the PSE test host: provides the four Personal-Status AIMs -
// Natural Language Understanding (Text PS), Entity Speech Interpretation (Speech PS),
// Entity Face Interpretation (Face PS), and Personal Status Multiplexing (assembles
// the Entity PS). First-pass engines (Phase A); the interfaces do not change when the
// engines are deepened.
public sealed class PseHostProvider : IAimProvider
{
    private readonly AmdStore _store;

    public PseHostProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "MMC-NLU-V2.5" => new NluAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "MMC-ESI-V2.5" => new EsiAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "MMC-EFI-V2.5" => new EfiAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "MMC-PSM-V2.5" => new PsmAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            _ => throw new NotSupportedException($"PseHostProvider does not provide '{aimName}'.")
        };
}
