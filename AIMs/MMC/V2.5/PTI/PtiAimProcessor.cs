// NOTICE (third-party models and data). This source file is released under the BSD 3-Clause licence of this repository.
// The models and the databases it uses, and the data derived from them, are not part of it: each has its own licence, and some
// restrict use (for example to non-commercial research). Read THIRD-PARTY-NOTICES.md and the Terms of the AccessSources of the AIM.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using AIF.Controller;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Mmc.Etd;

namespace Mpai.Mmc.Pti;

// MMC-PTI-V2.5 - PS-Text Interpretation. Receives the Text Descriptors Object of a text - the 28 GoEmotions scores that Entity Text Description gives, or the Basic
// Text Descriptors (taggings) of Natural Language Understanding, whose text it scores with the same GoEmotions classifier - and gives the Text Personal Status.
//
// The 28 labels of GoEmotions are mapped to the Factors of the Personal Status, each to a label of the standard vocabulary (MMC-EEM Emotion, MMC-ECS Cognitive State, MMC-ESA Social Attitude):
//
//   Emotion:         anger, annoyance, disgust, fear, nervousness, joy, amusement, love, relief, sadness, grief, disappointment, embarrassment, remorse, pride
//   Cognitive State: confusion, curiosity, realization, surprise, excitement, optimism
//   Social Attitude: admiration, approval, disapproval, caring
//   Not mapped (nothing in the vocabulary says them): desire, gratitude, neutral.
//
// From each Factor, the label with the highest score is taken, with its score as the Degree, if the score reaches Threshold; a Factor below it is left out. This is a mapping of a classifier's
// labels onto the vocabulary, not a model of the three Factors: where GoEmotions has no label for a label of the vocabulary, that label is never given, and where the vocabulary has no
// label for a label of GoEmotions the information is lost (see Lost).
public sealed class PtiAimProcessor : IAimProcessor
{
    // The least score for a Factor to be given.
    public const float Threshold = 0.25f;

    // GoEmotions label -> (Factor, Category, GeneralAdjectival, SpecificAdjectival); the words are those of MMC/V2.5/data/{Emotion,CognitiveState,SocialAttitude}.json.
    private static readonly Dictionary<string, (string Factor, string Category, string? General, string? Specific)> Map = new()
    {
        ["anger"]          = ("E", "ANGER", "angry", null),
        ["annoyance"]      = ("E", "ANGER", "angry", "irritated"),
        ["disgust"]        = ("E", "DISGUST", "disgusted", null),
        ["fear"]           = ("E", "FEAR", "fearful/scared", null),
        ["nervousness"]    = ("E", "FEAR", "fearful/scared", "anxious/uneasy"),
        ["joy"]            = ("E", "HAPPINESS", "happy", "joyful"),
        ["amusement"]      = ("E", "HAPPINESS", "happy", "amused"),
        ["love"]           = ("E", "HAPPINESS", "happy", null),
        ["relief"]         = ("E", "CALMNESS", "calm", null),
        ["sadness"]        = ("E", "SADNESS", "sad", null),
        ["grief"]          = ("E", "SADNESS", "sad", "grief-stricken"),
        ["disappointment"] = ("E", "SADNESS", "sad", "disappointed"),
        ["embarrassment"]  = ("E", "PRIDE/SHAME", "proud", "embarrassed"),
        ["remorse"]        = ("E", "PRIDE/SHAME", "proud", "guilty/remorseful/sorry"),
        ["pride"]          = ("E", "PRIDE/SHAME", "proud", null),
        ["confusion"]      = ("C", "UNDERSTANDING", "comprehending", "bewildered/puzzled"),
        ["realization"]    = ("C", "UNDERSTANDING", "comprehending", null),
        ["curiosity"]      = ("C", "INTEREST", "interested", "curious"),
        ["surprise"]       = ("C", "SURPRISE", "surprised", null),
        ["excitement"]     = ("C", "AROUSAL", "aroused/excited/energetic", null),
        ["optimism"]       = ("C", "ATTENTION", "attentive", "hopeful/optimistic"),
        ["admiration"]     = ("S", "APPROVAL/DISAPPROVAL", "admiring/approving", null),
        ["approval"]       = ("S", "APPROVAL/DISAPPROVAL", "admiring/approving", null),
        ["disapproval"]    = ("S", "APPROVAL/DISAPPROVAL", "disapproving", null),
        ["caring"]         = ("S", "EMPATHY", "empathetic/caring", "kind"),
    };

    // The labels of GoEmotions that the Personal Status cannot say.
    public static readonly string[] Lost = { "desire", "gratitude", "neutral" };

    private readonly string _inputPort, _outputPort;
    private readonly Func<GoEmotionsClassifier>? _model;
    public string InstanceId { get; }

    // model: the GoEmotions classifier, for Text Descriptors that are not GoEmotions scores (the Basic taggings of Natural Language Understanding); it is loaded
    // when the first such Descriptors arrive. Without it those Descriptors are refused, as before.
    public PtiAimProcessor(string instanceId, AimPortReader ports, Func<GoEmotionsClassifier>? model = null)
    {
        InstanceId = instanceId; _model = model;
        _inputPort = ports.Input("MMC-TDO-V2.5");   // one Port: the Descriptors from the Entity Text Description or given directly as an input
        _outputPort = ports.Output("MMC-TPS-V2.5");
    }

    public Task<Message> ProcessAsync(Message message)
    {
        // The Descriptors come from Entity Text Description, or directly as input: whichever port holds them.
        string? json = null;
        foreach (var port in new[] { _inputPort })
            if (port.Length > 0 && message.Ports.TryGetValue(port, out var j) && !string.IsNullOrWhiteSpace(j)) { json = j; break; }
        if (json is null) return Task.FromResult(Message.Error(message.MessageId, InstanceId, "MMC-PTI-V2.5: no Text Descriptors Object to interpret."));
        float[] scores;
        try { scores = ScoresOf(json); }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        { return Task.FromResult(Message.Error(message.MessageId, InstanceId, "MMC-PTI-V2.5: " + ex.Message)); }

        var outJson = MpaiJson.ToJson(Interpret(scores));
        return Task.FromResult(new Message { MessageId = message.MessageId, MessageType = message.MessageType, Ports = new Dictionary<string, string> { [_outputPort] = outJson } });
    }

    // THE TWO FORMATS OF TEXT DESCRIPTORS THIS AIM READS, told by the Qualifier: the GoEmotions scores of Entity Text Description, and the Basic Text Descriptors
    // (the taggings of Natural Language Understanding; the Qualifier names no format). Of the taggings the text is the words of the part-of-speech tagging
    // ("word/TAG ..."), and the scores are those the GoEmotions classifier gives for it.
    private float[] ScoresOf(string json)
    {
        var tdo = MpaiJson.FromJson<TextDescriptorsObject>(json) ?? throw new FormatException("The Text Descriptors Object cannot be read.");
        if (tdo.TextDescriptorsQualifier?.Formats.OtherTextDescriptorsFormats is not null) return EtdAimProcessor.Scores(json);
        var text = TextOfTaggings(tdo) ?? throw new FormatException("The Text Descriptors Object holds neither GoEmotions scores nor a part-of-speech tagging.");
        if (_model is null) throw new FormatException("Basic Text Descriptors (taggings) need the GoEmotions classifier, which this instance does not have.");
        if (text.Length == 0) throw new FormatException("The part-of-speech tagging holds no word.");
        return _model().Scores(text);
    }

    // The words of the part-of-speech tagging, in order, or null when there is none.
    public static string? TextOfTaggings(TextDescriptorsObject tdo)
    {
        TextTaggings? taggings;
        try { taggings = tdo.Taggings(); } catch (System.Text.Json.JsonException) { return null; }
        var result = taggings?.TextDescriptorsData.POS_tagging?.Result;
        if (result is null) return null;
        var words = result.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => { var i = t.LastIndexOf('/'); return i > 0 ? t[..i] : t; });
        return string.Join(' ', words).Replace(" .", ".").Replace(" ,", ",").Replace(" ?", "?").Replace(" !", "!").Trim();
    }

    // The Text Personal Status of 28 scores (in the order of GoEmotionsClassifier.Labels).
    public static TextPersonalStatus Interpret(IReadOnlyList<float> scores)
    {
        FactorLabel? Best(string factor)
        {
            FactorLabel? best = null; float top = Threshold;
            for (int i = 0; i < GoEmotionsClassifier.Labels.Length; i++)
                if (Map.TryGetValue(GoEmotionsClassifier.Labels[i], out var m) && m.Factor == factor && scores[i] >= top)
                { top = scores[i]; best = FactorLabel.Of(m.Category, m.General, m.Specific, scores[i]); }
            return best;
        }
        var emotion = Best("E"); var cognitive = Best("C"); var social = Best("S");
        return new TextPersonalStatus
        {
            TextEmotion = emotion is null ? null : Emotion.Of(emotion),
            TextCognitiveState = cognitive is null ? null : CognitiveState.Of(cognitive),
            TextSocialAttitude = social is null ? null : SocialAttitude.Of(social)
        };
    }
}
