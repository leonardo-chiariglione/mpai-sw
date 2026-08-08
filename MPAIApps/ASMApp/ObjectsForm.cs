using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Mpai.Cae.Aoe;
using Mpai.Cae.Asd;
using Mpai.Aims.Audio;
using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Repository;

namespace MPAIApps.ASMApp;

// The Objects window - Mode 1 (AUO editing). Cloned from the working
// ScenesForm layout/structure (same grid pattern, same canvas, same
// Log-then-fixed-height-list bottom section, same SplitContainer timing
// fix), adapted to Objects' own instructions: a single-item canvas
// (exactly one dot - the object currently selected) rather than a
// multi-placement draft, and one Objects column instead of two.
public sealed class ObjectsForm : Form
{
    private readonly AssetRepository repository;
    private readonly AoeAim aoe;
    private ScenesForm? sibling;

    private WasapiAudioAcquisition? activeRecording;

    private readonly ListBox objectsList = new() { Dock = DockStyle.Fill };
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

    private readonly SceneDraft objectDraft = new();
    private string? objectDraftTargetId;
    private bool suppressSelectionSync;

    public ObjectsForm(AssetRepository repository, AoeAim aoe)
    {
        this.repository = repository;
        this.aoe = aoe;

        Text = "ASMApp - Objects (AUO editing)";
        Width = 1000;
        Height = 835;
        StartPosition = FormStartPosition.Manual;
        Location = new System.Drawing.Point(60, 60);

        canvas.EmptySpaceClicked += OnCanvasEmptySpaceClicked;
        canvas.ItemMoved += OnCanvasItemMoved;
        canvas.ItemRightClicked += OnCanvasItemRightClicked;
        objectsList.SelectedIndexChanged += (_, _) => { if (!suppressSelectionSync) LoadSelectedObjectOntoCanvas(); };

        var bringScenesToFrontButton = new Button { Text = "Show Scenes Window", Width = 160, Height = 26 };
        bringScenesToFrontButton.Click += (_, _) => { sibling?.BringToFront(); sibling?.Activate(); };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 4,
            Height = 32 * 4 + 12,
            Padding = new Padding(4, 4, 4, 4)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 4; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        static Label RowLabel(string text) => new() { Text = text, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold) };
        static Button Cell(string text) => new() { Text = text, Dock = DockStyle.Fill, Margin = new Padding(2) };

        var acquireFileButton = Cell("File");
        acquireFileButton.Click += (_, _) => CreateObjectFromFile();
        var acquireDeviceButton = Cell("Device");
        acquireDeviceButton.Click += (_, _) => ToggleRecording(acquireDeviceButton);
        grid.Controls.Add(RowLabel("Acquire"), 0, 0);
        grid.Controls.Add(acquireFileButton, 1, 0);
        grid.Controls.Add(acquireDeviceButton, 2, 0);

        var deliverFileButton = Cell("File");
        deliverFileButton.Click += (_, _) => DeliverSelectedObjectToFile();
        var deliverDeviceButton = Cell("Device");
        deliverDeviceButton.Click += (_, _) => PlaySelectedObject();
        grid.Controls.Add(RowLabel("Deliver"), 0, 1);
        grid.Controls.Add(deliverFileButton, 1, 1);
        grid.Controls.Add(deliverDeviceButton, 2, 1);

        var editStageButton = Cell("Stage");
        editStageButton.Click += (_, _) => StageObjectEdit();
        var editClearButton = Cell("Clear");
        editClearButton.Click += (_, _) => ClearObjectEdit();
        grid.Controls.Add(RowLabel("Edit"), 0, 2);
        grid.Controls.Add(editStageButton, 1, 2);
        grid.Controls.Add(editClearButton, 2, 2);

        var repoSaveButton = Cell("Save");
        repoSaveButton.Click += (_, _) => SaveObjectEdit();
        var repoClearButton = Cell("Clear");
        repoClearButton.Click += (_, _) => ClearObjectEdit();
        grid.Controls.Add(RowLabel("Repository"), 0, 3);
        grid.Controls.Add(repoSaveButton, 1, 3);
        grid.Controls.Add(repoClearButton, 2, 3);

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

        var canvasPanel = new Panel { Dock = DockStyle.Fill };
        canvasPanel.Controls.Add(canvas);
        canvasPanel.Controls.Add(new Label
        {
            Text = "Click empty space to aim; drag the dot to move it; right-click it to edit Z/time.",
            Dock = DockStyle.Top,
            Height = 16,
            Font = new System.Drawing.Font("Segoe UI", 7.5f),
            ForeColor = System.Drawing.Color.DimGray
        });

        var logPanel = new Panel { Dock = DockStyle.Fill };
        logPanel.Controls.Add(logBox);
        logPanel.Controls.Add(new Label { Text = "Log:", Dock = DockStyle.Top, Height = 16, Font = new System.Drawing.Font("Segoe UI", 7.5f) });

        var refreshButton = new Button { Text = "Refresh", Dock = DockStyle.Top, Height = 22 };
        refreshButton.Click += (_, _) => RefreshList();

        var listPanel = new Panel { Dock = DockStyle.Fill };
        listPanel.Controls.Add(objectsList);
        listPanel.Controls.Add(new Label { Text = "Objects:", Dock = DockStyle.Top, Height = 16, Font = new System.Drawing.Font("Segoe UI", 7.5f) });

        var namesPanel = new Panel { Dock = DockStyle.Bottom, Height = 200 };
        namesPanel.Controls.Add(listPanel);
        namesPanel.Controls.Add(refreshButton);

        var canvasAndLogSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = System.Windows.Forms.Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel1   // canvas (Panel1) stays exactly this size; only Log (Panel2) absorbs any change in available space
        };
        canvasAndLogSplit.Panel1.Controls.Add(canvasPanel);
        canvasAndLogSplit.Panel2.Controls.Add(logPanel);

        Controls.Add(canvasAndLogSplit);
        Controls.Add(namesPanel);

        var switchPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        switchPanel.Controls.Add(bringScenesToFrontButton);
        Controls.Add(switchPanel);
        Controls.Add(topPanel);

        // Set AFTER the control hierarchy has its real, Dock-resolved size -
        // setting this during construction silently clamps against the
        // control's tiny pre-layout default size, which combined with
        // FixedPanel then locks in that wrong small value permanently.
        Load += (_, _) => canvasAndLogSplit.SplitterDistance = 280;

        RefreshList();
    }

    public void SetSibling(ScenesForm scenesForm) => sibling = scenesForm;

    public void RefreshList()
    {
        suppressSelectionSync = true;
        objectsList.Items.Clear();
        foreach (var asset in repository.FindAssets(AssetType.AUO))
        {
            objectsList.Items.Add(asset.AssetId);
        }
        suppressSelectionSync = false;
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

    // Shows the selected object's CURRENT stored position (if any) as the
    // single dot on the canvas, and pre-fills the fields to match - so
    // selecting an object shows where it already is, not a blank canvas.
    private void LoadSelectedObjectOntoCanvas()
    {
        canvas.Items.Clear();

        if (objectsList.SelectedItem is not string objectId)
        {
            canvas.RefreshDisplay();
            return;
        }

        try
        {
            var materialized = aoe.Materialize(objectId);
            var (x, y, z) = ExtractPosition(materialized.AudioObjectSpaceTime);
            var (start, end) = ExtractTimeRange(materialized.AudioObjectSpaceTime);

            positionX.Value = (decimal)x;
            positionY.Value = (decimal)y;
            positionZ.Value = (decimal)z;
            startTimeSeconds.Value = (decimal)start;
            endTimeSeconds.Value = (decimal)end;

            canvas.Items.Add(new PlacementCanvas.Item { Label = objectId, X = x, Y = y, Z = z, StartTime = start, EndTime = end });
            canvas.RefreshDisplay();
        }
        catch (Exception failure)
        {
            Log($"ERROR loading {objectId} onto canvas: {failure.Message}");
        }
    }

    private void OnCanvasEmptySpaceClicked(double worldX, double worldY)
    {
        positionX.Value = (decimal)Math.Clamp(worldX, (double)positionX.Minimum, (double)positionX.Maximum);
        positionY.Value = (decimal)Math.Clamp(worldY, (double)positionY.Minimum, (double)positionY.Maximum);
        Log($"Canvas aimed at ({positionX.Value},{positionY.Value}) - click 'Stage' to apply, then 'Save' to commit.");
    }

    private void OnCanvasItemMoved(PlacementCanvas.Item item)
    {
        positionX.Value = (decimal)item.X;
        positionY.Value = (decimal)item.Y;
        Log($"Moved to ({item.X},{item.Y}) on canvas - click 'Stage' to apply, then 'Save' to commit.");
    }

    private void OnCanvasItemRightClicked(PlacementCanvas.Item item)
    {
        using var dialog = new PlacementDetailsDialog(item.Label, item.Z, item.StartTime, item.EndTime);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        item.Z = dialog.Z;
        item.StartTime = dialog.StartTime;
        item.EndTime = dialog.EndTime;
        positionZ.Value = (decimal)item.Z;
        startTimeSeconds.Value = (decimal)item.StartTime;
        endTimeSeconds.Value = (decimal)item.EndTime;

        canvas.RefreshDisplay();
        Log($"Z/time updated on canvas - click 'Stage' to apply, then 'Save' to commit.");
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
            RefreshList();
        }
        catch (Exception failure)
        {
            Log($"ERROR creating object: {failure.Message}");
        }
    }

    private async void ToggleRecording(Button button)
    {
        if (activeRecording is null)
        {
            try
            {
                activeRecording = new WasapiAudioAcquisition();
                activeRecording.StartAcquire();
                button.Text = "Stop";
                Log("Recording... click 'Stop' when done.");
            }
            catch (Exception failure)
            {
                activeRecording = null;
                Log($"ERROR starting recording: {failure.Message}");
            }
            return;
        }

        try
        {
            var bao = await activeRecording.StopAcquireAsync();
            var audioObjectAsset = aoe.CreateObject(bao);
            Log($"Created {audioObjectAsset.AssetId} from microphone recording ({bao.Data.Length} bytes)");
            RefreshList();
        }
        catch (Exception failure)
        {
            Log($"ERROR stopping recording: {failure.Message}");
        }
        finally
        {
            activeRecording = null;
            button.Text = "Device";
        }
    }

    private void DeliverSelectedObjectToFile()
    {
        if (objectsList.SelectedItem is not string objectId)
        {
            MessageBox.Show(this, "Select an Object first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        AudioObject materialized;
        try
        {
            materialized = aoe.Materialize(objectId);
        }
        catch (Exception failure)
        {
            Log($"ERROR reading object: {failure.Message}");
            return;
        }

        var suggestedName = FindFirstBaoId(materialized);
        string destinationFolder;

        if (suggestedName != null)
        {
            using var saveDialog = new SaveFileDialog
            {
                Title = "Choose where to deliver this object",
                FileName = $"{suggestedName}.wav",
                Filter = "WAV files (*.wav)|*.wav"
            };
            if (saveDialog.ShowDialog(this) != DialogResult.OK) return;
            destinationFolder = Path.GetDirectoryName(saveDialog.FileName) ?? ".";
        }
        else
        {
            using var folderDialog = new FolderBrowserDialog { Description = "Choose a destination folder for delivery" };
            if (folderDialog.ShowDialog(this) != DialogResult.OK) return;
            destinationFolder = folderDialog.SelectedPath;
        }

        try
        {
            var delivery = new FileAudioDelivery(destinationFolder);
            var asd = new AsdAim(delivery);
            var listener = new PointOfView { PointOfViewID = "default-listener", CartPosition = new double[] { 0, 0, 0 }, Orientation = new double[] { 0, 0, 0 } };

            asd.DeliverObjectAsync(materialized, listener).GetAwaiter().GetResult();
            Log($"Delivered {objectId} -> {destinationFolder}");
        }
        catch (Exception failure)
        {
            Log($"ERROR delivering object: {failure.Message}");
        }
    }

    private static string? FindFirstBaoId(AudioObject audioObject)
    {
        var direct = audioObject.BasicAudioObjects?.FirstOrDefault()?.BAObjectIDOrBAObject?.BasicAudioObjectID;
        if (direct != null) return direct;

        foreach (var sub in audioObject.SubAudioObjects ?? Enumerable.Empty<SubAudioObjectEntry>())
        {
            if (sub.SubAObjectIDOrSubAObject is null) continue;
            var nested = FindFirstBaoId(sub.SubAObjectIDOrSubAObject);
            if (nested != null) return nested;
        }

        return null;
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
            var listener = new PointOfView { PointOfViewID = "default-listener", CartPosition = new double[] { 0, 0, 0 }, Orientation = new double[] { 0, 0, 0 } };

            Log($"Playing {objectId} ...");
            await asd.DeliverObjectAsync(materialized, listener);
            Log($"Finished playing {objectId}");
        }
        catch (Exception failure)
        {
            Log($"ERROR playing object: {failure.Message}");
        }
    }

    private void StageObjectEdit()
    {
        if (objectsList.SelectedItem is not string objectId)
        {
            MessageBox.Show(this, "Select an Object first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        objectDraftTargetId = objectId;
        objectDraft.Clear();
        objectDraft.AddPlacement(objectId, BuildPlacementFromFields());
        objectDraft.PendingAcousticProfile = new AcousticProfile
        {
            AcousticProfileID = Guid.NewGuid().ToString(),
            FrequencyRange = new FrequencyRange
            {
                MinFrequencyHz = (double)minFrequencyHz.Value,
                MaxFrequencyHz = (double)maxFrequencyHz.Value
            },
            Loudness = (double)loudnessLufs.Value
        };

        canvas.Items.Clear();
        canvas.Items.Add(new PlacementCanvas.Item
        {
            Label = objectId,
            X = (double)positionX.Value,
            Y = (double)positionY.Value,
            Z = (double)positionZ.Value,
            StartTime = (double)startTimeSeconds.Value,
            EndTime = (double)endTimeSeconds.Value
        });
        canvas.RefreshDisplay();

        Log($"Staged edit for {objectId}: position ({positionX.Value},{positionY.Value},{positionZ.Value}), " +
            $"AcousticProfile {minFrequencyHz.Value}-{maxFrequencyHz.Value} Hz, {loudnessLufs.Value} LUFS - not saved yet.");
    }

    private void SaveObjectEdit()
    {
        if (objectDraftTargetId is null || objectDraft.IsEmpty)
        {
            MessageBox.Show(this, "Nothing staged yet - use 'Stage' first.", "Draft is empty", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var updated = SceneDraftReplayer.SaveObjectEdit(aoe, objectDraftTargetId, objectDraft);
            Log($"Saved object edit -> new version {updated.AssetId} (was {objectDraftTargetId}).");

            objectDraft.Clear();
            objectDraftTargetId = null;

            RefreshList();
            objectsList.SelectedItem = updated.AssetId;
        }
        catch (Exception failure)
        {
            Log($"ERROR saving object edit: {failure.Message}");
        }
    }

    private void ClearObjectEdit()
    {
        var hadTarget = objectDraftTargetId;
        objectDraft.Clear();
        objectDraftTargetId = null;
        Log(hadTarget is null ? "Nothing was staged." : $"Discarded staged edit for {hadTarget} - it was never saved.");
    }

    private void Log(string line)
    {
        logBox.AppendText(line + Environment.NewLine);
    }
}