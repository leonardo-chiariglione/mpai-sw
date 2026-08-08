using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

namespace MPAIApps.AMQApp;

internal static class Program
{
    private const string AmdRepository = @"D:\AI\AIMs\AMDs";
    private const string SettingsFile  = @"D:\AI\AIMs\aim-settings.json";
    private const string DefaultAim    = "MMC-AMQ-V2.5";

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var store    = new AmdStore(AmdRepository);
        store.Scan();
        var settings = AimSettings.Load(SettingsFile);

        var selected = store.GetCatalog()
                            .FirstOrDefault(item => item.AIMName == DefaultAim);
        if (selected is null)
        {
            MessageBox.Show($"AMD not found for {DefaultAim}.", "AMQApp",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var identifier = new Identifier
        {
            AIMName          = selected.AIMName,
            ImplementerID    = selected.ImplementerID,
            ImplementationID = selected.ImplementationID
        };

        var controller = new Controller(store);
        var graph      = controller.RegisterAim(identifier);
        var host       = new AimHost();
        var executor   = new MachineExecutor(host);
        var session    = new AifAmqSession(host, executor, graph);
        var window     = new ObserverWindow(session);

        window.SetStatus("Loading AI models\u2026 please wait.", loading: true);

        Task.Run(() =>
        {
            try
            {
                var provider = new AmqAppProvider(store, window.ImageSurface);
                controller.Instantiate(graph, provider, settings, host);
                window.Invoke(() => window.SetStatus(
                    "Ready \u2014 select an image to begin.", loading: false));
            }
            catch (Exception ex)
            {
                window.Invoke(() =>
                {
                    window.SetStatus("Failed: " + ex.Message, loading: false);
                    MessageBox.Show(ex.ToString(), "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
        });

        Application.Run(window);
    }
}
