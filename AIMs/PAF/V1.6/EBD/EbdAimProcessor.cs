using System;
using System.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using AIF.Controller;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Osd.VisualScene;   // YoloxObjectDetector, ObjectDetection

namespace Mpai.Paf.Ebd;

// PAF-EBD-V1.6 - Entity Body Description, as an AIF IAimProcessor.
//
// Computes the Body Descriptors of an Entity from its Body Visual Object: detects
// the most prominent person (YOLOX), crops it, estimates a 3D body pose (BlazePose
// GHUM), and emits a Body Descriptors Object (PAF-BDO) carrying that pose as a BVH
// skeleton, with a Qualifier recording ContentFormat = "BVH". This is the body
// analogue of EFD (which detects+crops+embeds a face into PAF-FDO); here the
// descriptor is a 3D posture that expresses the body's semantics for Personal
// Status, not an identity embedding.
public sealed class EbdAimProcessor : IAimProcessor
{
    // The content format this implementation produces (a value from
    // TFA/V1.5/formats/BodyDescriptorsContentFormats.json).
    private const string ContentFormat = "BVH";

    private readonly string _instanceId;
    private readonly YoloxObjectDetector _detector;   // person localisation
    private readonly BlazePoseEstimator _pose;        // 3D body pose
    private readonly string _inPort;
    private readonly string _outPort;

    public EbdAimProcessor(
        string instanceId,
        YoloxObjectDetector detector,
        BlazePoseEstimator pose,
        AimPortReader ports)
    {
        _instanceId = instanceId;
        _detector   = detector;
        _pose       = pose;
        _inPort     = ports.Input("OSD-BVO-V1.5");
        _outPort    = ports.Output("PAF-BDO-V1.6");
    }

    public string InstanceId => _instanceId;

    public System.Threading.Tasks.Task<Message> ProcessAsync(Message message)
    {
        if (!message.Ports.TryGetValue(_inPort, out var bvoJson))
            return System.Threading.Tasks.Task.FromResult(
                Message.Error(message.MessageId, _instanceId, "no Body Visual Object on input port"));

        var visual = MpaiJson.FromJson<BasicVisualObject>(bvoJson);
        if (visual is null || visual.Data.Length == 0)
            return System.Threading.Tasks.Task.FromResult(
                Message.Error(message.MessageId, _instanceId, "empty Body Visual Object"));

        // Locate the most prominent person (largest 'person' detection); crop to it.
        var people = _detector.Detect(visual.Data)
            .Where(d => d.ClassName == "person")
            .OrderByDescending(d => d.Width * d.Height)
            .ToList();

        byte[] cropData;
        if (people.Count > 0)
            cropData = CropToPng(visual.Data, people[0]);
        else
            cropData = visual.Data;   // no person box - run pose on the whole image

        // Estimate the 3D body pose, then encode it as a BVH skeleton.
        var poseResult = _pose.Estimate(cropData);
        var bvh = BvhWriter.Write(poseResult);

        var bdo = BodyDescriptorsObject.FromContent(bvh, ContentFormat);

        return System.Threading.Tasks.Task.FromResult(new Message
        {
            MessageId = message.MessageId,
            MessageType = message.MessageType,
            Ports = new System.Collections.Generic.Dictionary<string, string>
            {
                [_outPort] = MpaiJson.ToJson(bdo)
            }
        });
    }

    // Crop the person box out of the source image and re-encode as PNG bytes for
    // the pose estimator.
    private static byte[] CropToPng(byte[] imageData, ObjectDetection box)
    {
        using var image = Image.Load<Rgb24>(imageData);
        int x1 = (int)Math.Clamp(box.X1, 0, image.Width - 1);
        int y1 = (int)Math.Clamp(box.Y1, 0, image.Height - 1);
        int x2 = (int)Math.Clamp(box.X2, 0, image.Width);
        int y2 = (int)Math.Clamp(box.Y2, 0, image.Height);
        int w = Math.Max(1, x2 - x1);
        int h = Math.Max(1, y2 - y1);

        using var crop = image.Clone(ctx => ctx.Crop(new Rectangle(x1, y1, w, h)));
        using var ms = new System.IO.MemoryStream();
        crop.SaveAsPng(ms);
        return ms.ToArray();
    }
}
