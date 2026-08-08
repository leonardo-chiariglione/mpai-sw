using System;
using System.Windows.Forms;

using Mpai.Cae.Aoe;
using Mpai.Cae.Ase;
using Mpai.Repository;

namespace MPAIApps.ASMApp;

// ASMApp - the user-facing front end for CAE-ASM. Hosts the Repository and
// AOE/ASE directly in-process (no separate ASM process, no IPC) and calls
// straight into their live instances, exactly like AmqAif.Host calls
// MachineExecutor - just imperative calls instead of a planned pipeline run,
// since a user composing objects/scenes works on demand, not start-to-finish.
//
// Two separate top-level windows - Objects and Scenes - "just another
// window that is shown," not tabs in one shared shell. Both are given the
// SAME AssetRepository/AoeAim/AseAim instances, so they always see
// consistent state; each has its own Refresh button rather than the
// windows automatically notifying each other.
internal static class Program
{
    // Same pattern as StoreForm's StoreFolder: check this matches your
    // actual drive letter. Without this, the Repository is in-memory only
    // and everything is lost when the app closes.
    private const string AssetsRootPath = @"D:\AI\Assets";

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var repository = new AssetRepository(AssetsRootPath);
        var aoe = new AoeAim(repository);
        var ase = new AseAim(repository, aoe);

        var objectsForm = new ObjectsForm(repository, aoe);
        var scenesForm = new ScenesForm(repository, aoe, ase);
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