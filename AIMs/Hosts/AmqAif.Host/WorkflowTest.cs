using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AIF.Store;

using Mpai.Core;

namespace AmqAif.Host;

// Proves Phase 1 (steps 1-17) of the AMQ workflow through the User Agent API.
// Run with:  dotnet run -- --workflowtest
internal static class WorkflowTest
{
    private const string AmdRepository = @"D:\AI\AIMs\AMDs";
    private const string SettingsFile  = @"D:\AI\AIMs\aim-settings.json";
    private const string ImagePath     = @"C:\Users\leona\Downloads\ocr-test.png";

    public static void Run()
    {
        Mpai.Core.AimLog.ToConsole();
        Console.WriteLine();
        Console.WriteLine("AMQ Workflow - Phase 1 (folder OCR) through the User Agent");
        Console.WriteLine();

        if (!File.Exists(ImagePath))
        {
            Console.WriteLine($"Image not found: {ImagePath}");
            return;
        }

        var store = new AmdStore(AmdRepository);
        store.Scan();
        var settings = AimSettings.Load(SettingsFile);

        var ua       = new AIF.Controller.UserAgent(store);
        var provider = new AmqAifProvider(store, headless: true);

        var workflow = new AmqWorkflow(ua, provider, settings)
        {
            // The User Agent's boundary primitives (headless stand-ins):
            CaptureImageFromUser = prompt =>
            {
                Console.WriteLine($"[UA -> User] {prompt}");
                Console.WriteLine($"[User -> UA] (headless) sending {Path.GetFileName(ImagePath)}");
                return Task.FromResult(File.ReadAllBytes(ImagePath));
            },
            DisplayRecognisedText = text =>
            {
                Console.WriteLine();
                Console.WriteLine($"[UA -> User] Recognised {text.TextLines.Count} lines. First 12:");
                Console.WriteLine(new string('-', 60));
                foreach (var line in text.TextLines.Take(12))
                    Console.WriteLine($"  [{line.Confidence:0.00}]  {line.Text.GetText()}");
                Console.WriteLine(new string('-', 60));
                return Task.CompletedTask;
            }
        };

        try
        {
            var recognised = workflow.RunFolderOcrPhaseAsync().GetAwaiter().GetResult();
            Console.WriteLine();
            Console.WriteLine($"Phase 1 complete. RecognisedText carried {recognised.TextLines.Count} lines " +
                              "across the boundary Ports via the MPAI_AIFU_* API.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Workflow failed: " + ex.Message);
            Console.WriteLine(ex);
        }
    }
}
