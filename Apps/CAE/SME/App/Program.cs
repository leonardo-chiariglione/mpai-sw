using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Mpai.Cae.Apps;
using Mpai.Ua;

namespace Mpai.Cae.Sme;

// SPEAK MODEL WITH EMOTION, the App.
//
//   SmeApp --speech neutral.wav --model model.wav --out out.wav
//
// --speech    the emotionless speech
// --model     a model utterance: the speech takes its prosody
// --out       the speech with emotion
internal static class Program
{
    private const string Module = "1CAE-SME-V2.5-I01";

    private static int Main(string[] args)
    {
        var (controller, rest) = ModuleConnection.Take(args); args = rest;          // --controller <url>: the Module on that Controller (Thalia's, or an MPAI as a Service instance)
        try
        {
            var o = new Options(args, "--speech", "--model", "--out");
            var speech = o.Need("--speech"); var model = o.Need("--model"); var output = o.Need("--out");

            Console.WriteLine("Starting the Module (a first start takes a while)...");
            using var module = ModuleConnection.Open(Module, controller, null, Console.WriteLine);

            // THE SAME WORKFLOW AS IN THE MAS APP: the speech and then the model are acquired (here the files given, as WAV files, in that order); the Mode Selector is the
            // workflow's own. The workflow repeats until it is stopped: the App stops it when it has the speech with the emotion.
            string? given = null;
            using var done = new System.Threading.CancellationTokenSource();
            var files = new SpeechFiles(Inputs.SpeechObjectOf("neutral", speech), Inputs.SpeechObjectOf("model", model));
            var devices = new DeviceRegistry()
                .RegisterAcquire("OSD-BSO-V1.5", files.AcquireAsync)
                .RegisterPresent("file", d =>
                {
                    if (d.TryGetValue("Prompt", out var said)) Console.WriteLine(said);
                    if (d.TryGetValue("OSD-BSO-V1.5", out var s)) { given = s; done.Cancel(); }
                    return Task.CompletedTask;
                });
            Console.WriteLine("Adding the emotion...");
            try { new WorkflowRun(module.Api, Path.Combine(ModuleHost.Root, "Apps", "CAE", "SME", "SME.orch"), devices, Environment.GetEnvironmentVariable("MPAI_DIAG") == "1" ? Console.WriteLine : null).Run(done.Token); }
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
