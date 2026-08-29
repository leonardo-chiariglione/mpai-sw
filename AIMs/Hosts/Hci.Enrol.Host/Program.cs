using System;
using System.IO;

using AIF.Controller;
using AIF.Store;
using AIF.GlobalStorage;

using Mpai.Core;
using Mpai.Gallery;
using Mpai.Aims.Visual;   // WebcamVisualAcquisition

namespace Hci.Enrol.Host;

// The HCI enrolment app. Captures a subject's face (live webcam) and voice (a wav
// for now - live mic is the same upgrade the access host awaits), describes both
// through the Controller (PAF-EFD / MMC-ESD), and stores the resulting standard
// Descriptors Objects in the gallery (AIF Global Storage).
//
//   dotnet run --project Hosts\Hci.Enrol.Host -- <subjectId> <wavPath>
//
// The face is captured live from the camera; the voice wav path is supplied.
internal static class Program
{
    private static void Main(string[] args)
    {
        AimLog.ToConsole();

        Console.Write("Subject name: ");
        string subjectId = args.Length > 0 ? args[0] : (Console.ReadLine() ?? "").Trim();
        if (string.IsNullOrWhiteSpace(subjectId)) { Console.WriteLine("No subject name."); return; }

        string wavPath = args.Length > 1 ? args[1] : @"D:\AI\TestData\Audio\leonardo.wav";

        // Store setup: the AmdStore (AIM catalogue) and AimSettings.
        var store = new AmdStore(@"D:\AI\AIMs\AMDs");
        store.Scan();
        var settings = AimSettings.Load(@"D:\AI\AIMs\aim-settings.json");

        // The gallery: a Repository over AIF Global Storage. The host constructs
        // the storage instance (Controller storage-plumbing is a framework
        // follow-up) with this app as the stamped Top AIM.
        var storage = new FileGlobalStorage(@"D:\AI\TestData\gallery-store", topAim: "Hci.Enrol");
        var gallery = new SubjectRepository(storage);

        // Capture the face live from the webcam and save it, so the UA can hand
        // the captured frame to the Controller for description.
        Console.WriteLine("Look at the camera...");
        string facePath = Path.Combine(@"D:\AI\TestData\Images", $"enrol-{subjectId}.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(facePath)!);
        var webcam = new WebcamVisualAcquisition();
        var frame  = webcam.AcquireAsync(new VisualAcquisitionRequest()).GetAwaiter().GetResult();
        File.WriteAllBytes(facePath, frame.Data);
        Console.WriteLine($"  captured face -> {facePath} ({frame.Data.Length:N0} bytes)");

        // Drive the enrolment through the User Agent.
        var ua = new UserAgent(store);
        var provider = new HciEnrolProvider(store);
        var workflow = new HciEnrolWorkflow(ua, provider, settings, gallery);

        Console.WriteLine($"Enrolling '{subjectId}'...");
        bool ok = workflow.Enrol(subjectId, facePath, wavPath);

        Console.WriteLine(ok
            ? $"=> ENROLLED: {subjectId}"
            : $"=> enrolment did not complete for {subjectId}.");
    }
}
