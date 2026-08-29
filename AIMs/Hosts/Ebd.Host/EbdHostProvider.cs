using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Osd.VisualScene;   // YoloxObjectDetector
using Mpai.Paf.Ebd;           // EbdAimProcessor, BlazePoseEstimator

namespace Ebd.Host;

// Composition root for the PAF-EBD test host. Builds the Entity Body Description
// AIM, sharing one YOLOX detector (person localisation) and one BlazePose estimator
// (3D pose) so the models load once.
public sealed class EbdHostProvider : IAimProvider, IDisposable
{
    private readonly AmdStore _store;
    private YoloxObjectDetector? _yolox;
    private BlazePoseEstimator? _pose;

    public EbdHostProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "PAF-EBD-V1.6" =>
                new EbdAimProcessor(aimName, Yolox(settings), Pose(settings), AimPortReader.Load(_store, aimName)),

            _ => throw new NotSupportedException($"EbdHostProvider does not provide '{aimName}'.")
        };

    private YoloxObjectDetector Yolox(IReadOnlyDictionary<string, string> s) =>
        _yolox ??= new YoloxObjectDetector(Setting(s, "YoloxModel", @"D:\AI\Models\yolox_s.onnx"));

    private BlazePoseEstimator Pose(IReadOnlyDictionary<string, string> s) =>
        _pose ??= new BlazePoseEstimator(Setting(s, "BlazePoseModel", @"D:\AI\Models\pose_landmarks_detector_full.onnx"));

    private static string Setting(IReadOnlyDictionary<string, string> s, string key, string fallback) =>
        s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    public void Dispose() { _yolox?.Dispose(); _pose?.Dispose(); }
}
