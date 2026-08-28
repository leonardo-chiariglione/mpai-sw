using System;
using System.Linq;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Hci.Access.Host;

// ============================================================================
//  Hci.Access.Host - the AIF host for the HCI "check authorised users" app.
//
//  Follows the correct User-Agent pattern (as WorkflowTest / AmqWorkflow do):
//  create a UserAgent over the AMD store, hand it the HCI identity provider and
//  settings via a workflow, and let the workflow drive AIWs through the public
//  MPAI_AIFU_* API. Nothing is invoked directly; the Controller routes.
//
//  This proving version runs MMC-SIR through the Controller and prints the
//  speaker identity, confirming the identity infrastructure executes end to end.
// ============================================================================
internal static class Program
{
    private const string AmdRepository = @"D:\AI\AIMs\AMDs";
    private const string SettingsFile  = @"D:\AI\AIMs\aim-settings.json";
    private const string SpeechFixture = @"D:\AI\TestData\Audio\leonardo.wav";
    private const string ImageFixture  = @"D:\AI\TestData\Images\leonardo.jpg";

    [STAThread]
    private static void Main(string[] args)
    {
        AimLog.ToConsole();

        Console.WriteLine();
        Console.WriteLine("HCI check-authorised-users  [proving host: MMC-SIR through the Controller]");
        Console.WriteLine();

        // AMD store + settings.
        var store = new AmdStore(AmdRepository);
        store.Scan();
        var settings = AimSettings.Load(SettingsFile);

        // User Agent over the store, the HCI identity provider, and the workflow.
        var ua       = new UserAgent(store);
        var provider = new HciIdentityProvider(store);
        var workflow = new HciAccessWorkflow(ua, provider, settings);

        // Mode: "face" runs FIR on the image; anything else runs SIR on the wav.
        var mode = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "voice";
        InstanceIdentifier? identity;
        if (string.Equals(mode, "face", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Identifying face in: {ImageFixture}");
            Console.WriteLine();
            identity = workflow.IdentifyFace(ImageFixture);
        }
        else
        {
            Console.WriteLine($"Identifying speaker in: {SpeechFixture}");
            Console.WriteLine();
            identity = workflow.IdentifySpeaker(SpeechFixture);
        }

        Console.WriteLine();
        if (identity is null)
        {
            Console.WriteLine("No identity returned.");
            return;
        }

        Console.WriteLine("Speaker identity (OSD-IID):");
        foreach (var c in identity.InstanceIdentifierData.Take(3))
            Console.WriteLine($"   {c.InstanceLabel,-12} conf={c.LabelConfidenceLevel:F3} " +
                              $"[{string.Join(",", c.Taxonomy?.TaxonomyLevelIDs ?? new System.Collections.Generic.List<string>())}]");

        Console.WriteLine();
        Console.WriteLine($"=> IDENTIFIED AS: {identity.InstanceIdentifierData.FirstOrDefault()?.InstanceLabel ?? "(unknown)"}");
    }
}
