using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Mmc.Nlu;   // NluAimProcessor

namespace Nlu.Host;

// Composition root for the MMC-NLU test host. Constructs the Natural Language
// Understanding AIM, self-contained, reading its own port names from its instance
// JSON via the AmdStore.
public sealed class NluHostProvider : IAimProvider
{
    private readonly AmdStore _store;

    public NluHostProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "MMC-NLU-V2.5" =>
                new NluAimProcessor(aimName, AimPortReader.Load(_store, aimName)),

            _ => throw new NotSupportedException($"NluHostProvider does not provide '{aimName}'.")
        };
}
