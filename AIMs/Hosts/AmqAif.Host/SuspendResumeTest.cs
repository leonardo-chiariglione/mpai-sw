using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

using AifMessage = AIF.Controller.Message;

namespace AmqAif.Host;

// Step 3 proof: drive the AMQ composite through the User Agent using
// Suspend/Resume. The UA writes boundary PORTS and reacts to requests for
// more input. It never names an AIM nor orders execution.
//
//   1. Start AMQ (MPAI_AIFU_AIW_Start)
//   2. Write the folder screenshot to InputFolderImage; run.
//      -> OCR (the only AIM whose boundary input is present) runs.
//      -> the composite suspends needing InputVisual (the chosen image).
//   3. Read OCR's Recognised Text from OutputListing (partial outputs).
//   4. Write the chosen image to InputVisual; resume.
//      -> VOA runs; the composite suspends needing InputAudio (spoken question).
//
// Run with:  dotnet run -- --suspendtest
internal static class SuspendResumeTest
{
    private const string AmdRepository = @"D:\AI\AIMs\AMDs";
    private const string SettingsFile  = @"D:\AI\AIMs\aim-settings.json";
    private const string FolderShot    = @"C:\Users\Leonardo\Downloads\ocr-test.png";
    private const string ChosenImage   = @"C:\Users\Leonardo\Downloads\zebra.jpg";
    private const string QuestionAudio = @"C:\Users\Leonardo\Downloads\question.wav";

    public static void Run()
    {
        Mpai.Core.AimLog.ToConsole();
        Console.WriteLine();
        Console.WriteLine("AMQ via User Agent - Suspend/Resume (boundary ports, no micromanagement)");
        Console.WriteLine();

        if (!File.Exists(FolderShot))  { Console.WriteLine($"Missing: {FolderShot}");  return; }
        if (!File.Exists(ChosenImage)) { Console.WriteLine($"Missing: {ChosenImage}"); return; }

        var store = new AmdStore(AmdRepository);
        store.Scan();
        var settings = AimSettings.Load(SettingsFile);

        var ua       = new UserAgent(store);
        var provider = new AmqAifProvider(store, headless: true);

        ua.MPAI_AIFU_Controller_Initialize();
        var err = ua.MPAI_AIFU_AIW_Start("MMC-AMQ-V2.5", provider, settings, out var aiwId);
        if (err != AifError.OK) { Console.WriteLine($"AIW_Start failed: {err}"); return; }
        Console.WriteLine($"[UA] AIW_Start MMC-AMQ-V2.5 -> AIW_ID {aiwId}");

        // Step 2: write the folder screenshot to InputFolderImage; run.
        var folderImage = BasicVisualObject.FromFile(FolderShot, File.ReadAllBytes(FolderShot));
        Console.WriteLine("[UA] Port_Input_Write folder screenshot -> InputFolderImage; run.");

        var (e1, o1) = ua.RunAsync(aiwId, new Dictionary<string, string>
        {
            ["InputFolderImage"] = MpaiJson.ToJson(folderImage)
        }).GetAwaiter().GetResult();
        if (e1 != AifError.OK || o1 is null) { Console.WriteLine($"Run failed: {e1}"); return; }

        // Step 3: read OCR's Recognised Text from OutputListing.
        if (o1.PartialOutputs is not null &&
            o1.PartialOutputs.TryGetValue("OutputListing", out var listingJson))
        {
            var listing = MpaiJson.FromJson<RecognisedText>(listingJson);
            Console.WriteLine($"[UA] OutputListing: {listing.TextLines.Count} lines. Sample:");
            foreach (var line in listing.TextLines.Take(8))
                Console.WriteLine($"        {line.Text.GetText()}");
        }
        else
        {
            Console.WriteLine("[UA] (no OutputListing yet)");
        }

        if (o1.Suspended)
            Console.WriteLine($"[AIF] Suspended; composite needs boundary port '{o1.WaitingPort}'.");

        // Step 4: write the chosen image to InputVisual; resume.
        var chosen = BasicVisualObject.FromFile(ChosenImage, File.ReadAllBytes(ChosenImage));
        Console.WriteLine($"[UA] Port_Input_Write chosen image ({Path.GetFileName(ChosenImage)}) " +
                          "-> InputVisual; resume.");

        var (e2, o2) = ua.ResumeAsync(aiwId, new Dictionary<string, string>
        {
            ["InputVisual"] = MpaiJson.ToJson(chosen)
        }).GetAwaiter().GetResult();
        if (e2 != AifError.OK || o2 is null) { Console.WriteLine($"Resume failed: {e2}"); return; }

        if (!o2.Suspended)
        {
            Console.WriteLine($"[AIF] Run completed early. DataType: {o2.Completed?.DataType}");
            return;
        }
        Console.WriteLine($"[AIF] Suspended again; composite needs boundary port '{o2.WaitingPort}'.");

        // Step 5: write the spoken question to InputSpeech; resume -> answer pipeline.
        // The composite's boundary port is InputSpeech (OSD-SPO-V1.5), feeding
        // MMC-SOA, not InputAudio: SOA produces a Speech Object where AOA produced
        // an Audio Object. Writing InputAudio left the run suspended for ever,
        // waiting on a port nobody was filling.
        if (!File.Exists(QuestionAudio)) { Console.WriteLine($"Missing: {QuestionAudio}"); return; }
        var speech = BasicSpeechObject.FromData(File.ReadAllBytes(QuestionAudio));
        Console.WriteLine($"[UA] Port_Input_Write question audio ({Path.GetFileName(QuestionAudio)}) " +
                          "-> InputSpeech; resume.");

        // "Ask your question as speech or text; the machine uses the one available."
        // The user chose speech, so supply the audio and an EMPTY text object on
        // InputText - TIQ will use the transcribed speech and ignore the empty text.
        var emptyText = BasicTextObject.FromText(string.Empty);
        var (e3, o3) = ua.ResumeAsync(aiwId, new Dictionary<string, string>
        {
            ["InputSpeech"] = MpaiJson.ToJson(speech),
            ["InputText"]  = MpaiJson.ToJson(emptyText)
        }).GetAwaiter().GetResult();
        if (e3 != AifError.OK || o3 is null) { Console.WriteLine($"Resume failed: {e3}"); return; }

        if (o3.Suspended)
        {
            Console.WriteLine($"[AIF] Suspended again; composite needs boundary port '{o3.WaitingPort}'.");
        }
        else if (o3.Completed is not null)
        {
            var c = o3.Completed;
            Console.WriteLine();
            Console.WriteLine("=== RUN COMPLETED ===");
            Console.WriteLine($"Final DataType: {c.DataType}");
            Console.WriteLine($"Composite output ports: {string.Join(", ", c.Ports.Keys)}");
            if (c.Ports.TryGetValue("OutputText", out var answerJson))
            {
                try
                {
                    var answer = MpaiJson.FromJson<BasicTextObject>(answerJson);
                    Console.WriteLine($"Spoken/text answer: {answer.GetText()}");
                }
                catch { Console.WriteLine("(OutputText present)"); }
            }
            if (c.Ports.ContainsKey("OutputAudio"))  Console.WriteLine("OutputAudio (spoken answer) produced.");
            if (c.Ports.ContainsKey("OutputVisual")) Console.WriteLine("OutputVisual (answer frame) produced.");
        }

        Console.WriteLine();
        Console.WriteLine("Full AMQ flow exercised via boundary ports; the UA never named an AIM.");
    }
}
