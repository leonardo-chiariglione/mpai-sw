using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Mpai.Xrv;

// WHAT A STATUS INTERPRETATION AIM READS IN THE DESCRIPTORS (N-26, 2026-11-15): the body, the voice of a crowd and where one faces. The Descriptors carry the data; the Status carries what
// it means for a Cue: a Visual Behaviour (XRV-VBH-V1.0: Gestures, StandSit), an Audio Behaviour (XRV-ABH-V1.0: ClappingIntensity) and a Spatial Attitude read to the nearest mark.
//
// THE SIMPLEST READINGS (the standard names the elements, not how they are computed):
//  - THE BODY is a Body Descriptors Object (PAF-BDO-V1.6) whose Data is the text of a JSON object of joint angles in degrees: {"Joints": {"LeftArmElevation", "RightArmElevation",
//    "HipFlexion", "KneeFlexion"}} - the elevation of an upper arm from hanging down (0) to straight up (180), the flexion of the hip and of the knee (0 standing straight). The format is
//    this implementation's (named in the Qualifier where there is one); the Body Descriptors Data leaves the format to its Qualifier.
//    Both upper arms above 135 degrees: the Gesture "Raising arms" (XRV-GSA-V1.0), its Intensity the mean elevation over 180 on the 0-100 scale.
//    StandSit: true when the person stands, false when it sits (hip and knee both flexed more than 60 degrees). The schema says only "boolean": true = standing is a choice.
//  - THE VOICE OF A CROWD is an Audio Object (OSD-AUO-V1.5) whose Basic Audio Object carries 16 kHz mono 16-bit PCM in base64 (Data). The applause of a crowd is loud and noise-like
//    (ClappingIntensity below says how it is measured); speech, music and silence give little or nothing.
//  - WHERE ONE FACES: the Spatial Attitude of the Descriptors with its Position to the nearest metre and its yaw to the nearest 45 degrees (pitch and roll 0): a Cue Condition names
//    a mark and a direction, not a measurement.
public static class Behaviour
{
    public const string BodyFormat = "MPAI-XRV-Joints";

    public sealed record Joints(double LeftArm, double RightArm, double Hip, double Knee)
    {
        public bool ArmsRaised => LeftArm > 135 && RightArm > 135;
        public bool Sitting => Hip > 60 && Knee > 60;
    }

    // The joints of a Body Descriptors Object, or null if it carries none this reading understands.
    public static Joints? JointsOf(JsonNode? body)
    {
        foreach (var item in (body?["BodyDescriptorsData"] as JsonArray) ?? [])
        {
            if (item?["Data"] is not JsonValue v || !v.TryGetValue<string>(out var text)) continue;
            JsonNode? data;
            try { data = JsonNode.Parse(text); } catch (System.Text.Json.JsonException) { continue; }
            if (data?["Joints"] is not JsonObject j) continue;
            double D(string k) => j[k] is JsonValue x && x.TryGetValue<double>(out var d) ? d : 0;
            return new Joints(D("LeftArmElevation"), D("RightArmElevation"), D("HipFlexion"), D("KneeFlexion"));
        }
        return null;
    }

    // A Body Descriptors Object (PAF-BDO-V1.6) of a pose, as a capture device would give it.
    public static JsonObject Body(string id, Joints joints, double nowMs) => new()
    {
        ["Header"] = "PAF-BDO-V1.6", ["BodyDescriptorsObjectID"] = id, ["BodyDescriptorsObjectTime"] = XrvTime.Simple(id + "-t", nowMs),
        ["BodyDescriptorsData"] = new JsonArray(new JsonObject
        {
            ["Data"] = new JsonObject
            {
                ["Format"] = BodyFormat,
                ["Joints"] = new JsonObject { ["LeftArmElevation"] = joints.LeftArm, ["RightArmElevation"] = joints.RightArm, ["HipFlexion"] = joints.Hip, ["KneeFlexion"] = joints.Knee }
            }.ToJsonString()
        })
    };

    // The Visual Behaviour (XRV-VBH-V1.0) of a body.
    public static JsonObject Visual(string id, Joints joints, double nowMs, string? environment)
    {
        var behaviour = new JsonObject { ["StandSit"] = !joints.Sitting };
        if (joints.ArmsRaised)
            behaviour["Gestures"] = new JsonArray(new JsonObject { ["Gesture"] = "Raising arms", ["Intensity"] = Math.Round(Math.Clamp((joints.LeftArm + joints.RightArm) / 2 / 180 * 100, 0, 100)) });
        return new JsonObject
        {
            ["Header"] = "XRV-VBH-V1.0", ["VisualBehaviourID"] = id, ["UEnvironmentID"] = environment ?? "", ["VisualBehaviourTime"] = XrvTime.Simple(id + "-t", nowMs), ["Behaviour"] = behaviour
        };
    }

    // The PCM (16 kHz mono 16-bit, little-endian) an Audio Object carries in its first Basic Audio Object, or null.
    public static short[]? PcmOf(JsonNode? audioObject)
    {
        foreach (var b in (audioObject?["BasicAudioObjects"] as JsonArray) ?? [])
            foreach (var o in (b?["BAObjectIDOrBAObject"] as JsonArray) ?? [])
                foreach (var d in (o?["BasicAudioObjectData"] as JsonArray) ?? [])
                    if (d?["Data"] is JsonValue v && v.TryGetValue<string>(out var text))
                    {
                        byte[] bytes;
                        try { bytes = Convert.FromBase64String(text); } catch (FormatException) { continue; }
                        var pcm = new short[bytes.Length / 2];
                        Buffer.BlockCopy(bytes, 0, pcm, 0, pcm.Length * 2);
                        return pcm;
                    }
        return null;
    }

    // ClappingIntensity (0-100) of 16 kHz PCM. The applause of a crowd is a loud, broadband, noise-like sound: in 10 ms frames, the share of loud frames (above -40 dBFS) whose zero-crossing
    // rate is above 0.25 (speech and music stay well below it, a noise of many claps above) is the share of clapping; the intensity is that share (full at 60 %) times the loudness of
    // the loud frames (RMS in dBFS, -50 -> 0, -10 -> 1).
    public static double ClappingIntensity(short[] pcm, int rate = 16000)
    {
        var frame = rate / 100;
        var n = pcm.Length / frame;
        if (n < 10) return 0;
        int loudFrames = 0, clapFrames = 0;
        double loudEnergy = 0;
        for (var f = 0; f < n; f++)
        {
            double e = 0; var crossings = 0;
            for (var i = 0; i < frame; i++)
            {
                var s = pcm[f * frame + i] / 32768.0; e += s * s;
                if (i > 0 && (pcm[f * frame + i] >= 0) != (pcm[f * frame + i - 1] >= 0)) crossings++;
            }
            e /= frame;
            if (e < Math.Pow(10, -40 / 10.0)) continue;
            loudFrames++; loudEnergy += e;
            if (crossings / (double)(frame - 1) > 0.25) clapFrames++;
        }
        if (loudFrames == 0) return 0;
        var db = 10 * Math.Log10(loudEnergy / loudFrames);
        var loud = Math.Clamp((db + 50) / 40, 0, 1);
        var share = Math.Clamp(clapFrames / (double)n / 0.6, 0, 1);
        return Math.Round(100 * loud * share);
    }

    // The Audio Behaviour (XRV-ABH-V1.0) of an Audio Object, or null when it carries no PCM.
    public static JsonObject? Audio(string id, JsonNode? audioObject, double nowMs, string? environment)
    {
        if (PcmOf(audioObject) is not { } pcm) return null;
        return new JsonObject
        {
            ["Header"] = "XRV-ABH-V1.0", ["AudioBehaviourID"] = id, ["UEnvironmentID"] = environment ?? "", ["AudioBehaviourTime"] = XrvTime.Simple(id + "-t", nowMs),
            ["BehaviourData"] = new JsonObject { ["ClappingIntensity"] = ClappingIntensity(pcm) }
        };
    }

    // A Spatial Attitude read to the nearest mark: the Position to the nearest metre, the yaw to the nearest 45 degrees (in (-180, 180]), pitch and roll 0.
    public static JsonObject? Marked(JsonNode? attitude)
    {
        if (attitude is not JsonObject a) return null;
        var copy = (JsonObject)a.DeepClone();
        if (copy["Position"]?["CartPosition"] is JsonArray p)
            for (var i = 0; i < p.Count; i++) if (p[i] is JsonValue v && v.TryGetValue<double>(out var d)) p[i] = Math.Round(d) + 0.0;
        if (copy["Orientation"]?["Orientation"] is JsonArray o && o.Count == 3 && o[0] is JsonValue yv && yv.TryGetValue<double>(out var yaw))
        {
            var r = Math.Round(yaw / 45) * 45;
            while (r <= -180) r += 360;
            while (r > 180) r -= 360;
            o[0] = r + 0.0; o[1] = 0.0; o[2] = 0.0;
        }
        return copy;
    }

    // In plain words, what one Individual Status says (for the caption of the Action it triggers).
    // named: the elements a Cue target names (only those are said); null: all.
    public static string Words(JsonObject individual, JsonObject? named = null)
    {
        var id = individual.FirstOrDefault(p => p.Key.EndsWith("ID", StringComparison.Ordinal) && p.Key != "SeatID").Value?.GetValue<string>() ?? "someone";
        var parts = new List<string>();
        if (named is not null)
        {
            var keep = new JsonObject();
            foreach (var (k, v) in individual) if (named.ContainsKey(k)) keep[k] = v?.DeepClone();
            individual = keep;
        }
        if (individual["VisualBehaviour"]?["Behaviour"] is JsonObject vb)
        {
            if ((vb["Gestures"] as JsonArray)?.Select(g => g?["Gesture"]?.GetValue<string>()).OfType<string>().ToList() is { Count: > 0 } gestures) parts.Add(string.Join(", ", gestures).ToLowerInvariant());
            if (vb["StandSit"] is JsonValue ss && ss.TryGetValue<bool>(out var standing) && !standing) parts.Add("sitting");
        }
        if (individual["AudioBehaviour"]?["BehaviourData"]?["ClappingIntensity"] is JsonValue ci)
            parts.Add($"clapping {ci.GetValue<double>().ToString("0", CultureInfo.InvariantCulture)}/100");
        if (individual["SpatialAttitude"]?["Orientation"]?["Orientation"] is JsonArray o && o.Count > 0)
            parts.Add($"facing {o[0]!.GetValue<double>().ToString("0", CultureInfo.InvariantCulture)} degrees");
        if (individual["SeatID"] is JsonValue seat) parts.Add($"in seat {seat}");
        return parts.Count == 0 ? $"{id} is present" : $"{id} {string.Join(", ", parts)}";
    }
}
