using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

using AifMessage = AIF.Controller.Message;

namespace AmqAif.Host;

// Scope A verification: run MMC-OCR-V2.5 THROUGH the Framework.
// Builds the Controller graph, instantiates the OCR AIM via the Provider,
// registers it in the AimHost, then calls AimHost.ProcessAsync(...) with an
// image on the InputVisual port - the real Framework routing path.
//
// Run with:  dotnet run -- --ocrtest
internal static class OcrFrameworkTest
{
    private const string AmdRepository = @"D:\AI\AIMs\AMDs";
    private const string SettingsFile  = @"D:\AI\AIMs\aim-settings.json";
    private const string ImagePath     = @"C:\Users\leona\Downloads\ocr-test.png";
    private const string AimName       = "MMC-OCR-V2.5";

    public static void Run()
    {
        Mpai.Core.AimLog.ToConsole();

        Console.WriteLine();
        Console.WriteLine("MMC-OCR-V2.5 through the AI Framework");
        Console.WriteLine();

        if (!File.Exists(ImagePath))
        {
            Console.WriteLine($"Image not found: {ImagePath}");
            return;
        }

        var store = new AmdStore(AmdRepository);
        store.Scan();
        var settings = AimSettings.Load(SettingsFile);

        // Instantiate the single OCR AIM via the Provider and register it
        // in the AimHost - this is what the Controller does for a leaf AIM.
        var host     = new AimHost();
        var provider = new AmqAifProvider(store, headless: true);
        host.RegisterRuntime(provider.Create(AimName, settings.For(AimName)));
        Console.WriteLine($"{AimName} instantiated and registered in the AimHost.");

        // Build the input image as a Visual Object on the InputVisual port.
        var bytes = File.ReadAllBytes(ImagePath);
        var image = BasicVisualObject.FromFile(ImagePath, bytes);

        var message = new AifMessage
        {
            MessageId   = Guid.NewGuid().ToString(),
            MessageType = "Recognise",
            Ports       = new Dictionary<string, string>
            {
                ["InputVisual"] = MpaiJson.ToJson(image)
            }
        };

        // Call the AIM THROUGH the Framework (AimHost), not directly.
        Console.WriteLine($"Calling AimHost.ProcessAsync(\"{AimName}\", ...)");
        var result = host.ProcessAsync(AimName, message)
                         .GetAwaiter().GetResult();

        if (result.IsError || result.IsCancelled)
        {
            Console.WriteLine($"OCR failed: {result.Payload}");
            return;
        }

        var recognised = MpaiJson.FromJson<RecognisedText>(result.Payload);

        Console.WriteLine();
        Console.WriteLine($"DataType returned: {result.DataType}");
        Console.WriteLine($"Recognised {recognised.TextLines.Count} lines through the Framework.");
        Console.WriteLine("First 15 lines:");
        Console.WriteLine(new string('-', 60));
        foreach (var line in recognised.TextLines.Take(15))
            Console.WriteLine($"  [{line.Confidence:0.00}]  {line.Text.GetText()}");
        Console.WriteLine(new string('-', 60));
    }
}
