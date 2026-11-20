using System.Collections.Generic;

using AIF.Controller;

namespace Mpai.Mmc.Pti;

// Plug-in of MMC-PTI-V2.5: the mapping is code; the GoEmotions classifier (its AccessSources) is loaded for Basic Text Descriptors only.
public sealed class PtiPlugin : IAimPlugin
{
    public string AimName => "MMC-PTI-V2.5";
    public IAimProcessor Create(AimPortReader ports, IReadOnlyDictionary<string, string> settings) => new PtiAimProcessor(AimName, ports, () => _model ??= Mpai.Mmc.Etd.EtdFactory.Create(settings));
    private Mpai.Mmc.Etd.GoEmotionsClassifier? _model;
}
