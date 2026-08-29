using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Mpai.Paf.Ebd;

// Encodes a single-frame body pose (33 BlazePose world-space 3D joints, metres,
// hip-centred) as a BVH skeleton - one of the canonical Body Descriptors content
// formats. BVH (BioVision Hierarchy) is natively a motion-capture format: a joint
// HIERARCHY with per-joint OFFSETs, followed by MOTION frames of channel values.
//
// We have one frame of 3D joint POSITIONS (not rotations). This writer builds a
// human body hierarchy from the BlazePose joints, sets each joint's OFFSET to its
// position relative to its parent (so the rest pose already expresses the captured
// posture), and emits ONE motion frame with the root world position and zero
// rotations. The posture is therefore carried by the offsets - enough to express
// body-language semantics for Personal Status. A rotation-native encoding (IK-
// derived Euler angles per frame, or an SMPL export) is a future refinement.
public static class BvhWriter
{
    // Parent for each BlazePose joint, forming a body tree rooted at a synthetic
    // pelvis (midpoint of the hips). -1 marks the root's parent.
    // We use a compact, anatomically sensible hierarchy over the 33 landmarks.
    private static readonly (string Name, string Parent)[] Hierarchy =
    {
        ("pelvis",           ""),               // synthetic root = hip midpoint
        ("left_hip",         "pelvis"),
        ("left_knee",        "left_hip"),
        ("left_ankle",       "left_knee"),
        ("left_heel",        "left_ankle"),
        ("left_foot_index",  "left_ankle"),
        ("right_hip",        "pelvis"),
        ("right_knee",       "right_hip"),
        ("right_ankle",      "right_knee"),
        ("right_heel",       "right_ankle"),
        ("right_foot_index", "right_ankle"),
        ("spine",            "pelvis"),          // synthetic = shoulder midpoint
        ("left_shoulder",    "spine"),
        ("left_elbow",       "left_shoulder"),
        ("left_wrist",       "left_elbow"),
        ("left_thumb",       "left_wrist"),
        ("left_index",       "left_wrist"),
        ("left_pinky",       "left_wrist"),
        ("right_shoulder",   "spine"),
        ("right_elbow",      "right_shoulder"),
        ("right_wrist",      "right_elbow"),
        ("right_thumb",      "right_wrist"),
        ("right_index",      "right_wrist"),
        ("right_pinky",      "right_wrist"),
        ("nose",             "spine"),
        ("left_eye",         "nose"),
        ("right_eye",        "nose"),
        ("left_ear",         "nose"),
        ("right_ear",        "nose"),
        ("mouth_left",       "nose"),
        ("mouth_right",      "nose"),
    };

    public static string Write(BodyPoseResult pose)
    {
        var pos = pose.Keypoints.ToDictionary(k => k.Name, k => (k.X, k.Y, k.Z));

        // Synthetic joints: pelvis = hip midpoint, spine = shoulder midpoint.
        (float X, float Y, float Z) Mid((float, float, float) a, (float, float, float) b)
            => ((a.Item1 + b.Item1) / 2f, (a.Item2 + b.Item2) / 2f, (a.Item3 + b.Item3) / 2f);

        var pelvis = Mid(pos["left_hip"], pos["right_hip"]);
        var spine  = Mid(pos["left_shoulder"], pos["right_shoulder"]);
        pos["pelvis"] = pelvis;
        pos["spine"]  = spine;

        var parentOf = Hierarchy.ToDictionary(h => h.Name, h => h.Parent);
        var childrenOf = Hierarchy
            .Where(h => h.Parent != "")
            .GroupBy(h => h.Parent)
            .ToDictionary(g => g.Key, g => g.Select(h => h.Name).ToList());

        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        // ---- HIERARCHY ----
        sb.AppendLine("HIERARCHY");
        WriteJoint(sb, "pelvis", pos, parentOf, childrenOf, isRoot: true, indent: 0, ci);

        // ---- MOTION (one frame) ----
        // Root gets 6 channels (position + rotation); every other joint 3 (rotation).
        // We emit the root world position and zero rotations everywhere (posture is in
        // the OFFSETs). Channel order matches the HIERARCHY declaration order.
        sb.AppendLine("MOTION");
        sb.AppendLine("Frames: 1");
        sb.AppendLine("Frame Time: 0.033333");

        var frame = new List<string>();
        // Root: Xposition Yposition Zposition Zrotation Xrotation Yrotation
        frame.Add(pelvis.X.ToString("F6", ci));
        frame.Add(pelvis.Y.ToString("F6", ci));
        frame.Add(pelvis.Z.ToString("F6", ci));
        frame.Add("0.000000"); frame.Add("0.000000"); frame.Add("0.000000");
        // Every non-root joint in declaration order: 3 zero rotations.
        foreach (var h in Hierarchy.Skip(1))
        { frame.Add("0.000000"); frame.Add("0.000000"); frame.Add("0.000000"); }
        sb.AppendLine(string.Join(" ", frame));

        return sb.ToString();
    }

    private static void WriteJoint(
        StringBuilder sb, string name,
        Dictionary<string, (float X, float Y, float Z)> pos,
        Dictionary<string, string> parentOf,
        Dictionary<string, List<string>> childrenOf,
        bool isRoot, int indent, CultureInfo ci)
    {
        string pad = new string(' ', indent * 2);
        sb.AppendLine($"{pad}{(isRoot ? "ROOT" : "JOINT")} {name}");
        sb.AppendLine($"{pad}{{");

        // OFFSET = this joint's position relative to its parent (metres). For the
        // root, offset is zero (its world position is in the motion frame).
        float ox = 0, oy = 0, oz = 0;
        if (!isRoot)
        {
            var p = pos[name];
            var par = pos[parentOf[name]];
            ox = p.X - par.X; oy = p.Y - par.Y; oz = p.Z - par.Z;
        }
        sb.AppendLine($"{pad}  OFFSET {ox.ToString("F6", ci)} {oy.ToString("F6", ci)} {oz.ToString("F6", ci)}");
        sb.AppendLine(isRoot
            ? $"{pad}  CHANNELS 6 Xposition Yposition Zposition Zrotation Xrotation Yrotation"
            : $"{pad}  CHANNELS 3 Zrotation Xrotation Yrotation");

        if (childrenOf.TryGetValue(name, out var kids))
            foreach (var kid in kids)
                WriteJoint(sb, kid, pos, parentOf, childrenOf, isRoot: false, indent + 1, ci);
        else
        {
            // Leaf: a small End Site so the BVH is well-formed.
            sb.AppendLine($"{pad}  End Site");
            sb.AppendLine($"{pad}  {{");
            sb.AppendLine($"{pad}    OFFSET 0.000000 0.000000 0.000000");
            sb.AppendLine($"{pad}  }}");
        }
        sb.AppendLine($"{pad}}}");
    }
}
