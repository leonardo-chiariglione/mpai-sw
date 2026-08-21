using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Mpai.Cae.Aoe;
using Mpai.Cae.Asd;
using Mpai.Aims.Audio;
using Mpai.Core;
using Mpai.Core.OSD;
using AIF.SharedStorage;

namespace MPAIApps.ASMApp;

// The Objects window - Mode 1 (AUO editing). Cloned from the working
// ScenesForm layout/structure (same grid pattern, same canvas, same
// Log-then-fixed-height-list bottom section, same SplitContainer timing
// fix), adapted to Objects' own instructions: a single-item canvas
// (exactly one dot - the object currently selected) rather than a
// multi-placement draft, and one Objects column instead of two.
public sealed class ObjectsForm : Form
{
    private readonly ISharedStorage storage;
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

    // WHAT IS OPEN.
    //
    // CAE-AOE edits one Object at a time - that is why a User Command need not
    // name its target - and until now this window had no notion of it, working
    // from whatever happened to be selected in the list. Composition makes the
    // distinction unavoidable: two Objects are involved, one being built and one
    // joining it, and "the selected one" cannot mean both.
    private string? openObjectId;

    // THE WINDOW'S LISTENER.
    //
    // A listener is a fact about the environment, not about any Object: it sits
    // at the origin in a perfectly silent room before anything exists, and it is
    // movable straight away. You sit down first, and then arrange sound around
    // yourself - which matters, because whatever you add goes to the ORIGIN, so
    // a listener left there would be inside it.
    //
    // Selecting an Object replaces it: the containing entity sets the space, so
    // that Object's listener becomes the one in the room. A listener stored on
    // something now CONTAINED is still there and is simply irrelevant, exactly
    // as an Object keeps its own attributes until a context provides them.
    //
    // One listener per window. Two windows, two rooms; several listeners in one
    // room is not CAE-ASM V1.0.
    private PointOfView listener = ListenerAt(0, 0, 0);

    // Set when the listener is dragged and not yet written to an Object. Every
    // edit mints a new key, so saving on each mouse move would turn one audition
    // into fifty revisions.
    private bool listenerMoved;

    private const string ListenerLabel = "Listener";

    private readonly Label openLabel = new()
    {
        Dock = DockStyle.Top,
        Height = 20,
        Padding = new Padding(6, 3, 0, 0),
        Text = "Nothing open."
    };

    public ObjectsForm(ISharedStorage storage, AoeAim aoe)
    {
        this.storage = storage;
        this.aoe = aoe;

        Text = "ASMApp - Objects (AUO editing)";
        Width = 1000;
        Height = 835;
        StartPosition = FormStartPosition.Manual;
        Location = new System.Drawing.Point(60, 60);

        canvas.EmptySpaceClicked += OnCanvasEmptySpaceClicked;
        canvas.ItemMoved += OnCanvasItemMoved;
        canvas.ItemRightClicked += OnCanvasItemRightClicked;
        canvas.LockedItemClicked += OnCanvasLockedItemClicked;
        objectsList.SelectedIndexChanged += (_, _) => { if (!suppressSelectionSync) LoadSelectedObjectOntoCanvas(); };

        var bringScenesToFrontButton = new Button { Text = "Show Scenes Window", Width = 160, Height = 26 };
        bringScenesToFrontButton.Click += (_, _) => { sibling?.BringToFront(); sibling?.Activate(); };

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

        // Row 0: Acquire/Deliver - From File | From Mic | To File | To Speaker
        var acquireFileButton = Cell("From File");
        acquireFileButton.Click += (_, _) => CreateObjectFromFile();
        var acquireDeviceButton = Cell("From Mic");
        acquireDeviceButton.Click += (_, _) => ToggleRecording(acquireDeviceButton);
        var deliverFileButton = Cell("To File");
        deliverFileButton.Click += (_, _) => DeliverSelectedObjectToFile();
        var deliverDeviceButton = Cell("To Speaker");
        deliverDeviceButton.Click += (_, _) => PlaySelectedObject();
        grid.Controls.Add(RowLabel("Acq/Deliver"), 0, 0);
        grid.Controls.Add(acquireFileButton, 1, 0);
        grid.Controls.Add(acquireDeviceButton, 2, 0);
        grid.Controls.Add(deliverFileButton, 3, 0);
        grid.Controls.Add(deliverDeviceButton, 4, 0);

        // Row 1: Edit - Stage | Discard
        var editStageButton = Cell("Stage Edit");
        editStageButton.Click += (_, _) => StageObjectEdit();
        var editClearButton = Cell("Discard Edit");
        editClearButton.Click += (_, _) => ClearObjectEdit();

        // Composition, in the two free cells of the Edit row. It existed in the
        // engine with no caller anywhere, so no full Audio Object could be made
        // through the application at all.
        var openButton = Cell("Open Selected");
        openButton.Click += (_, _) => OpenSelectedObject();
        var composeButton = Cell("Add to Open");
        composeButton.Click += (_, _) => ComposeIntoOpenObject();
        grid.Controls.Add(RowLabel("Edit"), 0, 1);
        grid.Controls.Add(editStageButton, 1, 1);
        grid.Controls.Add(editClearButton, 2, 1);
        grid.Controls.Add(openButton, 3, 1);
        grid.Controls.Add(composeButton, 4, 1);

        // Row 2: Repository - Save | Discard | Delete | Info
        var repoSaveButton = Cell("Save Changes");
        repoSaveButton.Click += (_, _) => SaveObjectEdit();
        var repoClearButton = Cell("Discard Changes");
        repoClearButton.Click += (_, _) => ClearObjectEdit();
        var repoDeleteButton = Cell("Delete");
        repoDeleteButton.Click += (_, _) => DeleteSelectedObject();
        var repoInfoButton = Cell("Info");
        repoInfoButton.Click += (_, _) => ShowSelectedObjectInfo();
        grid.Controls.Add(RowLabel("Repository"), 0, 2);
        grid.Controls.Add(repoSaveButton, 1, 2);
        grid.Controls.Add(repoClearButton, 2, 2);
        grid.Controls.Add(repoDeleteButton, 3, 2);
        grid.Controls.Add(repoInfoButton, 4, 2);

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
        topPanel.Controls.Add(openLabel);
        topPanel.Controls.Add(positionPanel);
        topPanel.Controls.Add(grid);

        var canvasPanel = new Panel { Dock = DockStyle.Fill };
        canvasPanel.Controls.Add(canvas);
        canvasPanel.Controls.Add(new Label
        {
            Text = "Click empty space to aim; drag the dot to move it; right-click it to edit Z/time. Hover it for orientation.",
            Dock = DockStyle.Top,
            Height = 16,
            Font = new System.Drawing.Font("Segoe UI", 7.5f),
            ForeColor = System.Drawing.Color.DimGray
        });

        // Log removed from the UI (kept alive but hidden so Log(...) calls still work).
        logBox.Visible = false;

        var refreshButton = new Button { Text = "Refresh", Dock = DockStyle.Top, Height = 22 };
        refreshButton.Click += (_, _) => RefreshList();

        var listPanel = new Panel { Dock = DockStyle.Fill };
        listPanel.Controls.Add(objectsList);
        listPanel.Controls.Add(new Label { Text = "Objects:", Dock = DockStyle.Top, Height = 16, Font = new System.Drawing.Font("Segoe UI", 7.5f) });

        var namesPanel = new Panel { Dock = DockStyle.Bottom, Height = 200 };
        namesPanel.Controls.Add(listPanel);
        namesPanel.Controls.Add(refreshButton);

        Controls.Add(canvasPanel);   // canvas fills the whole central area now
        Controls.Add(namesPanel);
        Controls.Add(logBox);        // hidden; present only so AppendText has a target

        var switchPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
        switchPanel.Controls.Add(bringScenesToFrontButton);
        Controls.Add(switchPanel);
        Controls.Add(topPanel);


        RefreshList();

        // The listener exists BEFORE anything is selected - an empty window is a
        // silent room with someone in it. ShowListener was reached only from
        // LoadSelectedObjectOntoCanvas, which runs on a selection change, so at
        // startup nothing drew it and what appeared was the canvas's own painted
        // marker: visible, and impossible to drag.
        ShowListener();
        canvas.RefreshDisplay();
    }

    public void SetSibling(ScenesForm scenesForm) => sibling = scenesForm;

    // Open the selected Object: it becomes the one being built.
    private void OpenSelectedObject()
    {
        if (objectsList.SelectedItem is not string objectId)
        {
            MessageBox.Show(this, "Select an Object to open.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        openObjectId   = objectId;
        openLabel.Text = $"Open: {objectId}";
        Log($"Opened {objectId}");
    }

    // Compose the SELECTED Object into the OPEN one.
    //
    // Composing BAO000001 with BAO000002 produces AUO000001 holding both, and
    // what is open becomes that AUO - so composing again adds a third. An Object
    // holding one Object is Basic; one holding more than one is full, and this
    // is the operation that turns the first into the second.
    //
    // The child is placed at whatever the Position fields say, so two voices can
    // stand apart. Left at the origin, the child sits where the container sits -
    // which the schema names as its own default rather than as a gap.
    private void ComposeIntoOpenObject()
    {
        if (openObjectId is null)
        {
            MessageBox.Show(this,
                "Nothing is open. Select an Object, press Open Selected, then " +
                "select the Object to add and press Add to Open.",
                "Nothing open", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (objectsList.SelectedItem is not string childId)
        {
            MessageBox.Show(this, "Select the Object to add.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (childId == openObjectId)
        {
            MessageBox.Show(this, "An Object cannot be composed into itself.",
                "Same Object", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            // NEVER ON TOP OF THE CONTAINER. Position at 0,0,0 means "where the
            // container is", which the schema permits and which puts the child
            // exactly under it: three names on one dot, and nothing to drag
            // apart. A child added with no position chosen goes two metres to
            // the side, where it can be seen and then moved.
            var chosen = positionX.Value != 0 || positionY.Value != 0 || positionZ.Value != 0;

            var placement = chosen
                ? BuildPlacementFromFields()
                : BuildPlacementFrom(2, 0, 0,
                    (double)startTimeSeconds.Value, (double)endTimeSeconds.Value);

            if (!chosen)
            {
                Log("No position chosen - placing it 2 m to the side so it can be seen.");
            }

            var composed = aoe.AddSubObject(openObjectId, childId, placement);

            Log($"Composed {childId} into {openObjectId} -> {composed.AssetId}");

            openObjectId   = composed.AssetId;
            openLabel.Text = $"Open: {composed.AssetId}";

            RefreshList();
            objectsList.SelectedItem = composed.AssetId;
        }
        catch (Exception failure)
        {
            Log($"ERROR composing: {failure.Message}");
        }
    }

    public void RefreshList()
    {
        suppressSelectionSync = true;
        objectsList.Items.Clear();

        // BOTH kinds. An Object holding one Object is Basic and one holding more
        // than one is full, so acquiring a file now yields a BAO and the list
        // showed nothing at all: the objects existed and the window could not
        // see them.
        foreach (var assetId in storage.List("BAO"))
        {
            objectsList.Items.Add(assetId);
        }

        foreach (var assetId in storage.List("AUO"))
        {
            objectsList.Items.Add(assetId);
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

        // The listener is drawn whether or not anything else exists - an empty
        // window is a silent room with someone in it, not a blank canvas.
        ShowListener();

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

            // THE SELECTED OBJECT IS THE ORIGIN, and locked there. It is the
            // thing being auditioned; there is nothing to move it relative to.
            // What moves is the ear - the opposite of a Scene, where Objects are
            // placed around a listener.
            canvas.Items.Add(new PlacementCanvas.Item
            {
                Label = objectId, X = 0, Y = 0, Z = 0,
                StartTime = start, EndTime = end, Locked = true
            });

            // ITS CHILDREN, where the container says they sit.
            //
            // A composition could be arranged once, by typing into the Position
            // fields before pressing Add to Open, and then never seen: the canvas
            // drew the container alone. Its children are drawn now, and each may
            // be dragged - which is what makes composing an arrangement rather
            // than a list.
            foreach (var child in ChildrenOf(materialized))
            {
                canvas.Items.Add(child);
            }

            // This Object is now the containing entity, so its listener becomes
            // the one in the room - unless one has been moved and not yet saved,
            // which is a deliberate act and should not be silently discarded.
            if (!listenerMoved && ListenerOf(materialized) is PointOfView stored)
            {
                listener = stored;
                ShowListener();
            }

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
        if (item.Label == ListenerLabel)
        {
            listener      = ListenerAt(item.X, item.Y, item.Z);
            listenerMoved = true;

            Log($"Listener at ({item.X},{item.Y}). Objects added from now on record it; " +
                "'Save Changes' writes it to the Object selected.");
            return;
        }

        // A CHILD of the selected Object: moving it changes where the container
        // says it sits, which is an edit to the container.
        if (objectsList.SelectedItem is string containerId &&
            containerId.StartsWith("AUO", StringComparison.Ordinal) &&
            item.Label != containerId)
        {
            try
            {
                var placement = BuildPlacementFrom(item.X, item.Y, item.Z, item.StartTime, item.EndTime);
                var moved     = aoe.MoveSubObject(containerId, item.Label, placement);

                Log($"Moved {item.Label} to ({item.X},{item.Y}) within {containerId} -> {moved.AssetId}");

                if (openObjectId == containerId)
                {
                    openObjectId   = moved.AssetId;
                    openLabel.Text = $"Open: {moved.AssetId}";
                }

                RefreshList();
                objectsList.SelectedItem = moved.AssetId;
            }
            catch (Exception failure)
            {
                Log($"ERROR moving {item.Label}: {failure.Message}");
            }

            return;
        }

        positionX.Value = (decimal)item.X;
        positionY.Value = (decimal)item.Y;
        Log($"Moved to ({item.X},{item.Y}) on canvas - click 'Stage' to apply, then 'Save' to commit.");
    }

    // The children of a composed Object, at the placements the container gives
    // them. A child with no placement of its own sits where the container sits,
    // which the schema names as its default - here, the origin.
    private static IEnumerable<PlacementCanvas.Item> ChildrenOf(AudioObject container)
    {
        // A Basic Object materialises as an Object of ONE, and that one is
        // itself - not a child. Drawing it would put a second dot on top of the
        // first and suggest a composition where there is none.
        var isComposed = (container.BasicAudioObjectCount ?? 0) +
                         (container.SubAudioObjectCount ?? 0) > 1;

        if (!isComposed) yield break;

        foreach (var entry in container.BasicAudioObjects ?? new List<BasicAudioObjectEntry>())
        {
            var id = entry.BAObjectIDOrBAObject?.BasicAudioObjectID;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var (x, y, z) = ExtractPosition(entry.BasicAudioObjectSpaceTime);
            var (start, end) = ExtractTimeRange(entry.BasicAudioObjectSpaceTime);

            yield return new PlacementCanvas.Item
            {
                Label = id, X = x, Y = y, Z = z, StartTime = start, EndTime = end
            };
        }

        foreach (var entry in container.SubAudioObjects ?? new List<SubAudioObjectEntry>())
        {
            var id = entry.SubAObjectIDOrSubAObject?.AudioObjectID;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var (x, y, z) = ExtractPosition(entry.SubAudioObjectSpaceTime);
            var (start, end) = ExtractTimeRange(entry.SubAudioObjectSpaceTime);

            yield return new PlacementCanvas.Item
            {
                Label = id, X = x, Y = y, Z = z, StartTime = start, EndTime = end
            };
        }
    }

    // Draw the listener where it currently stands, replacing any previous one.
    private void ShowListener()
    {
        canvas.Items.RemoveAll(i => i.Label == ListenerLabel);

        var (x, y, z) = PositionOf(listener);

        canvas.Items.Add(new PlacementCanvas.Item
        {
            Label = ListenerLabel, X = x, Y = y, Z = z
        });
    }

    private void OnCanvasLockedItemClicked(PlacementCanvas.Item item)
    {
        Log($"{item.Label} is at the origin: it is what is being auditioned. Drag the Listener instead.");
    }

    // A Point of View at a position.
    //
    // PointOfView carries CartPosition DIRECTLY - it has no nested Position, and
    // ScenesForm has always built one this way. This first version invented a
    // Position object, the same error as inventing X/Y/Z on Position an hour
    // earlier, with the correct example in view both times.
    //
    // Orientation is left alone: the canvas arrow sets Yaw and nothing yet
    // carries it into the stored value.
    private static PointOfView ListenerAt(double x, double y, double z) => new()
    {
        PointOfViewID = Guid.NewGuid().ToString(),
        CartPosition  = new double[] { x, y, z }
    };

    private static PointOfView? ListenerOf(AudioObject materialized)
    {
        if (materialized.UserPoV is not null) return materialized.UserPoV;

        // A Basic Object carries its listener on itself; a composed one on the
        // container. Materialize resolves a BAO into an Object of one, so its
        // listener is on the single entry.
        var basic = materialized.BasicAudioObjects is { Count: 1 }
            ? materialized.BasicAudioObjects[0].BAObjectIDOrBAObject
            : null;

        return basic?.ListenerPointOfView;
    }

    private static (double X, double Y, double Z) PositionOf(PointOfView? listener)
    {
        var cart = listener?.CartPosition;

        return cart is { Length: >= 3 }
            ? (cart[0], cart[1], cart[2])
            : (0, 0, 0);
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

            // The Object goes to the origin and records where the listener is
            // standing - which is why the listener is moved FIRST, before
            // anything is added.
            //
            // WithListener, not a with-expression: BasicAudioObject is a class
            // rather than a record, and copies here are written by hand.
            var bao = BasicAudioObject.FromData(bytes).WithListener(listener);
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
        // A moved listener is an edit in its own right, and needs nothing staged
        // through the placement draft: it is one field on the Object.
        if (listenerMoved && objectsList.SelectedItem is string listenerTarget)
        {
            try
            {
                var updated = listenerTarget.StartsWith("BAO", StringComparison.Ordinal)
                    ? aoe.EditBasicObjectProperties(listenerTarget, listenerPointOfView: listener)
                    : aoe.EditObjectProperties(listenerTarget, listenerPointOfView: listener);

                Log($"Saved listener -> new version {updated.AssetId} (was {listenerTarget}).");

                listenerMoved = false;

                if (openObjectId == listenerTarget)
                {
                    openObjectId   = updated.AssetId;
                    openLabel.Text = $"Open: {updated.AssetId}";
                }

                RefreshList();
                objectsList.SelectedItem = updated.AssetId;
                return;
            }
            catch (Exception failure)
            {
                Log($"ERROR saving listener: {failure.Message}");
                return;
            }
        }

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

    private void DeleteSelectedObject()
    {
        if (objectsList.SelectedItem is not string objectId)
        {
            MessageBox.Show(this, "Select an Object first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var referrers = aoe.ReferencedBy(objectId);
            if (referrers.Count > 0)
            {
                var list = string.Join(", ", referrers);
                var answer = MessageBox.Show(this,
                    $"{objectId} is referenced by: {list}.\n\nDelete {objectId} together with those {referrers.Count} referencing item(s)?",
                    "Delete referenced object", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) { Log($"Delete of {objectId} cancelled."); return; }

                foreach (var referrer in referrers) aoe.Delete(referrer);
                aoe.Delete(objectId);
                Log($"Deleted {objectId} and {referrers.Count} referencing item(s): {list}.");
            }
            else
            {
                aoe.Delete(objectId);
                Log($"Deleted {objectId} (was not referenced).");
            }

            RefreshList();
        }
        catch (Exception failure)
        {
            Log($"ERROR deleting {objectId}: {failure.Message}");
        }
    }

    private void ShowSelectedObjectInfo()
    {
        if (objectsList.SelectedItem is not string objectId)
        {
            MessageBox.Show(this, "Select an Object first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var info = storage.GetKeyInfo(objectId);
            Log($"{objectId} - StoredBy={info.StoredBy}, RequestedBy={info.RequestedBy}, StoredAt={info.StoredAt:yyyy-MM-dd HH:mm:ss} UTC");
        }
        catch (Exception failure)
        {
            Log($"ERROR reading info for {objectId}: {failure.Message}");
        }
    }

    private void Log(string line)
    {
        logBox.AppendText(line + Environment.NewLine);
    }
}