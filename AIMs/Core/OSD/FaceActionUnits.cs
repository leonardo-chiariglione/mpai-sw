using System;
using System.Collections.Generic;
using System.Linq;

namespace Mpai.Core.OSD;

// FACS Action Units - the machine's facial expression coded as Facial Action Coding
// System (Ekman) Action Unit activations, each a weight in [0,1]. This is the
// principled, renderer-agnostic representation of "what expression to show": the
// same AU descriptor can drive a 2D face, a 3D FACS avatar (OpenFACS/FACSvatar), or
// any blendshape rig. It is the Machine Face Descriptors the generative Entity Face
// Description produces from the machine's (generated) Face Personal Status.
//
// The core AU set covers the six basic emotions (via EM-FACS) plus the mouth AUs
// used for lip-sync.
public enum ActionUnit
{
    AU1_InnerBrowRaise = 1,
    AU2_OuterBrowRaise = 2,
    AU4_BrowLower      = 4,
    AU5_UpperLidRaise  = 5,
    AU6_CheekRaise     = 6,
    AU7_LidTighten     = 7,
    AU9_NoseWrinkle    = 9,
    AU12_LipCornerPull = 12,   // smile
    AU15_LipCornerDepress = 15,// frown
    AU17_ChinRaise     = 17,
    AU20_LipStretch    = 20,
    AU23_LipTighten    = 23,
    AU25_LipsPart      = 25,
    AU26_JawDrop       = 26
}

// A set of Action Unit activations (AU -> weight in [0,1]).
public sealed class FaceActionUnits
{
    public string Header { get; init; } = "PAF-FDO-V1.6";           // Face Descriptors Object
    public string FaceActionUnitsID { get; init; } = Guid.NewGuid().ToString();
    // The descriptor format: FACS Action Units (renderer-agnostic).
    public string ContentFormat { get; init; } = "FACS-AU";
    public Dictionary<string, double> ActionUnits { get; init; } = new();

    public double Weight(ActionUnit au) =>
        ActionUnits.TryGetValue(au.ToString(), out var w) ? w : 0.0;

    public static FaceActionUnits Of(IReadOnlyDictionary<ActionUnit, double> weights) => new()
    {
        ActionUnits = weights.ToDictionary(
            kv => kv.Key.ToString(),
            kv => Math.Clamp(kv.Value, 0.0, 1.0))
    };
}

// EM-FACS: maps a basic Emotion (Ekman category) to the Action Units that express
// it, scaled by intensity. The activation patterns are the standard EM-FACS emotion
// prototypes (e.g. happiness = AU6 cheek raise + AU12 lip-corner pull). This is the
// model that DRIVES the facial animation - a validated, principled mapping, not an
// ad-hoc preset.
public static class EmFacs
{
    // Emotion category (MPAI-MMC Emotion) -> its prototypical AU pattern (unit weights).
    private static readonly Dictionary<string, ActionUnit[]> Prototypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["HAPPINESS"] = new[] { ActionUnit.AU6_CheekRaise, ActionUnit.AU12_LipCornerPull },
        ["SADNESS"]   = new[] { ActionUnit.AU1_InnerBrowRaise, ActionUnit.AU4_BrowLower, ActionUnit.AU15_LipCornerDepress },
        ["ANGER"]     = new[] { ActionUnit.AU4_BrowLower, ActionUnit.AU5_UpperLidRaise, ActionUnit.AU7_LidTighten, ActionUnit.AU23_LipTighten },
        ["FEAR"]      = new[] { ActionUnit.AU1_InnerBrowRaise, ActionUnit.AU2_OuterBrowRaise, ActionUnit.AU4_BrowLower, ActionUnit.AU5_UpperLidRaise, ActionUnit.AU20_LipStretch, ActionUnit.AU26_JawDrop },
        ["DISGUST"]   = new[] { ActionUnit.AU9_NoseWrinkle, ActionUnit.AU15_LipCornerDepress, ActionUnit.AU17_ChinRaise },
        // MMC classes Surprise as a Cognitive State, but its AU prototype is standard:
        ["SURPRISE"]  = new[] { ActionUnit.AU1_InnerBrowRaise, ActionUnit.AU2_OuterBrowRaise, ActionUnit.AU5_UpperLidRaise, ActionUnit.AU26_JawDrop },
        // Calmness / neutral: a relaxed face - no active AUs.
        ["CALMNESS"]  = Array.Empty<ActionUnit>()
    };

    // Produce the AU activations for an emotion category at the given intensity [0,1].
    public static FaceActionUnits ToActionUnits(string? emotionCategory, double intensity)
    {
        double w = Math.Clamp(intensity, 0.0, 1.0);
        var weights = new Dictionary<ActionUnit, double>();
        if (emotionCategory is not null && Prototypes.TryGetValue(emotionCategory, out var aus))
            foreach (var au in aus) weights[au] = w;
        return FaceActionUnits.Of(weights);
    }
}
