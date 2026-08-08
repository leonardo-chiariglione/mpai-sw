using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Mpai.Cae.Aoe;
using Mpai.Cae.Ase;
using Mpai.Cae.Asd;
using Mpai.Aims.Audio;
using Mpai.Core;
using Mpai.Repository;

namespace MPAIApps.ASMApp;

// First working version of the ASMApp window: everything in one screen
// rather than the fuller multi-workspace design discussed, so there is a
// complete, testable loop (create object -> compose scene -> deliver)
// before splitting it into separate Object/Scene workspaces later.
//
// Every button here does exactly what the equivalent test program already
// proved works (aoe_test/ase_test/ASD_Test) - this form is a thin front end
// over AoeAim/AseAim/AsdAim, not new logic of its own.
public sealed class AsmForm : Form
{
    // Same pattern as StoreForm's StoreFolder: check this matches your
    // actual drive letter. Without this, the Repository is in-memory only
    // and everything is lost when the window closes.
    private const string AssetsRootPath = @"D:\AI\Assets";

    // Placeholder until the spatial placement GUI exists: a fixed listener
    // at the origin, facing forward. Every delivery call requires a
    // listener PointOfView; this is what stands in for "the user has placed
    // it graphically" until that GUI is built.
    private static readonly PointOfView DefaultListener = new()
    {
        PointOfViewID = "default-listener",
        CartPosition = new double[] { 0, 0, 0 },
        Orientation = new double[] { 0, 0, 0 }
    };

    private readonly AssetRepository repository = new(AssetsRootPath);
    private readonly AoeAim aoe;
    private readonly AseAim ase;

    private readonly ListBox objectsList = new() { Dock = DockStyle.Fill };
    private readonly ListBox scenesList = new() { Dock = DockStyle.Fill };
    private readonly TextBox logBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new System.Drawing.Font("Consolas", 9)
    };

    public AsmForm()
    {
        aoe = new AoeAim(repository);
        ase = new AseAim(repository, aoe);

        Text = "ASMApp - CAE Audio Scene Manager";
        Width = 960;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;

        var createObjectButton = new Button { Text = "Create Object from File...", Dock = DockStyle.Top, Height = 32 };
        createObjectButton.Click += (_, _) => CreateObjectFromFile();

        var createSceneButton = new Button { Text = "Create Scene from Selected Object", Dock = DockStyle.Top, Height = 32 };
        createSceneButton.Click += (_, _) => CreateSceneFromSelectedObject();

        var addToSceneButton = new Button { Text = "Add Selected Object to Selected Scene", Dock = DockStyle.Top, Height = 32 };
        addToSceneButton.Click += (_, _) => AddSelectedObjectToSelectedScene();

        var deliverButton = new Button { Text = "Deliver Selected Scene...", Dock = DockStyle.Top, Height = 32 };
        deliverButton.Click += (_, _) => DeliverSelectedScene();

        var playObjectButton = new Button { Text = "Play Selected Object", Dock = DockStyle.Top, Height = 32 };
        playObjectButton.Click += (_, _) => PlaySelectedObject();

        var playSceneButton = new Button { Text = "Play Selected Scene", Dock = DockStyle.Top, Height = 32 };
        playSceneButton.Click += (_, _) => PlaySelectedScene();

        var refreshButton = new Button { Text = "Refresh", Dock = DockStyle.Top, Height = 28 };
        refreshButton.Click += (_, _) => RefreshLists();

        var buttonPanel = new Panel { Dock = DockStyle.Top, Height = 32 * 6 + 28 };
        buttonPanel.Controls.Add(refreshButton);
        buttonPanel.Controls.Add(playSceneButton);
        buttonPanel.Controls.Add(playObjectButton);
        buttonPanel.Controls.Add(deliverButton);
        buttonPanel.Controls.Add(addToSceneButton);
        buttonPanel.Controls.Add(createSceneButton);
        buttonPanel.Controls.Add(createObjectButton);

        var objectsPanel = new Panel { Dock = DockStyle.Fill };
        objectsPanel.Controls.Add(objectsList);
        objectsPanel.Controls.Add(new Label { Text = "Objects (AudioObject):", Dock = DockStyle.Top, Height = 20 });

        var scenesPanel = new Panel { Dock = DockStyle.Fill };
        scenesPanel.Controls.Add(scenesList);
        scenesPanel.Controls.Add(new Label { Text = "Scenes (AudioSceneDescriptors):", Dock = DockStyle.Top, Height = 20 });

        var listsSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        listsSplit.Panel1.Controls.Add(objectsPanel);
        listsSplit.Panel2.Controls.Add(scenesPanel);

        var topSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 320 };
        topSplit.Panel1.Controls.Add(listsSplit);
        topSplit.Panel2.Controls.Add(logBox);
        topSplit.Panel2.Controls.Add(new Label { Text = "Log:", Dock = DockStyle.Top, Height = 20 });

        Controls.Add(topSplit);
        Controls.Add(buttonPanel);

        Log($"Repository root: {AssetsRootPath}");
        RefreshLists();
    }

    private void RefreshLists()
    {
        objectsList.Items.Clear();
        foreach (var asset in repository.FindAssets(AssetType.AUO))
        {
            objectsList.Items.Add(asset.AssetId);
        }

        scenesList.Items.Clear();
        foreach (var asset in repository.FindAssets(AssetType.ASD))
        {
            scenesList.Items.Add(asset.AssetId);
        }
    }

    private void CreateObjectFromFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Create an Audio Object from a file",
            Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            var bao = BasicAudioObject.FromData(bytes);
            var audioObjectAsset = aoe.CreateObject(bao);

            Log($"Created {audioObjectAsset.AssetId} from {Path.GetFileName(dialog.FileName)} ({bytes.Length} bytes)");
            RefreshLists();
        }
        catch (Exception failure)
        {
            Log($"ERROR creating object: {failure.Message}");
        }
    }

    private void CreateSceneFromSelectedObject()
    {
        if (objectsList.SelectedItem is not string objectId)
        {
            MessageBox.Show(this, "Select an Object first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var sceneAsset = ase.CreateScene(objectId);
            Log($"Created scene {sceneAsset.AssetId} containing {objectId}");
            RefreshLists();
        }
        catch (Exception failure)
        {
            Log($"ERROR creating scene: {failure.Message}");
        }
    }

    private void AddSelectedObjectToSelectedScene()
    {
        if (objectsList.SelectedItem is not string objectId || scenesList.SelectedItem is not string sceneId)
        {
            MessageBox.Show(this, "Select both an Object and a Scene first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var updated = ase.AddObjectToScene(sceneId, objectId);
            Log($"Added {objectId} to scene -> new version {updated.AssetId} (was {sceneId})");
            RefreshLists();

            // The old scene ID no longer reflects the current state; make
            // sure the user is looking at the version that just got created.
            scenesList.SelectedItem = updated.AssetId;
        }
        catch (Exception failure)
        {
            Log($"ERROR adding object to scene: {failure.Message}");
        }
    }

    private async void DeliverSelectedScene()
    {
        if (scenesList.SelectedItem is not string sceneId)
        {
            MessageBox.Show(this, "Select a Scene first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new FolderBrowserDialog { Description = "Choose a destination folder for delivery" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var materialized = ase.Materialize(sceneId);
            var delivery = new FileAudioDelivery(dialog.SelectedPath);
            var asd = new AsdAim(delivery);

            await asd.DeliverSceneAsync(materialized, DefaultListener);

            Log($"Delivered {sceneId} ({materialized.AudioObjectCount} object(s)) -> {dialog.SelectedPath}");
        }
        catch (Exception failure)
        {
            Log($"ERROR delivering scene: {failure.Message}");
        }
    }

    private async void PlaySelectedObject()
    {
        if (objectsList.SelectedItem is not string objectId)
        {
            MessageBox.Show(this, "Select an Object first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var materialized = aoe.Materialize(objectId);
            var asd = new AsdAim(new WinmmAudioDelivery());

            Log($"Playing {objectId} ...");
            await asd.DeliverObjectAsync(materialized, DefaultListener);
            Log($"Finished playing {objectId}");
        }
        catch (Exception failure)
        {
            Log($"ERROR playing object: {failure.Message}");
        }
    }

    private async void PlaySelectedScene()
    {
        if (scenesList.SelectedItem is not string sceneId)
        {
            MessageBox.Show(this, "Select a Scene first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var materialized = ase.Materialize(sceneId);
            var asd = new AsdAim(new WinmmAudioDelivery());

            Log($"Playing scene {sceneId} ({materialized.AudioObjectCount} object(s)) ...");
            await asd.DeliverSceneAsync(materialized, DefaultListener);
            Log($"Finished playing scene {sceneId}");
        }
        catch (Exception failure)
        {
            Log($"ERROR playing scene: {failure.Message}");
        }
    }

    private void Log(string line)
    {
        logBox.AppendText(line + Environment.NewLine);
    }
}