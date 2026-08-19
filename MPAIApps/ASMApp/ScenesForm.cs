using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Mpai.Cae.Aoe;
using Mpai.Cae.Ase;
using Mpai.Cae.Asd;
using Mpai.Aims.Audio;
using Mpai.Core;
using AIF.SharedStorage;

namespace MPAIApps.ASMApp;

// The Scenes window - Modes 2/3. Composes scenes via the draft/Save pattern.
//
// Grid rows follow the same "Label | Button | Button" pattern as Objects:
//   Scene      | Load  | Clear   - load an existing scene's content onto the
//                                  canvas to edit it, or clear back to empty
//   Edit       | Add   | Move    - Add stages the selected Object at the
//                                  current Position fields; Move re-applies
//                                  the Position fields to whichever canvas
//                                  item was placed there last (an
//                                  interpretation of the spec - a field-
//                                  driven alternative to dragging, for
//                                  precision positioning)
//   Repository | Save  | Clear   - commit the draft, or discard it
//   Deliver    | File  | Device  - always panned now (the un-panned "Play
//                                  Selected Scene" is retired, per the
//                                  agreed simplification)
//
// The listener is now draggable on the canvas exactly like an object dot -
// same PlacementCanvasMath hit-test mechanism, just a permanent, special
// entry rather than one tied to an AudioObjectID. Its position is
// genuinely persisted via AseAim.SetSceneListener (confirmed by the
// corrected schema - ListenerPointOfView is a real field on
// AudioSceneDescriptors), not just a delivery-time default.
//
// DEFERRED (budget): the rotation handle for setting orientation on the
// canvas. Right-click still opens the Z/time dialog only; orientation
// editing isn't wired into the canvas yet.
public sealed class ScenesForm : Form
{
    private readonly ISharedStorage storage;
    private readonly AoeAim aoe;
    private readonly AseAim ase;
    private ObjectsForm? sibling;

    private readonly ListBox objectsList = new() { Dock = DockStyle.Fill };
    private readonly ListBox scenesList = new() { Dock = DockStyle.Fill };
    private readonly PlacementCanvas canvas = new() { Dock = DockStyle.Fill };
    private readonly NumericUpDown positionX = new() { Minimum = -50, Maximum = 50, DecimalPlaces = 1, Increment = 0.5M, Width = 70 };
    private readonly NumericUpDown positionY = new() { Minimum = -50, Maximum = 50, DecimalPlaces = 1, Increment = 0.5M, Width = 70 };
    private readonly NumericUpDown positionZ = new() { Minimum = -50, Maximum = 50, DecimalPlaces = 1, Increment = 0.5M, Width = 70 };
    private readonly NumericUpDown startTimeSeconds = new() { Minimum = 0, Maximum = 3600, DecimalPlaces = 1, Increment = 0.5M, Width = 70 };
    private readonly NumericUpDown endTimeSeconds = new() { Minimum = 0, Maximum = 3600, DecimalPlaces = 1, Increment = 0.5M, Width = 70, Value = 5 };
    private readonly NumericUpDown minFrequencyHz = new() { Minimum = 0, Maximum = 22000, DecimalPlaces = 0, Increment = 100, Width = 80, Value = 80 };
    private readonly NumericUpDown maxFrequencyHz = new() { Minimum = 0, Maximum = 22000, DecimalPlaces = 0, Increment = 100, Width = 80, Value = 12000 };
    private readonly NumericUpDown loudnessLufs = new() { Minimum = -60, Maximum = 0, DecimalPlaces = 1, Increment = 0.5M, Width = 70, Value = -16 };
    private readonly TextBox logBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new System.Drawing.Font("Consolas", 8)
    };

    private readonly SceneDraft sceneDraft = new();
    private string? draftTargetSceneId;
    private PlacementCanvas.Item? lastPlacedItem;

    private const string ListenerLabel = "Listener";
    private PointOfView currentListener = new()
    {
        PointOfViewID = "default-listener",
        CartPosition = new double[] { 0, 0, 0 },
        Orientation = new double[] { 0, 0, 0 }
    };

    public ScenesForm(ISharedStorage storage, AoeAim aoe, AseAim ase)
    {
        this.storage = storage;
        this.aoe = aoe;
        this.ase = ase;

        Text = "ASMApp - Scenes (ASD editing / AED creation)";
        Width = 1000;
        Height = 835;
        StartPosition = FormStartPosition.Manual;
        // Deliberately overlapping the Objects window (not side-by-side) -
        // normal window dragging already uncovers the other one; this is
        // the "bring the other one to front without dragging" shortcut.
        Location = new System.Drawing.Point(60, 60);

        canvas.EmptySpaceClicked += OnCanvasEmptySpaceClicked;
        canvas.ItemMoved += OnCanvasItemMoved;
        canvas.ItemRightClicked += OnCanvasItemRightClicked;

        var bringObjectsToFrontButton = new Button { Text = "Show Objects Window", Width = 160, Height = 26 };
        bringObjectsToFrontButton.Click += (_, _) => { sibling?.BringToFront(); sibling?.Activate(); };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 5,
            RowCount = 3,
            Height = 32 * 3 + 12,
            Padding = new Padding(4, 4, 4, 4)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        for (var c = 0; c < 4; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        for (var i = 0; i < 3; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        static Label RowLabel(string text) => new() { Text = text, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold) };
        static Button Cell(string text) => new() { Text = text, Dock = DockStyle.Fill, Margin = new Padding(2) };

        // Row 0: Scene/Edit - Load Scene | Add to Draft | Discard Draft | Save Scene
        var sceneLoadButton = Cell("Load Scene");
        sceneLoadButton.Click += (_, _) => LoadSelectedScene();
        var editAddButton = Cell("Add to Draft");
        editAddButton.Click += (_, _) => AddSelectedObjectToDraft();
        var repoClearButton = Cell("Discard Draft");
        repoClearButton.Click += (_, _) => ClearDraft();
        var repoSaveButton = Cell("Save Scene");
        repoSaveButton.Click += (_, _) => SaveDraftAsScene();
        grid.Controls.Add(RowLabel("Scene/Edit"), 0, 0);
        grid.Controls.Add(sceneLoadButton, 1, 0);
        grid.Controls.Add(editAddButton, 2, 0);
        grid.Controls.Add(repoClearButton, 3, 0);
        grid.Controls.Add(repoSaveButton, 4, 0);

        // Row 1: Canvas - Clear Canvas | Move Selected
        var sceneClearButton = Cell("Clear Canvas");
        sceneClearButton.Click += (_, _) => ClearDraft();
        var editMoveButton = Cell("Move Selected");
        editMoveButton.Click += (_, _) => MoveLastPlacedItem();
        grid.Controls.Add(RowLabel("Canvas"), 0, 1);
        grid.Controls.Add(sceneClearButton, 1, 1);
        grid.Controls.Add(editMoveButton, 2, 1);

        // Row 2: Deliver - To File | To Speaker | Delete | Info
        var deliverFileButton = Cell("To File");
        deliverFileButton.Click += (_, _) => DeliverSelectedScenePannedToFile();
        var deliverDeviceButton = Cell("To Speaker");
        deliverDeviceButton.Click += (_, _) => PlaySelectedScenePanned();
        var sceneDeleteButton = Cell("Delete");
        sceneDeleteButton.Click += (_, _) => DeleteSelectedScene();
        var sceneInfoButton = Cell("Info");
        sceneInfoButton.Click += (_, _) => ShowSelectedSceneInfo();
        grid.Controls.Add(RowLabel("Deliver"), 0, 2);
        grid.Controls.Add(deliverFileButton, 1, 2);
        grid.Controls.Add(deliverDeviceButton, 2, 2);
        grid.Controls.Add(sceneDeleteButton, 3, 2);
        grid.Controls.Add(sceneInfoButton, 4, 2);

        var positionPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, FlowDirection = FlowDirection.LeftToRight };
        positionPanel.Controls.Add(new Label { Text = "Position (X,Y,Z):", AutoSize = true, Padding = new Padding(4, 6, 2, 0) });
        positionPanel.Controls.Add(positionX);
        positionPanel.Controls.Add(positionY);
        positionPanel.Controls.Add(positionZ);
        positionPanel.Controls.Add(new Label { Text = "  t (s):", AutoSize = true, Padding = new Padding(8, 6, 2, 0) });
        positionPanel.Controls.Add(startTimeSeconds);
        positionPanel.Controls.Add(endTimeSeconds);

        var acousticPanel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, FlowDirection = FlowDirection.LeftToRight };
        acousticPanel.Controls.Add(new Label { Text = "AcousticProfile Hz/LUFS:", AutoSize = true, Padding = new Padding(4, 6, 2, 0) });
        acousticPanel.Controls.Add(minFrequencyHz);
        acousticPanel.Controls.Add(maxFrequencyHz);
        acousticPanel.Controls.Add(loudnessLufs);

        var topPanel = new Panel { Dock = DockStyle.Top, Height = grid.Height + 28 + 28 };
        topPanel.Controls.Add(acousticPanel);
        topPanel.Controls.Add(positionPanel);
        topPanel.Controls.Add(grid);

        // Canvas gets the dominant share of the window - the whole point of
        // this restructuring.
        var canvasPanel = new Panel { Dock = DockStyle.Fill };
        canvasPanel.Controls.Add(canvas);
        canvasPanel.Controls.Add(new Label
        {
            Text = "Click empty space to aim; drag a dot (or the Listener) to move it; right-click a dot to edit Z/time.",
            Dock = DockStyle.Top,
            Height = 16,
            Font = new System.Drawing.Font("Segoe UI", 7.5f),
            ForeColor = System.Drawing.Color.DimGray
        });

        // Objects/Scenes names relocated into two columns below the canvas,
        // reclaiming what used to be near-empty space; Log kept, just smaller.
        var objectsColumn = new Panel { Dock = DockStyle.Fill };
        objectsColumn.Controls.Add(objectsList);
        objectsColumn.Controls.Add(new Label { Text = "Objects:", Dock = DockStyle.Top, Height = 16, Font = new System.Drawing.Font("Segoe UI", 7.5f) });

        var scenesColumn = new Panel { Dock = DockStyle.Fill };
        scenesColumn.Controls.Add(scenesList);
        scenesColumn.Controls.Add(new Label { Text = "Scenes:", Dock = DockStyle.Top, Height = 16, Font = new System.Drawing.Font("Segoe UI", 7.5f) });

        var namesSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = System.Windows.Forms.Orientation.Vertical };
        namesSplit.Panel1.Controls.Add(objectsColumn);
        namesSplit.Panel2.Controls.Add(scenesColumn);

        var refreshButton = new Button { Text = "Refresh", Dock = DockStyle.Top, Height = 22 };
        refreshButton.Click += (_, _) => RefreshLists();

        var namesPanel = new Panel { Dock = DockStyle.Fill };
        namesPanel.Controls.Add(namesSplit);
        namesPanel.Controls.Add(refreshButton);

        // Log removed from the UI (kept alive but hidden so Log(...) still works).
        logBox.Visible = false;

        namesPanel.Dock = DockStyle.Bottom;
        namesPanel.Height = 200;

        Controls.Add(canvasPanel);   // canvas fills the whole central area now
        Controls.Add(namesPanel);
        Controls.Add(logBox);        // hidden; present only so AppendText has a target

        var switchPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        switchPanel.Controls.Add(bringObjectsToFrontButton);
        Controls.Add(switchPanel);
        Controls.Add(topPanel);


        RefreshLists();
        RedrawListenerOnCanvas();
    }

    public void SetSibling(ObjectsForm objectsForm) => sibling = objectsForm;

    public void RefreshLists()
    {
        objectsList.Items.Clear();
        foreach (var assetId in storage.List("AUO"))
        {
            objectsList.Items.Add(assetId);
        }

        scenesList.Items.Clear();
        foreach (var assetId in storage.List("ASD"))
        {
            scenesList.Items.Add(assetId);
        }
    }

    private SpaceTime BuildPlacementFrom(double x, double y, double z, double startTime, double endTime) => new()
    {
        SpatialAttitude1 = new SpatialAttitude
        {
            ObjectSpatialAttitudeID = Guid.NewGuid().ToString(),
            Position = new Position { PositionID = Guid.NewGuid().ToString(), CartPosition = new double[] { x, y, z } },
            Orientation = new Mpai.Core.Orientation { OrientationID = Guid.NewGuid().ToString(), EulerAngles = new double[] { 0, 0, 0 } }
        },
        Time = new SimpleTime
        {
            SimpleTimeData = new() { new TimeSegment { FlagsByte = 0, StartTime = startTime, EndTime = endTime, TimeType = false, TimeUnit = "00" } }
        }
    };

    private SpaceTime BuildPlacementFromFields() =>
        BuildPlacementFrom((double)positionX.Value, (double)positionY.Value, (double)positionZ.Value, (double)startTimeSeconds.Value, (double)endTimeSeconds.Value);

    // The listener is drawn as a permanent, always-present canvas item -
    // same drag mechanism as an object, distinguished only by its label.
    private void RedrawListenerOnCanvas()
    {
        canvas.Items.RemoveAll(i => i.Label == ListenerLabel);
        var pos = currentListener.CartPosition ?? new double[] { 0, 0, 0 };
        canvas.Items.Add(new PlacementCanvas.Item { Label = ListenerLabel, X = pos[0], Y = pos.Length > 1 ? pos[1] : 0, Z = pos.Length > 2 ? pos[2] : 0 });
        canvas.RefreshDisplay();
    }

    private void OnCanvasEmptySpaceClicked(double worldX, double worldY)
    {
        positionX.Value = (decimal)Math.Clamp(worldX, (double)positionX.Minimum, (double)positionX.Maximum);
        positionY.Value = (decimal)Math.Clamp(worldY, (double)positionY.Minimum, (double)positionY.Maximum);
        Log($"Canvas aimed at ({positionX.Value},{positionY.Value}) - click 'Add' to place, or 'Move' to relocate the last-placed item there.");
    }

    private void OnCanvasItemMoved(PlacementCanvas.Item item)
    {
        if (item.Label == ListenerLabel)
        {
            currentListener = new PointOfView
            {
                PointOfViewID = currentListener.PointOfViewID,
                CartPosition = new double[] { item.X, item.Y, item.Z },
                Orientation = currentListener.Orientation
            };

            // Persist immediately if a scene is currently loaded/targeted -
            // this is what makes the listener position survive a restart,
            // per AseAim.SetSceneListener (confirmed by the corrected
            // schema). If no scene is targeted yet, the position is still
            // held here and will be included the next time a scene is saved.
            if (draftTargetSceneId != null)
            {
                try
                {
                    ase.SetSceneListener(draftTargetSceneId, currentListener);
                    Log($"Listener moved to ({item.X},{item.Y}) and saved to {draftTargetSceneId}.");
                }
                catch (Exception failure)
                {
                    Log($"ERROR saving listener position: {failure.Message}");
                }
            }
            else
            {
                Log($"Listener moved to ({item.X},{item.Y}) - will be saved with the next scene you save.");
            }
            return;
        }

        var index = canvas.Items.IndexOf(item);
        if (index < 0 || index >= sceneDraft.Placements.Count) return;

        var placement = sceneDraft.Placements[index];
        var existing = placement.SpaceTime;
        var (_, _, z) = ExtractPosition(existing);
        var (start, end) = ExtractTimeRange(existing);

        placement.SpaceTime = BuildPlacementFrom(item.X, item.Y, z, start, end);
        lastPlacedItem = item;
        Log($"Moved {placement.AssetId} in draft to ({item.X},{item.Y}) - not saved yet.");
    }

    private void OnCanvasItemRightClicked(PlacementCanvas.Item item)
    {
        if (item.Label == ListenerLabel)
        {
            using var listenerDialog = new PlacementDetailsDialog(ListenerLabel, item.Z, 0, 0);
            if (listenerDialog.ShowDialog(this) != DialogResult.OK) return;
            item.Z = listenerDialog.Z;
            currentListener = new PointOfView
            {
                PointOfViewID = currentListener.PointOfViewID,
                CartPosition = new double[] { item.X, item.Y, item.Z },
                Orientation = currentListener.Orientation
            };
            canvas.RefreshDisplay();
            Log($"Listener Z set to {item.Z}.");
            return;
        }

        var index = canvas.Items.IndexOf(item);
        if (index < 0 || index >= sceneDraft.Placements.Count) return;

        using var dialog = new PlacementDetailsDialog(item.Label, item.Z, item.StartTime, item.EndTime);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        item.Z = dialog.Z;
        item.StartTime = dialog.StartTime;
        item.EndTime = dialog.EndTime;

        var placement = sceneDraft.Placements[index];
        placement.SpaceTime = BuildPlacementFrom(item.X, item.Y, item.Z, item.StartTime, item.EndTime);

        canvas.RefreshDisplay();
        Log($"Updated {placement.AssetId} in draft: Z={item.Z}, t={item.StartTime}-{item.EndTime}s - not saved yet.");
    }

    private static (double X, double Y, double Z) ExtractPosition(SpaceTime? spaceTime)
    {
        var pos = spaceTime?.SpatialAttitude1?.Position?.CartPosition;
        return pos is { Length: >= 3 } ? (pos[0], pos[1], pos[2]) : (0, 0, 0);
    }

    private static (double Start, double End) ExtractTimeRange(SpaceTime? spaceTime)
    {
        var segment = spaceTime?.Time?.SimpleTimeData?.FirstOrDefault();
        return (segment?.StartTime ?? 0, segment?.EndTime ?? 5);
    }

    // "Load" - materializes the selected scene and populates the draft AND
    // canvas with its EXISTING content (objects and listener), so you can
    // see what's already there before adding to or moving anything -
    // rather than starting from an empty canvas while silently targeting
    // an existing scene underneath.
    private void LoadSelectedScene()
    {
        if (scenesList.SelectedItem is not string sceneId)
        {
            MessageBox.Show(this, "Select a Scene first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var materialized = ase.Materialize(sceneId);

            sceneDraft.Clear();
            canvas.Items.Clear();

            foreach (var entry in materialized.AudioObjects ?? new())
            {
                var id = entry.ObjectIDOrObject?.AudioObjectID;
                if (id is null) continue;

                sceneDraft.AddPlacement(id, entry.AudioObjectSpaceTime);
                var (x, y, z) = ExtractPosition(entry.AudioObjectSpaceTime);
                var (start, end) = ExtractTimeRange(entry.AudioObjectSpaceTime);
                canvas.Items.Add(new PlacementCanvas.Item { Label = id, X = x, Y = y, Z = z, StartTime = start, EndTime = end });
            }

            draftTargetSceneId = sceneId;

            if (materialized.ListenerPointOfView != null)
            {
                currentListener = materialized.ListenerPointOfView;
            }
            RedrawListenerOnCanvas();

            Log($"Loaded {sceneId}: {sceneDraft.Placements.Count} object(s) on the canvas, ready to edit.");
        }
        catch (Exception failure)
        {
            Log($"ERROR loading scene: {failure.Message}");
        }
    }

    private void AddSelectedObjectToDraft()
    {
        if (objectsList.SelectedItem is not string objectId)
        {
            MessageBox.Show(this, "Select an Object first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var placement = BuildPlacementFromFields();
        sceneDraft.AddPlacement(objectId, placement);

        var newItem = new PlacementCanvas.Item
        {
            Label = objectId,
            X = (double)positionX.Value,
            Y = (double)positionY.Value,
            Z = (double)positionZ.Value,
            StartTime = (double)startTimeSeconds.Value,
            EndTime = (double)endTimeSeconds.Value
        };
        canvas.Items.Add(newItem);
        lastPlacedItem = newItem;
        canvas.RefreshDisplay();

        var target = draftTargetSceneId is null ? "a NEW scene" : $"existing scene {draftTargetSceneId}";
        Log($"Staged {objectId} in draft (not saved yet) - {sceneDraft.Placements.Count} placement(s) pending, will save into {target}.");
    }

    // "Move" - a field-driven alternative to dragging: re-applies whatever
    // is currently in the Position/time fields to the LAST item that was
    // placed or moved (via Add, or a previous Move/drag).
    private void MoveLastPlacedItem()
    {
        if (lastPlacedItem is null || lastPlacedItem.Label == ListenerLabel)
        {
            MessageBox.Show(this, "Add an object to the canvas first (or drag it directly).", "Nothing to move", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var index = canvas.Items.IndexOf(lastPlacedItem);
        if (index < 0 || index >= sceneDraft.Placements.Count) return;

        lastPlacedItem.X = (double)positionX.Value;
        lastPlacedItem.Y = (double)positionY.Value;
        lastPlacedItem.Z = (double)positionZ.Value;
        lastPlacedItem.StartTime = (double)startTimeSeconds.Value;
        lastPlacedItem.EndTime = (double)endTimeSeconds.Value;

        sceneDraft.Placements[index].SpaceTime = BuildPlacementFromFields();
        canvas.RefreshDisplay();

        Log($"Moved {lastPlacedItem.Label} to ({positionX.Value},{positionY.Value},{positionZ.Value}) via fields - not saved yet.");
    }

    private void SaveDraftAsScene()
    {
        if (sceneDraft.IsEmpty)
        {
            MessageBox.Show(this, "Nothing staged yet.", "Draft is empty", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var saved = SceneDraftReplayer.SaveSceneComposition(ase, draftTargetSceneId, sceneDraft);
            saved = ase.SetSceneListener(saved.AssetId, currentListener);

            var target = draftTargetSceneId is null ? "new scene" : $"added to {draftTargetSceneId}";
            Log($"Saved draft ({sceneDraft.Placements.Count} placement(s)) -> {saved.AssetId} ({target}), listener included.");

            sceneDraft.Clear();
            draftTargetSceneId = saved.AssetId;
            lastPlacedItem = null;
            canvas.Items.Clear();
            RedrawListenerOnCanvas();

            RefreshLists();
            scenesList.SelectedItem = saved.AssetId;
        }
        catch (Exception failure)
        {
            Log($"ERROR saving draft: {failure.Message}");
        }
    }

    private void ClearDraft()
    {
        var count = sceneDraft.Placements.Count;
        sceneDraft.Clear();
        draftTargetSceneId = null;
        lastPlacedItem = null;
        canvas.Items.Clear();
        RedrawListenerOnCanvas();
        Log($"Cleared ({count} pending placement(s) discarded - none of it was ever saved).");
    }

    private void DeliverSelectedScenePannedToFile()
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
            var delivery = new PannedAudioDelivery(dialog.SelectedPath);
            var asd = new AsdAim(delivery);
            var listener = materialized.ListenerPointOfView ?? currentListener;

            asd.DeliverSceneAsync(materialized, listener).GetAwaiter().GetResult();
            Log($"Delivered (panned) {sceneId} ({materialized.AudioObjectCount} object(s)) -> {dialog.SelectedPath}");
        }
        catch (Exception failure)
        {
            Log($"ERROR delivering scene: {failure.Message}");
        }
    }

    private async void PlaySelectedScenePanned()
    {
        if (scenesList.SelectedItem is not string sceneId)
        {
            MessageBox.Show(this, "Select a Scene first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var materialized = ase.Materialize(sceneId);
            var downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var pannedFolder = Path.Combine(downloadsFolder, "ASMApp_Panned_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            var delivery = new PannedAudioDelivery(pannedFolder);
            var asd = new AsdAim(delivery);
            var listener = materialized.ListenerPointOfView ?? currentListener;

            Log($"Rendering panned audio for scene {sceneId} ({materialized.AudioObjectCount} object(s)) ...");
            await asd.DeliverSceneAsync(materialized, listener);

            // PLAY, rather than hand to the shell.
            //
            // This used to call Process.Start with UseShellExecute, which asks
            // Windows to open each file in whatever application is associated
            // with .wav - a media player window, or nothing at all if no
            // association exists. "To Speaker" should reach the speaker.
            //
            // The rendered files stay on disk either way, so "To File" still
            // shows the same result without playing it.
            var rendered = Directory.GetFiles(pannedFolder, "*.wav");

            if (rendered.Length == 0)
            {
                Log("Nothing was rendered - the objects may carry no audio.");
                return;
            }

            foreach (var file in rendered)
            {
                var length = new FileInfo(file).Length;
                Log($"Playing {Path.GetFileName(file)} ({length:N0} bytes)");

                if (length <= 44)
                {
                    Log("  ... header only, no samples - nothing to hear.");
                    continue;
                }

                // One after another. Simultaneous playback would need the files
                // mixed, which is the renderer's job and not this button's.
                await Task.Run(() =>
                {
                    using var player = new System.Media.SoundPlayer(file);
                    player.PlaySync();
                });
            }

            Log($"Rendered files kept in {pannedFolder}");
        }
        catch (Exception failure)
        {
            Log($"ERROR playing panned scene: {failure.Message}");
        }
    }

    private void DeleteSelectedScene()
    {
        if (scenesList.SelectedItem is not string sceneId)
        {
            MessageBox.Show(this, "Select a Scene first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var referrers = ase.ReferencedBy(sceneId);
            if (referrers.Count > 0)
            {
                var list = string.Join(", ", referrers);
                var answer = MessageBox.Show(this,
                    $"{sceneId} is referenced by: {list}.\n\nDelete {sceneId} together with those {referrers.Count} referencing item(s)?",
                    "Delete referenced scene", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) { Log($"Delete of {sceneId} cancelled."); return; }

                foreach (var referrer in referrers) ase.Delete(referrer);
                ase.Delete(sceneId);
                Log($"Deleted {sceneId} and {referrers.Count} referencing item(s): {list}.");
            }
            else
            {
                ase.Delete(sceneId);
                Log($"Deleted {sceneId} (was not referenced).");
            }

            RefreshLists();
        }
        catch (Exception failure)
        {
            Log($"ERROR deleting {sceneId}: {failure.Message}");
        }
    }

    private void ShowSelectedSceneInfo()
    {
        if (scenesList.SelectedItem is not string sceneId)
        {
            MessageBox.Show(this, "Select a Scene first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var info = storage.GetKeyInfo(sceneId);
            Log($"{sceneId} - StoredBy={info.StoredBy}, RequestedBy={info.RequestedBy}, StoredAt={info.StoredAt:yyyy-MM-dd HH:mm:ss} UTC");
        }
        catch (Exception failure)
        {
            Log($"ERROR reading info for {sceneId}: {failure.Message}");
        }
    }

    private void Log(string line)
    {
        logBox.AppendText(line + Environment.NewLine);
    }
}