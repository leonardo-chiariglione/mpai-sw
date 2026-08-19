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
    // These were three compiled-in constants:
    //
    //     @"D:\AI\AIMs\AMDs"
    //     @"D:\AI\AIMs\aim-settings.json"
    //     @"C:\Users\leona\Downloads\ocr-test.png"
    //
    // one machine's drive layout and, in the third, a username belonging to a
    // different account - so the test failed before it began on a machine whose
    // profile is named otherwise. A path that names a person cannot be right for
    // anyone but that person.
    public static void Run(string[]? args = null)
    {
        Mpai.Core.AimLog.ToConsole();
        Console.WriteLine();
        Console.WriteLine("AMQ Workflow - Phase 1 (folder OCR) through the User Agent");
        Console.WriteLine();

        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.WriteLine("Could not find AIMs/AMDs above this executable.");
            return;
        }

        var imagePath = ImageFrom(args);

        if (!File.Exists(imagePath))
        {
            Console.WriteLine($"Image not found: {imagePath}");
            Console.WriteLine("Pass one with:  dotnet run -- --workflowtest --image <path>");
            return;
        }

        Console.WriteLine($"  image: {imagePath}");

        var store = new AmdStore(Path.Combine(repoRoot, "AIMs", "AMDs"));
        store.Scan();
        var settings = AimSettings.Load(Path.Combine(repoRoot, "AIMs", "aim-settings.json"));

        var ua       = new AIF.Controller.UserAgent(store);
        var provider = new AmqAifProvider(store, headless: true);

        var workflow = new AmqWorkflow(ua, provider, settings)
        {
            // The User Agent's boundary primitives (headless stand-ins):
            CaptureImageFromUser = prompt =>
            {
                Console.WriteLine($"[UA -> User] {prompt}");
                Console.WriteLine($"[User -> UA] (headless) sending {Path.GetFileName(imagePath)}");
                return Task.FromResult(File.ReadAllBytes(imagePath));
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
        finally
        {
            // Phase 1 leaves the AMQ AIW running on purpose - the later phases
            // need it - so the choreography ends where the caller says it does.
            workflow.Stop();
        }
    }
    // --image wins; otherwise THIS user's Downloads, which is where the fixture
    // has always been - just not under the name that was compiled in.
    private static string ImageFrom(string[]? args)
    {
        if (args is not null)
        {
            var named = Array.IndexOf(args, "--image");
            if (named >= 0 && named + 1 < args.Length) return args[named + 1];
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "ocr-test.png");
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "AIMs", "AMDs")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }}