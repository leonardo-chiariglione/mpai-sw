using System;
using System.IO;
using System.Threading.Tasks;

namespace Mpai.Mas.Sci;

// SCI stand-in entry point. Loads AMQ + models server-side and serves the
// MPAI-MAS Remote API over HTTP for the RCA to call.
//
// Paths default to D:\AI layout; override via args:
//   Sci.Host [amdRepo] [settingsFile] [outputFolder] [listenUrl]
public static class Program
{
    public static async Task Main(string[] args)
    {
        string amdRepo      = args.Length > 0 ? args[0] : @"D:\AI\AIMs\AMDs";
        string settingsFile = args.Length > 1 ? args[1] : @"D:\AI\AIMs\aim-settings.json";
        string outputFolder = args.Length > 2 ? args[2] : @"D:\AI\MPAIApps\AMQ\Output";
        string listenUrl    = args.Length > 3 ? args[3] : "http://localhost:5005/";

        Console.WriteLine("=== MPAI-MAS SCI stand-in (AMQ) ===");
        Console.WriteLine($"  AMDs:     {amdRepo}");
        Console.WriteLine($"  Settings: {settingsFile}");
        Console.WriteLine($"  Output:   {outputFolder}");
        Console.WriteLine($"  Listen:   {listenUrl}");
        Console.WriteLine();

        var server = new SciServer(amdRepo, settingsFile, outputFolder, listenUrl);
        await server.RunAsync();
    }
}
