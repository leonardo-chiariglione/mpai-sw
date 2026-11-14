using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Mpai.Cae.Apps;
using Mpai.Ua;

namespace Mpai.Cae.Aes;

// ADD EMOTION TO SPEECH, the App.
//
//   AesApp --speech neutral.wav --emotion SADNESS [--degree 0.5] --out out.wav
//
// --speech    the emotionless speech
// --emotion   one of the emotions of MMC-EEM (SADNESS, HAPPINESS, ANGER, FEAR, ...); --degree 0 to 1 (default 0.5)
// --out       the speech with emotion
internal static class Program
{
    private const string Module = "1CAE-AES-V2.5-I01";

    private static int Main(string[] args)
    {
        try
        {
            var o = new Options(args, "--speech", "--emotion", "--degree", "--out");
            var speech = o.Need("--speech"); var emotion = o.Need("--emotion"); var output = o.Need("--out");
            var degree = double.Parse(o.One("--degree") ?? "0.5", System.Globalization.CultureInfo.InvariantCulture);

            Console.WriteLine("Starting the Module (a first start takes a while)...");
            using var module = ModuleHost.Start(Module, null, Console.WriteLine);

            // THE SAME WORKFLOW AS IN THE MAS APP: the speech is acquired (here the file given, as a WAV file), the emotion too (here the one given); the Mode Selector and the
            // Language Selector are the workflow's own. The workflow repeats until it is stopped: the App stops it when it has the speech with the emotion.
            string? given = null;
            using var done = new System.Threading.CancellationTokenSource();
            var files = new SpeechFiles(Inputs.SpeechObjectOf("neutral", speech));
            var devices = new DeviceRegistry()
                .RegisterAcquire("OSD-BSO-V1.5", files.AcquireAsync)
                .RegisterAcquire("MMC-EEM-V2.5", (_, _) => Task.FromResult<string?>(new JsonObject { ["Header"] = "MMC-EEM-V2.5", ["EmotionCategory"] = new JsonObject { [emotion.ToUpperInvariant()] = new JsonObject() }, ["Degree"] = degree }.ToJsonString()))
                .RegisterPresent("file", d =>
                {
                    if (d.TryGetValue("Prompt", out var said)) Console.WriteLine(said);
                    if (d.TryGetValue("OSD-BSO-V1.5", out var s)) { given = s; done.Cancel(); }
                    return Task.CompletedTask;
                });
            Console.WriteLine("Adding the emotion...");
            try { new WorkflowRun(module.Api, Path.Combine(ModuleHost.Root, "Apps", "CAE", "AES", "AES.orch"), devices, Environment.GetEnvironmentVariable("MPAI_DIAG") == "1" ? Console.WriteLine : null).Run(done.Token); }
            catch (OperationCanceledException) when (given is not null) { }

            if (given is null) throw new InvalidOperationException("The Module gave no speech.");
            foreach (var file in Inputs.WriteSpeech(given, output)) Console.WriteLine("Speech with emotion: " + file);
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
