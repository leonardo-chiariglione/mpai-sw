using System;
using System.IO;

using AIF.Controller;
using AIF.Store;
using AIF.GlobalStorage;

using Mpai.Core;
using Mpai.Gallery;
using Mpai.Aims.Visual;   // WebcamVisualAcquisition

namespace Hci.AccessControl.Host;

// The HCI "check authorised users" app (path A). Captures a face (live webcam)
// and a voice (a wav for now), describes both through the Controller (PAF-EFD /
// MMC-ESD), matches each against the gallery that enrolment populated, reconciles
// the two identities through HCI-IDR, and prints GRANT / DENY.
//
//   dotnet run --project Hosts\Hci.AccessControl.Host -- <wavPath>
internal static class Program
{
    private static void Main(string[] args)
    {
        AimLog.ToConsole();

        string wavPath = args.Length > 0 ? args[0] : @"D:\AI\TestData\Audio\leonardo.wav";

        var store = new AmdStore(@"D:\AI\AIMs\AMDs");
        store.Scan();
        var settings = AimSettings.Load(@"D:\AI\AIMs\aim-settings.json");

        // The same gallery (Global Storage) enrolment wrote to.
        var storage = new FileGlobalStorage(@"D:\AI\TestData\gallery-store", topAim: "Hci.Access");
        var gallery = new SubjectRepository(storage);

        // Capture the face live from the webcam.
        Console.WriteLine("Look at the camera...");
        string facePath = Path.Combine(@"D:\AI\TestData\Images", "access-probe.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(facePath)!);
        var webcam = new WebcamVisualAcquisition();
        var frame  = webcam.AcquireAsync(new VisualAcquisitionRequest()).GetAwaiter().GetResult();
        File.WriteAllBytes(facePath, frame.Data);
        Console.WriteLine($"  captured face -> {facePath} ({frame.Data.Length:N0} bytes)");

        var ua       = new UserAgent(store);
        var provider = new HciAccessControlProvider(store);
        var workflow = new HciAccessControlWorkflow(ua, provider, settings, gallery);

        Console.WriteLine("Checking authorisation...");
        var decision = workflow.CheckAuthorised(facePath, wavPath);

        Console.WriteLine();
        Console.WriteLine(decision.Granted
            ? $"=> ACCESS GRANTED: {decision.SubjectId}"
            : $"=> ACCESS DENIED");
        Console.WriteLine($"   ({decision.Reason})");
    }
}
