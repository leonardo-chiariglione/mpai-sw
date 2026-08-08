using System;

using AIF.Controller;
using AIF.Store;

using Amq.Linux.Host;

// ============================================================================
//  Runs an AIM on the AI Framework, on Linux.
//
//    dotnet run                          MMC-AMQ-V2.5, files only
//    dotnet run -- MMC-AMQ2-V2.5         the hierarchical composite
//    dotnet run -- --devices             ALSA capture and playback
//
//  Paths come from aim-settings.json next to this program, or from the path
//  given in AIM_SETTINGS.
// ============================================================================
internal static class Program
{
    private const string DefaultAim = "MMC-AMQ-V2.5";

    private static int Main(
        string[] args)
    {
        var useDevices =
            Array.Exists(
                args,
                a => string.Equals(a, "--devices", StringComparison.OrdinalIgnoreCase));

        var named =
            Array.FindAll(
                args,
                a => !a.StartsWith("--", StringComparison.Ordinal));

        var aimName =
            named.Length > 0 ? named[0] : DefaultAim;

        var repository =
            Environment.GetEnvironmentVariable("AMD_REPOSITORY")
            ?? "../../AMDs";

        var settingsFile =
            Environment.GetEnvironmentVariable("AIM_SETTINGS")
            ?? "aim-settings.json";

        // The AIMs report through Mpai.Core.AimLog; this host prints them.
        Mpai.Core.AimLog.ToConsole();

        Console.WriteLine();
        Console.WriteLine(
            $"{aimName} on AIF (Linux)" +
            (useDevices ? "   [ALSA devices]" : "   [files only]"));
        Console.WriteLine();

        var store = new AmdStore(repository);
        store.Scan();

        var settings = AimSettings.Load(settingsFile);

        var controller = new Controller(store);
        var graph = controller.RegisterAim(aimName);

        Console.WriteLine("Hierarchy from Metadata:");
        Show(graph.Root, 1);
        Console.WriteLine();

        var host = new AimHost();

        var instantiated =
            controller.Instantiate(
                graph,
                new LinuxAimProvider(useDevices),
                settings,
                host);

        Console.WriteLine(
            "Instantiated Basic AIMs:      " +
            string.Join(", ", instantiated));

        var executor = new MachineExecutor(host);

        Console.WriteLine(
            "Execution plan (top level):   " +
            string.Join("  ->  ", executor.Plan(graph)));
        Console.WriteLine();

        var result =
            executor.ExecuteAsync(
                graph,
                new Message
                {
                    MessageId = Guid.NewGuid().ToString(),
                    MessageType = "Start"
                })
                .GetAwaiter()
                .GetResult();

        Console.WriteLine();

        if (result.IsCancelled)
        {
            Console.WriteLine($"Cancelled at {result.FailedAim}: {result.Payload}");
            return 2;
        }

        if (result.IsError)
        {
            Console.WriteLine($"Failed at {result.FailedAim}: {result.Payload}");
            return 1;
        }

        Console.WriteLine(
            $"Result: {result.DataType}  ({result.Payload.Length:N0} chars)");
        Console.WriteLine();
        return 0;
    }

    private static void Show(
        DescriptorNode node,
        int depth)
    {
        Console.WriteLine(
            new string(' ', depth * 4) +
            node.AIMName +
            (node.IsComposite ? "   [Composite]" : "   [Basic]"));

        foreach (var child in node.Children)
        {
            Show(child, depth + 1);
        }
    }
}

