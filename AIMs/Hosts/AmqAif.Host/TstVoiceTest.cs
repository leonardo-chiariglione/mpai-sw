using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

namespace AmqAif.Host;

// MMC-TST-V2.5 with a spoken interface.
//
// WHO TALKS TO THE USER. The User Agent does - not MMC-TST. Translating and
// greeting are different functions, and giving TST a second one would mean ports
// that exist only for conversation. But the UA may not call an AIM either, so it
// speaks through a two-AIM AIW of its own, UAG-SPK-V1.0 (Text-To-Speech ->
// Speech Object Delivery), which has an AMD and no code. The UA writes text to a
// boundary port; the Controller speaks it. Zero trust intact.
//
// THE INTERACTION.
//   spoken:  the welcome, then after each translation a short reminder
//   typed:   four characters, the two language codes
//   either:  the sentence - type it and press ENTER, or press ENTER on an empty
//            line to speak it instead
//
// Run with:  dotnet run -- --tstvoice
internal static class TstVoiceTest
{
    private const string PromptAiw = "UAG-SPK-V1.0";
    private const string TstAiw    = "MMC-TST-V2.5";

    // Every prompt is ONE string, spoken and displayed. Two strings that merely
    // agree drift apart the first time either is edited, which is exactly what
    // happened when the voice asked for both codes at once and the console asked
    // for them together on a single line.
    private const string Welcome =
        "Welcome to MPAI Text and Speech Translation. Say q to quit.";

    private const string AskInputLanguage =
        "Two characters for the input language.";

    private const string AskOutputLanguage =
        "Two characters for the output language.";

    private const string AskSentence =
        "Type your sentence, or press Enter to speak it.";

    private const string HeardNothing =
        "I did not hear anything.";

    public static void Run()
    {
        AimLog.ToConsole();

        var repoRoot = FindRepoRoot();
        if (repoRoot is null) { Console.WriteLine("Could not find AIMs\\AMDs."); return; }

        var store = new AmdStore(Path.Combine(repoRoot, "AIMs", "AMDs"));
        store.Scan();

        var settings = AimSettings.Load(Path.Combine(repoRoot, "AIMs", "aim-settings.json"));

        Console.WriteLine();
        Console.WriteLine("MPAI Text and Speech Translation");
        Console.WriteLine("  'q' at any prompt quits.");
        Console.WriteLine();
        Console.WriteLine("  loading models...");

        // BOTH AIWs are started ONCE and kept alive for the whole session.
        //
        // Starting an AIW instantiates every SubAIM, and instantiating loads its
        // model: Piper for the prompt voice, and Whisper plus three translation
        // sessions plus Piper for MMC-TST. Starting an AIW per prompt and per
        // translation - which is what this did - reloaded all of it every time,
        // which is where the long wait after pressing Enter came from.
        var ua       = new UserAgent(store);
        var provider = new AmqAifProvider(store, headless: false);

        ua.MPAI_AIFU_Controller_Initialize();

        if (ua.MPAI_AIFU_AIW_Start(PromptAiw, provider, settings, out var promptAiwId) != AifError.OK)
        {
            Console.WriteLine($"  could not start {PromptAiw}."); return;
        }

        if (ua.MPAI_AIFU_AIW_Start(TstAiw, provider, settings, out var tstAiwId) != AifError.OK)
        {
            Console.WriteLine($"  could not start {TstAiw}.");
            ua.MPAI_AIFU_AIW_Stop(promptAiwId);
            return;
        }

        try
        {
            Say(ua, promptAiwId, Welcome);

            while (true)
            {
                var sourceLanguage = AskLanguage(ua, promptAiwId, AskInputLanguage);
                if (sourceLanguage is null) break;

                var targetLanguage = AskLanguage(ua, promptAiwId, AskOutputLanguage);
                if (targetLanguage is null) break;

                var typed = Ask(ua, promptAiwId, AskSentence);
                if (string.Equals(typed, "q", StringComparison.OrdinalIgnoreCase)) break;

                Translate(ua, tstAiwId, promptAiwId, typed, sourceLanguage, targetLanguage);
            }
        }
        finally
        {
            ua.MPAI_AIFU_AIW_Stop(tstAiwId);
            ua.MPAI_AIFU_AIW_Stop(promptAiwId);
        }

        Console.WriteLine();
        Console.WriteLine("Goodbye.");
    }

    // Speak the prompt, show the same words, read the answer.
    private static string Ask(UserAgent ua, int promptAiwId, string prompt)
    {
        Say(ua, promptAiwId, prompt);
        Console.Write("  > ");
        return (Console.ReadLine() ?? string.Empty).Trim();
    }

    // Two characters, or null to quit. A wrong answer re-asks with the same
    // words, spoken again, rather than inventing a second phrasing.
    private static string? AskLanguage(UserAgent ua, int promptAiwId, string prompt)
    {
        while (true)
        {
            var answer = Ask(ua, promptAiwId, prompt);

            if (string.Equals(answer, "q", StringComparison.OrdinalIgnoreCase)) return null;
            if (answer.Length == 2 && answer.All(char.IsLetter)) return answer.ToLowerInvariant();

            Console.WriteLine("  two letters, for example: en");
        }
    }

    // One TST run on the AIW that is already started. An empty sentence means
    // "record it": MMC-SOA acquires when the Speech Object it receives has no
    // data, and carries that object's Qualifier onto what it captures.
    private static void Translate(
        UserAgent ua,
        int tstAiwId,
        int promptAiwId,
        string typed,
        string sourceLanguage,
        string targetLanguage)
    {
        var speaking = typed.Length == 0;

        var boundary = new Dictionary<string, string>
        {
            ["LanguageSelector"] = MpaiJson.ToJson(
                BasicSelectorObject.Languages(sourceLanguage, targetLanguage))
        };

        if (speaking)
        {
            // The empty trigger carries the SOURCE LANGUAGE on its Qualifier.
            // MMC-ASR reads the language from there - that is why MMC-TST has no
            // Input Language Selector - and a microphone cannot say what language
            // is about to be spoken into it. Without this, ASR falls back to its
            // configured default and returns "(speaking in foreign language)".
            boundary["InputSpeech"] = MpaiJson.ToJson(
                BasicSpeechObject.FromData(
                    Array.Empty<byte>(),
                    new SpeechQualifier
                    {
                        SpeechQualifierID = Guid.NewGuid().ToString(),
                        Attributes = new SpeechAttributes
                        {
                            Metadata = new SpeechMetadata
                            {
                                Language = new Language
                                {
                                    LanguageCode   = sourceLanguage,
                                    LanguageFormat = LanguageFormat.Iso639_1
                                }
                            }
                        }
                    }));
        }
        else
        {
            boundary["InputText"] = MpaiJson.ToJson(BasicTextObject.FromText(typed));
        }

        var completed = speaking
            ? RunWithPressToStop(ua, tstAiwId, boundary)
            : Run(ua, tstAiwId, boundary);

        if (completed is null) return;

        if (completed.Ports.TryGetValue("OutputText", out var textJson))
        {
            var text = MpaiJson.FromJson<BasicTextObject>(textJson);
            var words = text.GetText();

            if (!string.IsNullOrWhiteSpace(words))
            {
                Console.WriteLine($"  {targetLanguage}: {words}");
            }
            else if (speaking)
            {
                Say(ua, promptAiwId, HeardNothing);
            }
            else
            {
                Console.WriteLine("  nothing came back.");
            }
        }
        else if (speaking)
        {
            // Only meaningful when a microphone was involved: saying "I did not
            // hear anything" about a sentence that was TYPED is nonsense.
            Say(ua, promptAiwId, HeardNothing);
        }
        else
        {
            Console.WriteLine("  nothing came back.");
        }
    }

    // The User Agent's own voice: text in, sound out, through the Controller.
    // The prompts are English, so the voice is English.
    private static void Say(UserAgent ua, int promptAiwId, string words)
    {
        const string language = "en";

        Console.WriteLine($"  {words}");

        var text = BasicTextObject.FromText(
            words,
            new TextQualifier
            {
                TextQualifierID = Guid.NewGuid().ToString(),
                Attributes = new TextAttributes
                {
                    Language = new Language
                    {
                        LanguageCode   = language,
                        LanguageFormat = LanguageFormat.Iso639_1
                    }
                }
            });

        Run(ua, promptAiwId, new Dictionary<string, string>
        {
            ["InputText"] = MpaiJson.ToJson(text)
        });
    }

    private static AIF.Controller.Message? Run(
        UserAgent ua,
        int aiwId,
        Dictionary<string, string> boundary)
    {
        var (error, outcome) = ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();
        return Completed(error, outcome);
    }

    // Press-to-stop: run on a background task and, when the user presses Enter,
    // ask the Controller to PAUSE. MMC-SOA is the only AIM running then; it sees
    // the pause request, closes the microphone, and the Resume immediately after
    // lets the rest of the pipeline carry on. Stop would end the AIW and discard
    // the recording it had just taken.
    private static AIF.Controller.Message? RunWithPressToStop(
        UserAgent ua,
        int aiwId,
        Dictionary<string, string> boundary)
    {
        var running = Task.Run(() => ua.RunAsync(aiwId, boundary));

        while (!running.IsCompleted)
        {
            if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Enter)
            {
                ua.MPAI_AIFU_AIW_Pause(aiwId);
                ua.MPAI_AIFU_AIW_Resume(aiwId);
                break;
            }

            Thread.Sleep(25);
        }

        var (error, outcome) = running.GetAwaiter().GetResult();
        return Completed(error, outcome);
    }

    // RunOutcome is nested inside UserAgent, so it needs qualifying here.
    private static AIF.Controller.Message? Completed(
        AifError error,
        UserAgent.RunOutcome? outcome)
    {
        if (error != AifError.OK || outcome is null)
        {
            Console.WriteLine($"  run failed: {error}");
            return null;
        }

        if (outcome.Suspended)
        {
            Console.WriteLine($"  unexpectedly suspended on '{outcome.WaitingPort}'.");
            return null;
        }

        if (outcome.Completed is null) return null;

        if (outcome.Completed.IsError)
        {
            Console.WriteLine($"  {outcome.Completed.FailedAim}: {outcome.Completed.Payload}");
            return null;
        }

        return outcome.Completed;
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
    }
}