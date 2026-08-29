using System;
using System.IO;

using Mpai.Core;
using Mpai.Aims.Visual;

// Standalone proof that live webcam capture works, in isolation, before it is
// wired into any app. Grabs one frame and writes it to disk so it can be eyeballed.
//
//   dotnet run --project CVE\V1.0\VOA.Windows.WebcamTest
internal static class Program
{
    private static void Main(string[] args)
    {
        AimLog.ToConsole();

        int cameraIndex = args.Length > 0 && int.TryParse(args[0], out var i) ? i : 0;
        string outPath  = args.Length > 1 ? args[1] : @"D:\AI\TestData\Images\webcam-test.jpg";

        Console.WriteLine($"Capturing one frame from camera {cameraIndex}...");

        var voa = new WebcamVisualAcquisition(cameraIndex);
        var obj = voa.AcquireAsync(new VisualAcquisitionRequest()).GetAwaiter().GetResult();

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllBytes(outPath, obj.Data);

        Console.WriteLine($"Wrote {obj.Data.Length:N0} bytes to {outPath}");
        Console.WriteLine("Open it to confirm the camera captured a real frame.");
    }
}
