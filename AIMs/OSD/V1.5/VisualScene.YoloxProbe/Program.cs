using System;
using System.IO;
using System.Linq;

using Microsoft.ML.OnnxRuntime;

namespace Mpai.Osd.VisualScene.YoloxProbe;

// Dumps YOLOX-S ONNX I/O so we build the detector against the real signature.
//   Expected input:  [1,3,640,640] f32 (YOLOX: resize+pad to 640, NO /255 norm)
//   Expected output:  [1,8400,85]  f32  (8400 candidates x [cx,cy,w,h,obj,80cls])
//                     - may need grid/stride decode, or may be pre-decoded.
//   arg 0: model path (default D:\AI\Models\yolox_s.onnx)
public static class Program
{
    public static int Main(string[] args)
    {
        string modelPath = args.Length >= 1 ? args[0] : @"D:\AI\Models\yolox_s.onnx";
        if (!File.Exists(modelPath)) { Console.WriteLine($"Model not found: {modelPath}"); return 1; }

        Console.WriteLine($"Model: {modelPath}");
        Console.WriteLine($"Size:  {new FileInfo(modelPath).Length / 1024.0 / 1024.0:F1} MB");
        Console.WriteLine();

        using var session = new InferenceSession(modelPath);

        Console.WriteLine("== Inputs ==");
        foreach (var kv in session.InputMetadata)
            Console.WriteLine($"  {kv.Key}: [{string.Join(",", kv.Value.Dimensions)}]  {kv.Value.ElementType}");

        Console.WriteLine("== Outputs ==");
        foreach (var kv in session.OutputMetadata)
            Console.WriteLine($"  {kv.Key}: [{string.Join(",", kv.Value.Dimensions)}]  {kv.Value.ElementType}");

        Console.WriteLine();
        Console.WriteLine("Interpretation:");
        Console.WriteLine("  input [1,3,640,640] -> resize+pad image to 640, feed CHW (YOLOX: raw 0-255, no /255).");
        Console.WriteLine("  output [1,8400,85]  -> 8400 candidates: [cx,cy,w,h,obj,80 cls]; grid/stride decode + NMS.");
        Console.WriteLine("  (if output already [N,6] = [x1,y1,x2,y2,score,cls], it is pre-decoded.)");
        return 0;
    }
}
