using System;
using System.Windows.Forms;

namespace MPAIApps.StoreApp;

// ============================================================================
//  StoreApp - a standing window where an implementer submits an AIM Metadata
//  instance. On submission, it is validated against the AMD rules and, if
//  valid, published into the MPAI Store (the AMDs folder the Controller reads
//  from). Nothing here changes MpaiStore itself; this is only the front door
//  it was missing.
// ============================================================================
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new StoreForm());
    }
}
