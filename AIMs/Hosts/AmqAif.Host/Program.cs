using System;
using System.Linq;
using System.Windows.Forms;

using AIF.Controller;
using AIF.Store;

using AmqAif.Host;

using Message = AIF.Controller.Message;   // disambiguate from WinForms Message

// ============================================================================
//  Runs an AIM on the AI Framework.
//
//  The host names one AIM and supplies a provider and settings. Everything
//  else comes from the Metadata: which SubAIMs exist (to any depth), what
//  resources they declare, the order they run in, and how their ports connect.
//
//    dotnet run                            -> MMC-AMQ-V2.5
//    dotnet run -- --headless              -> files only: no devices, no UI
// ============================================================================
internal static class Program
{
    private const string DefaultAim = "MMC-AMQ-V2.5";
    private const string AmdRepository = @"D:\AI\AIMs\AMDs";
    private const string SettingsFile = @"D:\AI\AIMs\aim-settings.json";

    [STAThread]
    private static void Main(
        string[] args)
    {
        Application.EnableVisualStyles();

        if (System.Array.Exists(args, a => string.Equals(a, "--translatetest", System.StringComparison.OrdinalIgnoreCase)))
        {
            TranslateTest.Run();
            return;
        }

        if (System.Array.Exists(args, a => string.Equals(a, "--graphtest", System.StringComparison.OrdinalIgnoreCase)))
        {
            GraphTest.Run();
            return;
        }

        if (System.Array.Exists(args, a => string.Equals(a, "--tokentest", System.StringComparison.OrdinalIgnoreCase)))
        {
            TokeniserTest.Run();
            return;
        }

        if (System.Array.Exists(args, a => string.Equals(a, "--tstlive", System.StringComparison.OrdinalIgnoreCase)))
        {
            TstLiveTest.Run();
            return;
        }

        if (System.Array.Exists(args, a => string.Equals(a, "--tstvoice", System.StringComparison.OrdinalIgnoreCase)))
        {
            TstVoiceTest.Run();
            return;
        }

        if (System.Array.Exists(args, a => string.Equals(a, "--tsttest", System.StringComparison.OrdinalIgnoreCase)))
        {
            TstTest.Run();
            return;
        }

        if (System.Array.Exists(args, a => string.Equals(a, "--suspendtest", System.StringComparison.OrdinalIgnoreCase)))
        {
            SuspendResumeTest.Run();
            return;
        }

        if (System.Array.Exists(args, a => string.Equals(a, "--workflowtest", System.StringComparison.OrdinalIgnoreCase)))
        {
            WorkflowTest.Run(args);
            return;
        }

        if (System.Array.Exists(args, a => string.Equals(a, "--ocrtest", System.StringComparison.OrdinalIgnoreCase)))
        {
            OcrFrameworkTest.Run();
            return;
        }

        Mpai.Core.AimLog.ToConsole();

        var headless =
            Array.Exists(
                args,
                argument => string.Equals(
                    argument,
                    "--headless",
                    StringComparison.OrdinalIgnoreCase));

        var named =
            Array.FindAll(
                args,
                argument => !argument.StartsWith(
                    "--",
                    StringComparison.Ordinal));

        var aimName =
            named.Length > 0
                ? named[0]
                : DefaultAim;

        Console.WriteLine();
        Console.WriteLine(
            $"{aimName} on AIF" +
            (headless
                ? "   [headless: files only, no devices, no UI]"
                : ""));
        Console.WriteLine();

        // 1. AIM Metadata repository and deployment settings.
        var store =
            new AmdStore(AmdRepository);

        store.Scan();

        var settings =
            AimSettings.Load(SettingsFile);

        // 2. Resolve the AIM name to a concrete Level-3 AIM Metadata instance.
        var selected =
            store.GetCatalog()
                 .FirstOrDefault(
                     item => item.AIMName == aimName);

        if (selected is null)
        {
            Console.WriteLine(
                $"AIM Metadata not found for {aimName}.");

            return;
        }

        var identifier =
            new Identifier
            {
                AIMName =
                    selected.AIMName,

                ImplementerID =
                    selected.ImplementerID,

                ImplementationID =
                    selected.ImplementationID
            };

        // 3. Build the hierarchy.
        var controller =
            new Controller(store);

        var graph =
            controller.RegisterAim(identifier);

        Console.WriteLine("Hierarchy from Metadata:");
        Show(graph.Root, 1);
        Console.WriteLine();

        // 4. Create the runtime AIW instance.
        var machineInstantiator =
            new MachineInstantiator();

        var machine =
            machineInstantiator.Instantiate(graph);

        Console.WriteLine(
            $"Machine ID: {machine.MachineId}");

        Console.WriteLine();

        // 5. Instantiate the Basic AIMs.
        var host =
            new AimHost();

        var instantiated =
            controller.Instantiate(
                machine.DescriptorGraph,
                new AmqAifProvider(store, headless),
                settings,
                host);

        Console.WriteLine(
            "Instantiated Basic AIMs:      " +
            string.Join(", ", instantiated));

        // 6. Execute.
        var executor =
            new MachineExecutor(host);

        Console.WriteLine(
            "Execution plan (top level):   " +
            string.Join(
                "  ->  ",
                executor.Plan(
                    machine.DescriptorGraph)));

        Console.WriteLine();

        var result =
            executor.ExecuteAsync(
                machine.DescriptorGraph,
                new Message
                {
                    MessageId =
                        Guid.NewGuid().ToString(),

                    MessageType =
                        "Start"
                })
                .GetAwaiter()
                .GetResult();

        Console.WriteLine();

        if (result.IsCancelled)
        {
            Console.WriteLine(
                $"Cancelled at {result.FailedAim}: {result.Payload}");
        }
        else if (result.IsError)
        {
            Console.WriteLine(
                $"Failed at {result.FailedAim}: {result.Payload}");
        }
        else
        {
            Console.WriteLine(
                $"Result: {result.DataType}  ({result.Payload.Length:N0} chars)");
        }

        Console.WriteLine();
    }

    private static void Show(
        DescriptorNode node,
        int depth)
    {
        Console.WriteLine(
            new string(' ', depth * 4) +
            node.AIMName +
            (node.IsComposite
                ? "   [Composite]"
                : "   [Basic]"));

        foreach (var child in node.Children)
        {
            Show(
                child,
                depth + 1);
        }
    }
}