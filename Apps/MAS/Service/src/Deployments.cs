using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

using Mpai.Core;

namespace Mpai.Mas.Service;

// THE DEPLOYMENT OF THE APPS WHOSE CONTROLLER PREPARES IT (N-49, 2026-11-24). Two Apps of this machine get from their own Controller more than the L3s of AIMs\AMDs and the settings of
// AIMs\aim-settings.json, and the instance does for them what those Controllers do:
//   ASM (CAE-ASM): the Access of the JSON Interface read from the files of this software (AsmVoiceModules.Prepare, as AsmController): a copy of the L3s and a settings file.
//   TMA (OSD-TMA): the models of its AIMs, from Models\ (as TmaController), added to the settings.
// Only when the instance reads the L3s of the software (not from the Store) and the App is in its list; otherwise nothing changes.
internal static class Deployments
{
    public static (string Amds, string Settings) Prepare(IReadOnlyCollection<string> apps, string amdDir, string settingsPath)
    {
        var asm = apps.Contains("ASM"); var tma = apps.Contains("TMA");
        if (!asm && !tma) return (amdDir, settingsPath);
        if (!string.Equals(Path.GetFullPath(amdDir).TrimEnd('\\'), Path.GetFullPath(MpaiPaths.Amds).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        { Console.WriteLine("  Deployments:   ASM/TMA are prepared only from the L3s of the software (AIMs\\AMDs): not this time"); return (amdDir, settingsPath); }

        var folder = Path.Combine(Path.GetTempPath(), "mpai-mas-deployment-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var root = MpaiPaths.Root;
        var amds = amdDir; var settings = settingsPath;
        if (asm)
        {
            // as Apps\CAE\ASM\Controller: the grammar alone understands (no language model); the scene is called "stage". Not the hint ASM's Controller gives the recogniser
            // ("room, metres, LUFS..."): the recogniser (1MMC-ASR-V2.5-I01) is shared with the other Apps of the instance, which must not hear with ASM's words in mind.
            var own = new JsonObject
            {
                [Mpai.Cae.AsmVoice.AsmVoiceModules.Jsi] = new JsonObject { ["Scene"] = "stage", ["GrammarOnly"] = "true" }
            };
            var prepared = Mpai.Cae.AsmVoice.AsmVoiceModules.Prepare(root, Path.Combine(folder, "asm"), own, null);
            amds = prepared.Amds; settings = prepared.Settings;
            Console.WriteLine($"  Deployments:   ASM - the L3s with the Access of its JSON Interface from the files of this software: {amds}");
        }
        if (tma)
        {
            string Models(params string[] parts) => Path.Combine([root, "Models", .. parts]);
            var all = JsonNode.Parse(File.ReadAllText(settings))!.AsObject();
            void Set(string aim, params (string Key, string Value)[] values)
            {
                var o = all[aim] as JsonObject ?? new JsonObject(); all[aim] = o;
                foreach (var (k, v) in values) o[k] ??= v;              // what the settings already give is kept
            }
            Set("1CAE-AUS-V1.0-I01", ("EcapaModel", Models("ecapa-tdnn.onnx")));
            Set("1MMC-SIR-V2.5-I02", ("EcapaModel", Models("ecapa-tdnn.onnx")));
            Set("1PAF-FIR-V1.6-I02", ("ScrfdModel", Models("scrfd_10g_bnkps.onnx")), ("ArcFaceModel", Models("glintr100.onnx")));
            Set("1MMC-ASR-V2.5-I02", ("ExecutablePath", Models("Whisper", "bin", "whisper-cli.exe")), ("ModelPath", Models("Whisper", "models", "ggml-small.bin")), ("LanguageCode", "en"));
            Set("1CVE-VCD-V1.0-I01", ("ImagesPerScene", "1"), ("MinSceneLength", "15"), ("SampleSeconds", "1"));
            settings = Path.Combine(folder, "aim-settings.json");
            File.WriteAllText(settings, all.ToJsonString());
            Console.WriteLine($"  Deployments:   TMA - the models of its AIMs from {Models()}");
        }
        return (amds, settings);
    }
}
