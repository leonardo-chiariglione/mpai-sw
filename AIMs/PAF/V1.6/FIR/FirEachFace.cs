// NOTICE (third-party models and data). This source file is released under the BSD 3-Clause licence of this repository.
// The models and the databases it uses, and the data derived from them, are not part of it: each has its own licence, and some
// restrict use (for example to non-commercial research). Read THIRD-PARTY-NOTICES.md and the Terms of the AccessSources of the AIM.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;
using Mpai.Core.OSD;
using Mpai.Core;
using Mpai.Osd.VisualScene;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mpai.Paf.Fir;

// PAF-FIR-V1.6 AS TELEVISION MEDIA ANALYSIS NEEDS IT (1PAF-FIR-V1.6-I02, 2026-11-18): EVERY face of EVERY picture it is given, not only the largest. A picture is a frame of
// a Visual Scene that Visual Change Detection gives - its frames sampled through the scene (BasicVisualObjectProperties.FrameTimes, the programme time of each, in seconds) -
// and for each face found the AIM gives one Instance Identifier:
//   InstanceIdentifierData  who it is: the person of the gallery it matches (the registered), or, for a face that matches no one, a stable label of its own,
//                           "unregistered-1", "unregistered-2", ... (the same for the same face across the programme, by the likeness of the faces it has seen);
//   InstanceTime            the programme time of the frame (a relative Simple Time: the programme knows no other time);
//   InstanceSpaceTime       where it is in the picture: a Spatial Attitude whose Position is the centre of the face's box, in pixels from the top-left corner of the picture,
//                           its accuracy half the width and half the height of the box (the Spatial Qualifier says so: frame Screen, LengthUnit pixel), at the time of the frame.
// The Auxiliary Text, as in the other Television Media Analysis instance, raises the candidates it names. The Bounding Box Port is not connected in the L2 of OSD-TMA: this
// implementation gives nothing on it. The access-control FIR (I01) is unchanged.
public sealed class FirEachFace : IAimProcessor, IAimRunner
{
    public const double UnregisteredLikeness = 0.35, RegisteredLikeness = 0.25, RegisteredMargin = 0.08;
    // A FACE OF A PROGRAMME IS SMALL (in the AMI meeting at 960x540, 30 to 50 pixels wide): its likeness to the picture a person was registered from is lower than the gallery's
    // threshold (0.35) even when it is that person (measured 2026-11-18 on two frames: 0.25 to 0.61, always the highest for the right person). A face the gallery does not name
    // is therefore given to the registered person it is most like when that likeness is RegisteredLikeness or more AND it exceeds the next person's by RegisteredMargin.

    private readonly ScrfdFaceDetector _detector;
    private readonly ArcFaceRecogniser _recogniser;
    private readonly SubjectGallery _gallery;
    private readonly AIF.SharedStorage.ISharedStorage? _galleryStore;
    private readonly List<(string Label, float[] Embedding)> _unregistered = [];          // every face seen under each unregistered label (up to 20 a label)
    private string? _text;

    public string InstanceId { get; }

    public FirEachFace(string instanceId, ScrfdFaceDetector detector, ArcFaceRecogniser recogniser, SubjectGallery gallery, AIF.SharedStorage.ISharedStorage? galleryStore = null)
    {
        InstanceId = instanceId; _detector = detector; _recogniser = recogniser; _gallery = gallery; _galleryStore = galleryStore;
    }

    public Task<Message> ProcessAsync(Message message) => throw new NotSupportedException(InstanceId + " runs continuously.");

    public async Task RunAsync(IAimPorts ports, AimContext context)
    {
        while (await ports.SelectAsync(-1, [("OSD-BVO-V1.5", 1), ("OSD-TXO-V1.5", 1)]) is { } p)
        {
            if (await ports.ReadAsync(p.DataType, p.PortNumber, 0) is not { } m) continue;
            if (p.DataType == "OSD-TXO-V1.5") { _text = Mpai.Core.OSD.AuxiliaryText.Words(m.Json); continue; }
            if (JsonNode.Parse(m.Json) is not JsonObject picture) continue;
            foreach (var id in Faces(picture))
                await ports.WriteAsync("OSD-IID-V1.5", 1, id.ToJsonString());
        }
    }

    // The Instance Identifiers of the faces of the frames of a Basic Visual Object, frame by frame, left to right in each frame.
    public IEnumerable<JsonObject> Faces(JsonObject picture)
    {
        if (picture["BasicVisualObjectData"] is not JsonArray frames) yield break;
        var times = picture["BasicVisualObjectProperties"]?["FrameTimes"] as JsonArray;
        var start = picture["BasicVisualObjectTime"]?["Time"]?["SimpleTimeData"]?[0]?["StartTime"] is JsonValue s && s.TryGetValue<double>(out var t0) ? t0 : 0;
        var instance = (string?)picture["MInstanceID"] ?? "tma";
        if (_galleryStore is not null) _gallery.Refresh(_galleryStore);
        for (var i = 0; i < frames.Count; i++)
        {
            if ((string?)frames[i]?["Data"] is not { } b64) continue;
            var at = times is not null && i < times.Count && times[i] is JsonValue v && v.TryGetValue<double>(out var ti) ? ti : start;
            using var image = Image.Load<Rgb24>(Convert.FromBase64String(b64));
            var found = _detector.Detect(image).OrderBy(f => f.X1).ToList();
            for (var k = 0; k < found.Count; k++)
            {
                var face = found[k];
                using var crop = FaceCrop.Crop(image, face.X1, face.Y1, face.X2, face.Y2);
                var embedding = _recogniser.Embed(crop);
                var (label, confidence, registered) = Who(embedding);
                if (registered && _text is { Length: > 0 } && _text.Contains(label, StringComparison.OrdinalIgnoreCase)) confidence = Math.Min(1, confidence + FirAimProcessor.TextBonus);
                yield return Identifier(instance, label, confidence, registered, at, face, image.Width, image.Height, k + 1, found.Count);
            }
        }
    }

    // The person of the gallery the face matches, or the label of the unregistered face it resembles (a new one if none).
    private (string Label, double Confidence, bool Registered) Who(float[] embedding)
    {
        if (_gallery.IdentifyFace(embedding) is GalleryMatch m) return (m.SubjectId, m.Similarity, true);
        var registered = _gallery.SubjectIds.ToHashSet();
        var scores = _gallery.ScoreFace(embedding).Where(x => registered.Contains(x.SubjectId)).OrderByDescending(x => x.Score).ToList();
        if (scores.Count > 0 && scores[0].Score >= RegisteredLikeness && (scores.Count == 1 || scores[0].Score - scores[1].Score >= RegisteredMargin))
            return (scores[0].SubjectId, scores[0].Score, true);
        var best = _unregistered.GroupBy(u => u.Label).Select(g => (Label: g.Key, Likeness: g.Max(u => (double)ArcFaceRecogniser.CosineSimilarity(u.Embedding, embedding))))
                                .OrderByDescending(u => u.Likeness).FirstOrDefault();
        if (best.Label is not null && best.Likeness >= UnregisteredLikeness)
        {
            if (_unregistered.Count(u => u.Label == best.Label) < 20) _unregistered.Add((best.Label, embedding));
            return (best.Label, best.Likeness, false);
        }
        var label = "unregistered-" + (_unregistered.Select(u => u.Label).Distinct().Count() + 1);
        _unregistered.Add((label, embedding));
        return (label, 1.0, false);
    }

    public static JsonObject ProgrammeTime(double from, double to) => new()
    {
        ["Header"] = "OSD-STM-V1.5", ["SimpleTimeID"] = "st-" + Guid.NewGuid().ToString("N")[..12],
        ["SimpleTimeData"] = new JsonArray(new JsonObject
        {
            ["FlagsByte"] = 0, ["StartTime"] = Math.Round(from, 3), ["EndTime"] = Math.Round(to, 3), ["AccuracyMode"] = "single", ["AccuracyPlusMinus"] = 0.0, ["TimeType"] = false, ["TimeUnit"] = "00"
        })
    };

    // Where a box is in a picture: a Spatial Attitude in pixels from its top-left corner.
    public static JsonObject PictureAttitude(string objectId, double x1, double y1, double x2, double y2) => new()
    {
        ["Header"] = "OSD-OSA-V1.5", ["ObjectSpatialAttitudeID"] = "osa-" + Guid.NewGuid().ToString("N")[..12], ["ObjectID"] = objectId,
        ["Position"] = new JsonObject
        {
            ["Header"] = "OSD-OPS-V1.5", ["PositionID"] = "ops-" + Guid.NewGuid().ToString("N")[..12],
            ["CartPosition"] = new JsonArray(Math.Round((x1 + x2) / 2, 1), Math.Round((y1 + y2) / 2, 1), 0.0),
            ["CartPositionAccuracy"] = new JsonArray(Math.Round((x2 - x1) / 2, 1), Math.Round((y2 - y1) / 2, 1), 0.0)
        },
        ["Orientation"] = new JsonObject { ["Header"] = "OSD-OOR-V1.5", ["OrientationID"] = "oor-" + Guid.NewGuid().ToString("N")[..12], ["Orientation"] = new JsonArray(0.0, 0.0, 0.0) },
        ["SpatialQualifier"] = new JsonObject
        {
            ["Header"] = "TFA-SAQ-V1.5", ["SpatialQualifierID"] = "saq-" + Guid.NewGuid().ToString("N")[..12],
            ["SubTypes"] = new JsonObject { ["Kind"] = "SpatialAttitude" },
            ["Formats"] = new JsonObject { ["PositionFormat"] = "Cartesian" },
            ["Attributes"] = new JsonObject
            {
                ["ReferenceFrame"] = new JsonObject { ["Name"] = "Screen", ["Origin"] = "the top-left corner of the picture" },
                ["LengthUnit"] = "pixel"
            }
        }
    };

    private static JsonObject Identifier(string instance, string label, double confidence, bool registered, double at, FaceDetection face, int width, int height, int index, int count)
    {
        var time = ProgrammeTime(at, at);
        var id = "iid-" + Guid.NewGuid().ToString("N")[..12];
        return new JsonObject
        {
            ["Header"] = "OSD-IID-V1.5", ["MInstanceID"] = instance, ["InstanceIdentifier"] = id,
            ["InstanceTime"] = time.DeepClone(),
            ["InstanceSpaceTime"] = new JsonObject
            {
                ["Header"] = "OSD-SPT-V1.5", ["SpaceTimeID"] = "spt-" + Guid.NewGuid().ToString("N")[..12],
                ["SpatialAttitude1"] = PictureAttitude(label, face.X1, face.Y1, face.X2, face.Y2),
                ["Time"] = time
            },
            ["InstanceIdentifierData"] = new JsonArray(new JsonObject
            {
                ["InstanceLabel"] = label, ["LabelConfidenceLevel"] = Math.Round(Math.Clamp(confidence, 0, 1), 4),
                ["Taxonomy"] = new JsonObject { ["TaxonomyLevelIDs"] = registered ? new JsonArray("visual", "face", "person") : new JsonArray("visual", "face"), ["TaxonomyDataURI"] = "" }
            }),
            ["DescrMetadata"] = string.Create(CultureInfo.InvariantCulture,
                $"Face {index} of {count} in the frame at {at:0.###} s ({width}x{height} pixels): box {face.X1:0},{face.Y1:0}-{face.X2:0},{face.Y2:0}, detection score {face.Score:0.00}; {(registered ? "registered" : "not registered")}.")
        };
    }
}
