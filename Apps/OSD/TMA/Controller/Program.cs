using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Aif.Api;
using Mpai.Aif.PortData;
using Mpai.Core;
using Mpai.Mas.Server;
using Mpai.Mas.Service;

namespace Mpai.Osd.Tma.Controller;

// TmaController - THE CONTROLLER of Television Media Analysis, a program of its own.
//
//   TmaController [--listen http://127.0.0.1:5060]
//
// The App is a User Agent: it reads its workflow, and the workflow asks this Controller, through the Controller API, to start its Module (the L3); this Controller starts the Module and its AIMs as
// the L3 says. The Controller API is carried over the routes of Mpai.Mas.Server, on the loopback. Nothing here is the person's: the App only supplies the devices.
//
// The models are given to the AIMs by the settings of this deployment, from Models\ (the L3s as the repository has them name remote Access Sources, which this Controller does not fetch: it holds
// a copy of the L3s of the Module without them).
internal static class Program
{
    private const string Module = "1OSD-TMA-V1.5-I01";

    private static async Task<int> Main(string[] args)
    {
        string? Value(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var url = Value("--listen") ?? "http://127.0.0.1:5060";
        Console.WriteLine("=== MPAI-OSD Television Media Analysis: the Controller ===");
        if (!url.StartsWith("http://127.0.0.1", StringComparison.Ordinal) && !url.StartsWith("http://localhost", StringComparison.Ordinal))
        { Console.WriteLine("FATAL: this Controller listens on the loopback only (no token, no certificate is configured)."); return 1; }

        var amds = MpaiPaths.Amds;
        if (!Directory.Exists(amds)) { Console.WriteLine($"FATAL: the AMD directory does not exist: {amds}"); return 1; }
        string Models(params string[] parts) => Path.Combine([MpaiPaths.Root, "Models", .. parts]);
        foreach (var needed in new[] { Models("ecapa-tdnn.onnx"), Models("scrfd_10g_bnkps.onnx"), Models("Whisper", "bin", "whisper-cli.exe"), Models("Whisper", "models", "ggml-small.bin") })
            if (!File.Exists(needed)) { Console.WriteLine($"FATAL: the model is not on this PC: {needed}"); return 1; }

        // The deployment: a copy of the L3s of the Module and of its AIMs, without their remote Access Sources, and the settings that give each AIM its models.
        var work = Path.Combine(Path.GetTempPath(), "mpai-tma-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(work);
        var module = JsonNode.Parse(File.ReadAllText(Path.Combine(amds, Module + ".json")))!.AsObject();
        var names = module["SubAIMs"]!.AsArray().Select(s => s!["Identifier"]!["AIMInstID"]!.GetValue<string>()).Append(Module).ToList();
        foreach (var aim in names)
        {
            var l3 = JsonNode.Parse(File.ReadAllText(Path.Combine(amds, aim + ".json")))!.AsObject();
            l3.Remove("AccessSources");
            File.WriteAllText(Path.Combine(work, aim + ".json"), l3.ToJsonString());
        }
        var arcFace = PinnedFaceModel() ?? Models("glintr100.onnx");
        var settings = new JsonObject
        {
            ["1CAE-AUS-V1.0-I01"] = new JsonObject { ["EcapaModel"] = Models("ecapa-tdnn.onnx") },
            ["1MMC-SIR-V2.5-I02"] = new JsonObject { ["EcapaModel"] = Models("ecapa-tdnn.onnx") },
            ["1PAF-FIR-V1.6-I02"] = new JsonObject { ["ScrfdModel"] = Models("scrfd_10g_bnkps.onnx"), ["ArcFaceModel"] = arcFace },
            ["1MMC-ASR-V2.5-I02"] = new JsonObject { ["ExecutablePath"] = Models("Whisper", "bin", "whisper-cli.exe"), ["ModelPath"] = Models("Whisper", "models", "ggml-small.bin"), ["LanguageCode"] = "en" },
            ["1CVE-VCD-V1.0-I01"] = new JsonObject { ["ImagesPerScene"] = "1", ["MinSceneLength"] = "15", ["SampleSeconds"] = "1" }
        };
        var settingsPath = Path.Combine(work, "settings.json");
        File.WriteAllText(settingsPath, settings.ToJsonString());

        PortDataSchema.Sink = (dataType, direction, complaint) => Console.WriteLine($"[SCHEMA] {dataType} {direction} {complaint}");
        var store = new AmdStore(work);
        store.Scan();
        using var north = new ControllerApi(work, settingsPath, s => new Provider.TmaProvider(s));
        var runner = new ControllerApiRunner(north, store);
        var server = new MasServer(runner, PortDataCodecs.Default(), url);
        Console.WriteLine($"  Listening:   {url}");
        Console.WriteLine($"  AMDs:        {work}");
        Console.WriteLine("The Controller is ready; the App starts its Module.");
        try { await server.RunAsync(); }
        finally { try { Directory.Delete(work, true); } catch { } }
        return 0;
    }

    // THE FACE MODEL THE L3 PINS: the one Access Control Registration registers faces with, where the Controller has obtained it on this PC (AIF\Access). Models\glintr100.onnx is another file of the
    // same name: its features cannot be compared with those of the registered faces.
    private static string? PinnedFaceModel()
    {
        var access = Path.Combine(MpaiPaths.Root, "AIF", "Access", "view");
        if (!Directory.Exists(access)) return null;
        foreach (var file in Directory.EnumerateFiles(access, "glintr100.onnx", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(file);
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).StartsWith("4AB1D6435D639628", StringComparison.Ordinal)) return file;
        }
        return null;
    }
}
