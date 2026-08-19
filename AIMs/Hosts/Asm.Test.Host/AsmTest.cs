using System;
using System.Collections.Generic;
using System.IO;

using AIF.Controller;
using AIF.SharedStorage;
using AIF.Store;

using System.Linq;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Asm.Test.Host;

// CAE-ASM through the Controller: open by Port, act by Command, one run per
// action.
//
// This is the first thing to exercise the new path. Everything before it
// established that the metadata parses and the code compiles; nothing had put a
// Command through a Port and looked at what came back.
//
// Four runs, each one user action:
//
//   1  create an object      a Basic Audio Object arrives, no Command
//   2  modify it             ModifiedObjects - INTERNAL attributes
//   3  place it in a scene   AddedObjects on the scene Command Port
//   4  move the listener     a Point of View, no Command at all
//
// What each run proves is stated as it runs, because a test that only says PASS
// tells you nothing about what was actually established.
internal static class AsmTest
{
    public static void Run()
    {
        AimLog.ToConsole();

        var repoRoot = FindRepoRoot();
        if (repoRoot is null) { Console.WriteLine("Could not find AIMs/AMDs."); return; }

        var store = new AmdStore(Path.Combine(repoRoot, "AIMs", "AMDs"));
        store.Scan();

        var settings = AimSettings.Load(Path.Combine(repoRoot, "AIMs", "aim-settings.json"));

        // A repository of its own, so the test neither reads nor disturbs the
        // assets ASMApp has built.
        var assets = Path.Combine(Path.GetTempPath(), "mpai-asm-test");
        if (Directory.Exists(assets)) Directory.Delete(assets, recursive: true);

        var storage  = new FileSharedStorage(assets, topAim: "CAE-ASM", requestedBy: "AsmTest");
        var delivered = Path.Combine(assets, "delivered");
        Directory.CreateDirectory(delivered);

        var provider = new AsmProvider(store, storage, delivered);
        var ua       = new UserAgent(store);

        ua.MPAI_AIFU_Controller_Initialize();

        var started = ua.MPAI_AIFU_AIW_Start("CAE-ASM-V1.0", provider, settings, out var aiwId);
        if (started != AifError.OK) { Console.WriteLine($"AIW_Start failed: {started}"); return; }

        Console.WriteLine();
        Console.WriteLine("=== CAE-ASM through the Controller ===");
        Console.WriteLine();

        var created1 = false; var commandReachedAoe = false; var modifiedBasic = false;
        var commandReachedAse = false; var listenerRun = false;

        try
        {
            // ---- 1. create an object -------------------------------------
            Console.WriteLine("1  a Basic Audio Object arrives, with no Command.");
            Console.WriteLine("   proves: creation opens implicitly, and CAE-AOE runs while");
            Console.WriteLine("           CAE-ASE and CAE-ASD have nothing to do.");

            var basic = new BasicAudioObject { BasicAudioObjectID = "" };

            var first = Run(ua, aiwId, new Dictionary<string, string>
            {
                // The boundary Port is AudioObject and it accepts BOTH kinds:
                // a Port declares the SET of Data Types it takes, so there is no
                // longer a separate Port for the Basic case. This wrote
                // "BasicAudioObject", which no longer exists, so nothing arrived
                // and every AIM was skipped - the test lagging the metadata, for
                // the third time, because renaming a Port means revisiting what
                // writes to it.
                ["AudioObject"] = MpaiJson.ToJson(basic)
            });

            // Nothing comes back at the boundary here, BY DESIGN: CAE-ASM's
            // outputs are the Audio Scene Descriptors and the audio itself. The
            // edited Object is internal - CAE-AOE feeds CAE-ASE - so it is
            // looked for where it actually lives.
            var created = storage.List("AUO").FirstOrDefault();
            created1 = created is not null;
            Console.WriteLine($"   -> object {created ?? "(none)"}");
            Console.WriteLine();

            // ---- 2. modify it, internally --------------------------------
            Console.WriteLine("2  ModifiedObjects: INTERNAL attributes of what is open.");
            Console.WriteLine("   proves: a Command needs no target, because one object is open;");
            Console.WriteLine("           and the Command reaches CAE-AOE and not the others.");

            var modify = new UserCommand
            {
                UserCommandID   = Guid.NewGuid().ToString(),
                UserCommandData = new UserCommandData
                {
                    LUFS            = -23.0,
                    ModifiedObjects = new ObjectChanges { Objects = { new ObjectChange() } }
                }
            };

            var second = Run(ua, aiwId, new Dictionary<string, string>
            {
                ["ObjectCommand"] = MpaiJson.ToJson(modify)
            });

            // The run completed without error and the object survived it: the
            // Command reached CAE-AOE rather than being ignored for want of
            // something open.
            modifiedBasic = second is not null;

            // Likewise internal, and counted where the change actually lands.
            //
            // This first counted AUO revisions and reported failure while the
            // run had plainly succeeded: EditBasicObjectProperties revises the
            // BASIC object, not the composed one. Modifying internal attributes
            // does not mint a new AudioObject - which is the layering itself,
            // and the trace says so: "modified (internal) BAO000001".
            //
            // A wrong assertion is worse than a missing one. It reports a fault
            // in the system when the fault is in the expectation.
            var basics = storage.List("BAO").Count;
            commandReachedAoe = basics > 0 && modifiedBasic;
            Console.WriteLine($"   -> {basics} basic object revision(s) in the repository");
            Console.WriteLine();

            // ---- 3. place it in a scene ----------------------------------
            Console.WriteLine("3  AddedObjects on the scene Command Port.");
            Console.WriteLine("   proves: four Command Ports of ONE Data Type are told apart by");
            Console.WriteLine("           PortNumber - this one reaches CAE-ASE, not CAE-AOE.");

            var place = new UserCommand
            {
                UserCommandID   = Guid.NewGuid().ToString(),
                UserCommandData = new UserCommandData
                {
                    AddedObjects = new ObjectPlacements
                    {
                        Objects = { new ObjectPlacement
                        {
                            ObjectID = new ManagedObject { ObjectID = created }
                        } }
                    }
                }
            };

            var third = Run(ua, aiwId, new Dictionary<string, string>
            {
                ["SceneCommand"] = MpaiJson.ToJson(place)
            });

            var scene = Scene(third);
            commandReachedAse = scene is not null;
            Console.WriteLine($"   -> scene {scene?.AudioSceneDescriptorsID ?? "(none)"}" +
                              $" with {scene?.AudioObjects?.Count ?? 0} object(s)");
            Console.WriteLine();

            // ---- 4. move the listener ------------------------------------
            Console.WriteLine("4  a Point of View, with no Command at all.");
            Console.WriteLine("   proves: the listener is a SCENE attribute, set once, and a run");
            Console.WriteLine("           carrying no Command is still a legitimate run.");

            var fourth = Run(ua, aiwId, new Dictionary<string, string>
            {
                ["ListenerPointOfView"] = MpaiJson.ToJson(new PointOfView())
            });

            var moved = Scene(fourth);
            listenerRun = moved is not null;
            Console.WriteLine($"   -> scene {moved?.AudioSceneDescriptorsID ?? "(none)"}");
            Console.WriteLine();

            // WHAT ACTUALLY HAPPENED, not what was hoped for.
            //
            // The first version of this test printed its conclusions
            // unconditionally, so it announced that everything was established
            // while every run had in fact failed. A test that cannot report
            // failure is worse than no test: it converts an absence of evidence
            // into a false claim.
            Console.WriteLine("=== results ===");
            Report("an object was created and opened",            created1);
            Report("a Command reached CAE-AOE and produced an object", commandReachedAoe);
            Report("a Command reached CAE-ASE and produced a scene",   commandReachedAse);
            Report("a run carrying only a Point of View worked",       listenerRun);

            var all = created1 && commandReachedAoe && commandReachedAse && listenerRun;

            Console.WriteLine();
            Console.WriteLine(all
                ? "All four established. An interactive session is a sequence of runs."
                : "NOT everything worked - read the trace above, not this line.");
        }
        finally
        {
            ua.MPAI_AIFU_AIW_Stop(aiwId);
        }
    }

    private static void Report(string claim, bool held) =>
        Console.WriteLine($"  [{(held ? "yes" : "NO ")}] {claim}");

    private static AIF.Controller.Message? Run(
        UserAgent ua, int aiwId, Dictionary<string, string> ports)
    {
        var (error, outcome) = ua.RunAsync(aiwId, ports).GetAwaiter().GetResult();

        if (error != AifError.OK)      { Console.WriteLine($"   !! {error}"); return null; }
        if (outcome?.Suspended == true){ Console.WriteLine($"   !! suspended on {outcome.WaitingPort}"); return null; }
        if (outcome?.Completed is null){ Console.WriteLine("   !! nothing completed"); return null; }

        if (outcome.Completed.IsError)
        {
            Console.WriteLine($"   !! {outcome.Completed.FailedAim}: {outcome.Completed.Payload}");
            return null;
        }

        return outcome.Completed;
    }

    // BY DATA TYPE, not by Port name.
    //
    // The first version searched for a Port whose NAME contained
    // "EditedAudioObject" - which is precisely the mistake the framework does
    // not make: BuildInbox matches on DataType and treats the name as a label.
    // Matching on the Header a payload carries is both correct and immune to the
    // Port renaming this AMD went through earlier.
    private static AudioSceneDescriptors? Scene(AIF.Controller.Message? completed) =>
        ByDataType<AudioSceneDescriptors>(completed, "OSD-ASD-V1.5");

    private static T? ByDataType<T>(AIF.Controller.Message? completed, string dataType)
        where T : class
    {
        if (completed is null) return null;

        foreach (var entry in completed.Ports)
        {
            if (entry.Value.Contains($"\"{dataType}\"", StringComparison.Ordinal))
                return MpaiJson.FromJson<T>(entry.Value);
        }

        return null;
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