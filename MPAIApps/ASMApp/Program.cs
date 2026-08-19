using System;
using System.Windows.Forms;

using AIF.SharedStorage;
using Mpai.Cae.Aoe;
using Mpai.Cae.Ase;

namespace MPAIApps.ASMApp;

// ASMApp - the user-facing front end for CAE-ASM. Hosts Shared Storage and
// AOE/ASE directly in-process (no separate ASM process, no IPC) and calls
// straight into their live instances, exactly like AmqAif.Host calls
// MachineExecutor - just imperative calls instead of a planned pipeline run,
// since a user composing objects/scenes works on demand, not start-to-finish.
//
// AoeAim/AseAim now call the proposed MPAI-AIF Shared Storage API
// (Put/Get/Delete/List/Exists/GetKeyInfo) directly - no Repository class or
// method vocabulary. "CAE-ASM" and "ASMApp-Desktop-UA" below are the
// closest honest approximation of the Top AIM / requesting User Agent a
// real AIF Controller would supply automatically; this reference software
// runs its AIMs by direct in-process calls rather than through a
// Controller (documented explicitly in CAE-ASM-V1.0's own AMD), so there
// is nothing upstream to supply these values the way a genuine deployment
// would.
//
// Two separate top-level windows - Objects and Scenes - "just another
// window that is shown," not tabs in one shared shell. Both are given the
// SAME ISharedStorage/AoeAim/AseAim instances, so they always see
// consistent state; each has its own Refresh button rather than the
// windows automatically notifying each other.
internal static class Program
{
    // Same pattern as before: check this matches your actual drive letter.
    // Without this, storage is in-memory only and everything is lost when
    // the app closes.
    private const string AssetsRootPath = @"D:\AI\Assets";

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var storage = new FileSharedStorage(AssetsRootPath, topAim: "CAE-ASM", requestedBy: "ASMApp-Desktop-UA");
        var aoe = new AoeAim(storage);

        // AseAim no longer holds an AoeAim. Until ASMApp is itself driven
        // through the Controller, it passes the expansion explicitly - the same
        // behaviour as before, but as an argument at the call site rather than a
        // dependency inside the AIM.
        var ase = new AseAim(storage);

        var objectsForm = new ObjectsForm(storage, aoe);
        var scenesForm = new ScenesForm(storage, aoe, ase);
        objectsForm.SetSibling(scenesForm);
        scenesForm.SetSibling(objectsForm);

        scenesForm.Show();
        objectsForm.Show();
        objectsForm.BringToFront();
        objectsForm.Activate();

        // Runs until every top-level form is closed, not just one - since
        // both windows were shown independently via .Show() rather than
        // one being passed to Application.Run(mainForm).
        Application.Run();
    }
}