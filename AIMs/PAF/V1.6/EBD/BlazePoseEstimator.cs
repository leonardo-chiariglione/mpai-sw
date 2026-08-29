using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mpai.Paf.Ebd;

// ---------------------------------------------------------------------------
//  BlazePose GHUM 3D body-landmark estimation (top-down).
//
//  Given an image cropped to one person, predicts a 3D body skeleton: 33 body
//  landmarks with world coordinates (metres, hip-centred), from the BlazePose
//  GHUM "full" landmarker (pose_landmarks_detector_full.onnx, Apache-2.0,
//  converted TFLite->ONNX by Unity). It is the body-pose analogue of
//  ScrfdFaceDetector / YoloxObjectDetector - one ONNX wrapped with ONNX Runtime
//  + ImageSharp.
//
//  ONNX signature (confirmed by probe):
//    input_1     [1,256,256,3]  NHWC RGB crop
//    Identity    [1,195]  39 landmarks x (x_px,y_px,z,visibility,presence) screen-space
//    Identity_1  [1,1]    pose presence logit
//    Identity_4  [1,117]  39 landmarks x (x,y,z) WORLD metres, hip-centred  <-- 3D used here
//  We take the first 33 landmarks (the standard BlazePose body topology), using
//  Identity_4 for 3D position and Identity for per-joint visibility, gating on
//  Identity_1 presence.
//
//  NOT COMPILE-VERIFIED / NOT RUN here. The input normalisation is the usual place
//  a BlazePose port needs tuning: this uses /255 -> [0,1]; if landmarks look wrong,
//  try [-1,1] ((v/127.5)-1). Verify on first inference (there is a standalone test).
// ---------------------------------------------------------------------------
public sealed class BlazePoseEstimator : IDisposable
{
    private readonly InferenceSession _session;
    private const int InputSize = 256;

    // The 33 standard BlazePose body landmark names, in model order.
    public static readonly string[] LandmarkNames =
    {
        "nose","left_eye_inner","left_eye","left_eye_outer","right_eye_inner","right_eye",
        "right_eye_outer","left_ear","right_ear","mouth_left","mouth_right",
        "left_shoulder","right_shoulder","left_elbow","right_elbow","left_wrist","right_wrist",
        "left_pinky","right_pinky","left_index","right_index","left_thumb","right_thumb",
        "left_hip","right_hip","left_knee","right_knee","left_ankle","right_ankle",
        "left_heel","right_heel","left_foot_index","right_foot_index"
    };

    public BlazePoseEstimator(string modelPath)
    {
        _session = new InferenceSession(modelPath);
    }

    // Estimate the 3D body pose from an image already cropped to one person.
    public BodyPoseResult Estimate(byte[] personCrop)
    {
        using var image = Image.Load<Rgb24>(personCrop);
        return Estimate(image);
    }

    public BodyPoseResult Estimate(Image<Rgb24> image)
    {
        // Resize (stretch) the crop to the model's 256x256 NHWC input, normalise /255.
        using var work = image.Clone(ctx => ctx.Resize(InputSize, InputSize));
        var input = new DenseTensor<float>(new[] { 1, InputSize, InputSize, 3 });
        work.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < InputSize; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < InputSize; x++)
                {
                    Rgb24 p = row[x];
                    input[0, y, x, 0] = p.R / 255f;
                    input[0, y, x, 1] = p.G / 255f;
                    input[0, y, x, 2] = p.B / 255f;
                }
            }
        });

        var inputName = _session.InputMetadata.Keys.First();
        using var results = _session.Run(new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, input)
        });

        var byName = results.ToDictionary(r => r.Name, r => r.AsTensor<float>());

        // Presence gate (Identity_1). Sigmoid of the logit.
        float presence = byName.TryGetValue("Identity_1", out var pres) ? Sigmoid(pres[0]) : 1f;

        var world  = byName["Identity_4"];   // [1,117] = 39 x (x,y,z) world metres
        var screen = byName.TryGetValue("Identity", out var sc) ? sc : null;  // [1,195] for visibility

        var keypoints = new List<BodyKeypoint>();
        for (int i = 0; i < LandmarkNames.Length; i++)   // first 33 = body topology
        {
            float wx = world[0, i * 3 + 0];
            float wy = world[0, i * 3 + 1];
            float wz = world[0, i * 3 + 2];
            float visibility = screen is not null ? Sigmoid(screen[0, i * 5 + 3]) : 1f;

            keypoints.Add(new BodyKeypoint
            {
                Name = LandmarkNames[i],
                X = wx, Y = wy, Z = wz,
                Visibility = visibility
            });
        }

        return new BodyPoseResult { Presence = presence, Keypoints = keypoints };
    }

    private static float Sigmoid(float x) => 1f / (1f + (float)Math.Exp(-x));

    public void Dispose() => _session.Dispose();
}

// A 3D body pose: presence + 33 world-space keypoints (metres, hip-centred).
public sealed class BodyPoseResult
{
    public float Presence { get; init; }
    public List<BodyKeypoint> Keypoints { get; init; } = new();
}

public sealed class BodyKeypoint
{
    public string Name { get; init; } = "";
    public float X { get; init; }        // world metres, hip-centred
    public float Y { get; init; }
    public float Z { get; init; }
    public float Visibility { get; init; }
}
