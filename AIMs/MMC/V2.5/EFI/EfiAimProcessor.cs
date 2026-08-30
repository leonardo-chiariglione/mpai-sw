using System;
using System.Collections.Generic;
using System.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using AIF.Controller;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Osd.VisualScene;   // ScrfdFaceDetector, FaceDetection
using Mpai.Paf.Fir;           // FaceCrop (shared face primitive)

namespace Mpai.Mmc.Efi;

// MMC-EFI-V2.5 - Entity Face Interpretation, as an AIF IAimProcessor.
//
// Receives a Basic Visual Object (OSD-BVO) whose Visual Qualifier declares its
// content is a face, and produces the Face Personal Status (MMC-FPS): the Personal
// Status Factors carried by the face, each a chosen label + Degree.
//
// ENGINE (Phase B, effective): detects the most prominent face (SCRFD), crops it,
// and reads facial affect with HSEmotion (EfficientNet-B0 multi-task, AffectNet) -
// eight emotion probabilities plus valence and arousal. The chosen emotion maps to
// an MPAI Emotion label (MMC-EEM); because Surprise is a Cognitive State in MPAI,
// a Surprise result is emitted as a Cognitive State (MMC-ECS) instead. The Degree
// is the model's confidence for the chosen label. Fused description and interpretation.
public sealed class EfiAimProcessor : IAimProcessor
{
    private readonly string _instanceId;
    private readonly ScrfdFaceDetector _detector;
    private readonly HSEmotionEstimator _hse;
    private readonly string _inPort;   // OSD-BVO
    private readonly string _outPort;  // MMC-FPS

    public EfiAimProcessor(
        string instanceId,
        ScrfdFaceDetector detector,
        HSEmotionEstimator hse,
        AimPortReader ports)
    {
        _instanceId = instanceId;
        _detector   = detector;
        _hse        = hse;
        _inPort     = ports.Input("OSD-BVO-V1.5");
        _outPort    = ports.Output("MMC-FPS-V2.5");
    }

    public string InstanceId => _instanceId;

    public System.Threading.Tasks.Task<Message> ProcessAsync(Message message)
    {
        if (!message.Ports.TryGetValue(_inPort, out var bvoJson) || string.IsNullOrWhiteSpace(bvoJson))
            return System.Threading.Tasks.Task.FromResult(
                Message.Error(message.MessageId, _instanceId, "no Basic Visual Object on input port"));

        var visual = MpaiJson.FromJson<BasicVisualObject>(bvoJson);
        if (visual is null || visual.Data.Length == 0)
            return System.Threading.Tasks.Task.FromResult(
                Message.Error(message.MessageId, _instanceId, "empty Basic Visual Object"));

        // Detect the most prominent face; crop to it (whole image if none found).
        FaceAffect affect;
        using (var image = Image.Load<Rgb24>(visual.Data))
        {
            var faces = _detector.Detect(visual.Data)
                .OrderByDescending(f => f.Width * f.Height)
                .ToList();

            using var crop = faces.Count > 0
                ? FaceCrop.Crop(image, faces[0].X1, faces[0].Y1, faces[0].X2, faces[0].Y2)
                : image.Clone();
            affect = _hse.Estimate(crop);
        }

        var fps = ToFacePersonalStatus(affect);

        return System.Threading.Tasks.Task.FromResult(new Message
        {
            MessageId = message.MessageId,
            MessageType = message.MessageType,
            Ports = new Dictionary<string, string> { [_outPort] = MpaiJson.ToJson(fps) }
        });
    }

    // Map the HSEmotion result to a Face Personal Status. Most emotions map to an
    // MPAI Emotion (MMC-EEM) label; Surprise maps to a Cognitive State (MMC-ECS),
    // as MPAI classes Surprise as a cognitive rather than emotional Factor.
    private static FacePersonalStatus ToFacePersonalStatus(FaceAffect a)
    {
        double degree = Math.Clamp(a.Confidence, 0.0, 1.0);

        if (a.Emotion == "Surprise")
            return new FacePersonalStatus
            {
                FaceCognitiveState = CognitiveState.Of(FactorLabel.Of("SURPRISE", "surprised", null, degree))
            };

        FactorLabel label = a.Emotion switch
        {
            "Anger"     => FactorLabel.Of("ANGER", "angry", null, degree),
            "Disgust"   => FactorLabel.Of("DISGUST", "disgusted", null, degree),
            "Fear"      => FactorLabel.Of("FEAR", "fearful/scared", null, degree),
            "Happiness" => FactorLabel.Of("HAPPINESS", "happy", null, degree),
            "Sadness"   => FactorLabel.Of("SADNESS", "sad", null, degree),
            "Contempt"  => FactorLabel.Of("HURT", "hurt", null, degree),
            _           => FactorLabel.Of("CALMNESS", "calm", null, degree)   // Neutral
        };

        return new FacePersonalStatus { FaceEmotion = Emotion.Of(label) };
    }
}
