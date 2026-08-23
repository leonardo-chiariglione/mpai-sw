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
    // THE AIM, and no longer a row of controls.
    //
    // These were NumericUpDowns in the window. ASM is a 2D editing space
    // extended to 3D: you place an Object graphically and refine it in figures
    // afterwards, so the fields were a third way of doing what the canvas and
    // Object Edit already do between them.
    //
    // What remains is the aim itself - where the next Place will put something -
    // set by clicking empty canvas.
    private double aimX;
    private double aimY;
    private double aimZ;
    private double aimStartTime;
    private double aimEndTime = 5;

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

    // THE EDITING SPACE IS A DRAFT.
    //
    // Like an empty document: you type into it and it does not exist until you
    // save. The first Object placed goes to the origin and IS the container -
    // that placement is already the act of creating one - and each further Object
    // is placed within it. Nothing reaches the Repository until Save Changes,
    // which produces ONE Audio Object rather than a trail of intermediates.
    //
    // After a save, what is on screen is no longer a draft but the Object that
    // was saved: placing another into it and saving again produces a NEW VERSION
    // of it, which is the editing principle everywhere else here.
    //
    // This replaces an "open Object" with two buttons and a state label - a mode
    // borrowed from CAE-AOE, which does edit one Object at a time, and put in
    // front of a person who thinks "this object, at this place, goes in".
    // draftContainerId is gone. The editing space held a container and its
    // children; it holds a LIST OF PLACED OBJECTS, all equal. The Object that
    // holds them comes into existence at Save.

    private readonly List<(string ChildId, SpaceTime Placement)> draftChildren = new();

    // positionAimed was here to stop a selection overwriting a deliberate aim.
    // Selection then stopped touching the Position fields at all, so there was
    // nothing left to protect them from and the field was set and never read.
    // Removed rather than left as residue that later reads as meaningful.

    // ONE STEP BACK.
    //
    // Undo knew only about placements, so moving a child and pressing Ctrl+Z
    // removed the child rather than putting it back: it took back the last thing
    // it understood, not the last thing that happened.
    //
    // What is remembered is the last ACTION and what it changed - a placement to
    // remove, or a position to restore. One step, deliberately: a stack would
    // have to decide what a Save does to the steps before it, and one step needs
    // no such answer.
    private Action? undoLastAction;

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

        // LEFT CLICK SELECTS, RIGHT CLICK OFFERS WHAT CAN BE DONE. The list
        // shows identifiers, which say nothing: AUO000004 is not a thing anyone
        // can recognise, and you want to know what something is BEFORE placing
        // it.
        //
        // Delete lives here rather than on a button: it acts on the entry you
        // clicked, and it is not something to press by accident while reaching
        // for something else.
        objectsList.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;

            var index = objectsList.IndexFromPoint(e.Location);
            if (index == ListBox.NoMatches) return;

            var assetId = (string)objectsList.Items[index];

            var menu = new ContextMenuStrip();

            menu.Items.Add("Details...", null, (_, _) => ShowObjectDetails(assetId));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Delete", null, (_, _) => DeleteObject(assetId));

            menu.Show(objectsList, e.Location);
        };

        var bringScenesToFrontButton = new Button { Text = "Show Scenes Window", Width = 160, Height = 26 };
        bringScenesToFrontButton.Click += (_, _) => { sibling?.BringToFront(); sibling?.Activate(); };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 5,
            RowCount = 2,
            Height = 32 * 2 + 12,
            Padding = new Padding(4, 4, 4, 4)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        for (var c = 0; c < 4; c++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        for (var i = 0; i < 2; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        static Label RowLabel(string text) => new() { Text = text, TextAlign = System.Drawing.ContentAlignment.MiddleLeft, Dock = DockStyle.Fill, Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold) };
        static Button Cell(string text) => new() { Text = text, Dock = DockStyle.Fill, Margin = new Padding(2) };

        // TWO ROWS, and each is one kind of thing.
        //
        // Making: bring audio in, change what an Object is, put it in the
        // editing space.
        var acquireFileButton = Cell("From File");
        acquireFileButton.Click += (_, _) => CreateObjectFromFile();
        var acquireDeviceButton = Cell("From Device");
        acquireDeviceButton.Click += (_, _) => ToggleRecording(acquireDeviceButton);

        var objectEditButton = Cell("Object Edit");
        objectEditButton.Click += (_, _) => EditSelectedObject();

        var placeButton = Cell("Place");
        placeButton.Click += (_, _) => PlaceSelectedObject();

        grid.Controls.Add(RowLabel("Make"), 0, 0);
        grid.Controls.Add(acquireFileButton, 1, 0);
        grid.Controls.Add(acquireDeviceButton, 2, 0);
        grid.Controls.Add(objectEditButton, 3, 0);
        grid.Controls.Add(placeButton, 4, 0);

        // Keeping and hearing. SAVE DRAFT puts the composition in the
        // Repository, where it stays an Object and can be placed into another.
        // TO FILE and TO SPEAKER hand it to CAE-ASD, which delivers it as audio
        // - the arrangement rendered rather than kept.
        var repoSaveButton = Cell("Save Draft");
        repoSaveButton.Click += (_, _) => SaveObjectEdit();
        var repoClearButton = Cell("Discard Draft");
        repoClearButton.Click += (_, _) => ClearObjectEdit();

        var deliverFileButton = Cell("To File");
        deliverFileButton.Click += (_, _) => DeliverSelectedObjectToFile();
        var deliverDeviceButton = Cell("To Speaker");
        deliverDeviceButton.Click += (_, _) => PlaySelectedObject();

        grid.Controls.Add(RowLabel("Keep"), 0, 1);
        grid.Controls.Add(repoSaveButton, 1, 1);
        grid.Controls.Add(repoClearButton, 2, 1);
        grid.Controls.Add(deliverFileButton, 3, 1);
        grid.Controls.Add(deliverDeviceButton, 4, 1);

        // The Position row is gone.
        //
        // ASM is a 2D editing space extended to 3D: you place an Object
        // GRAPHICALLY, and if the position or the plan-view orientation is not
        // what you wanted, you refine it in figures. Height, the other two
        // angles and the times were only ever settable here, and they are
        // refinements - which is where they now live.
        //
        // Place uses the last click on empty canvas as its aim.

        var topPanel = new Panel { Dock = DockStyle.Top, Height = grid.Height };
        topPanel.Controls.Add(grid);

        var canvasPanel = new Panel { Dock = DockStyle.Fill };
        canvasPanel.Controls.Add(canvas);
        canvasPanel.Controls.Add(new Label
        {
            Text = "Select an Object, click where it goes, press Place. Drag to adjust; hover for orientation. Nothing is stored until Save Changes.",
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

    // Ctrl+Z takes back the last Place.
    //
    // Only within the DRAFT. A saved Object is saved - undoing one is not a thing
    // a document does either - and every edit mints a new key, so the previous
    // version is still in the Repository and selecting it is the way back.
    protected override bool ProcessCmdKey(ref System.Windows.Forms.Message message, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Z))
        {
            UndoLastPlace();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    private void UndoLastPlace()
    {
        if (undoLastAction is null)
        {
            Log("Nothing to take back.");
            return;
        }

        undoLastAction();
        undoLastAction = null;   // one step, and it has been used

        RedrawDraft();
    }

    // Put the selected Object into the editing space.
    //
    // The FIRST one goes to the origin and becomes the container: bringing an
    // Object into the editing space is already the act of creating one, so there
    // is no separate step for it. Each one after that is placed where the
    // Position fields say - typed, or filled by clicking the canvas.
    private void PlaceSelectedObject()
    {
        if (objectsList.SelectedItem is not string objectId)
        {
            MessageBox.Show(this, "Select an Object to place.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // THE FIRST PLACEMENT IS LIKE THE Nth.
        //
        // There used to be a container and its children: the first Object placed
        // became the container, sat at the origin, and could not be moved. That
        // was wrong twice over - it could not be positioned, and when such a
        // composition was later nested, the composed Object and its first
        // component occupied the same point by construction.
        //
        // There is no container in the editing space. There is a LIST OF PLACED
        // OBJECTS, all equal. The container comes into existence at Save, and
        // its own spatial attitude - the origin, unrotated - is the frame they
        // are placed in.
        var chosen = aimX != 0 || aimY != 0 || aimZ != 0;

        var placement = chosen
            ? BuildPlacementFromFields()
            : BuildPlacementFrom(draftChildren.Count * 2.0, 0, 0, aimStartTime, aimEndTime);

        if (!chosen && draftChildren.Count > 0)
        {
            Log("No position chosen - placing it clear of what is already there.");
        }

        // The Object's own listener becomes the one in the room, for the FIRST
        // thing placed: it is the space being entered. Later placements do not
        // move the ear.
        if (draftChildren.Count == 0 && !listenerMoved)
        {
            try
            {
                if (ListenerOf(aoe.Materialize(objectId)) is PointOfView stored)
                {
                    listener = stored;
                }
            }
            catch (Exception failure)
            {
                Log($"ERROR reading {objectId}: {failure.Message}");
            }
        }

        draftChildren.Add((objectId, placement));

        var placedAt = draftChildren.Count - 1;

        undoLastAction = () =>
        {
            if (placedAt < draftChildren.Count && draftChildren[placedAt].ChildId == objectId)
            {
                draftChildren.RemoveAt(placedAt);
                Log($"Took back {objectId}.");
                RedrawDraft();
            }
        };

        Log($"{objectId} placed ({draftChildren.Count} in the editing space) - " +
            "nothing is in the Repository until Save Changes.");

        RedrawDraft();
    }

    // The editing space: the container at the origin, whatever it already holds,
    // whatever has been placed since, and the listener.
    private void RedrawDraft()
    {
        canvas.Items.Clear();
        ShowListener();

        // EVERYTHING PLACED, ALL EQUAL. There is no container here: the editing
        // space is a list of Objects at positions, and the Object that holds
        // them comes into existence at Save.
        foreach (var (childId, placement) in draftChildren)
        {
            var (x, y, z) = ExtractPosition(placement);
            var (start, end) = ExtractTimeRange(placement);

            var isComposed = childId.StartsWith("AUO", StringComparison.Ordinal);

            canvas.Items.Add(new PlacementCanvas.Item
            {
                Label = childId, X = x, Y = y, Z = z, StartTime = start, EndTime = end,

                // Placed and not yet saved: every one of these moves.
                Role = PlacementCanvas.ItemRole.DraftComponent,

                HasContents = isComposed
            });

            // A composed Object shows its parts too, at offsets from where it
            // sits - the same representation at every depth. They belong to
            // something already saved, so they do not move; dragging the Object
            // itself carries them along.
            if (!isComposed) continue;

            try
            {
                foreach (var deeper in ComponentsOf(
                             aoe.Materialize(childId), x, y, z,
                             PlacementCanvas.ItemRole.SavedComponent))
                {
                    canvas.Items.Add(deeper);
                }
            }
            catch (Exception failure)
            {
                Log($"ERROR reading {childId}: {failure.Message}");
            }
        }

        canvas.RefreshDisplay();
    }

    public void RefreshList()
    {
        suppressSelectionSync = true;
        objectsList.Items.Clear();

        // BOTH kinds, NEWEST FIRST.
        //
        // An Object holding one Object is Basic and one holding more than one is
        // full, so both belong here - acquiring a file yields a BAO, and listing
        // only AUOs once showed nothing at all.
        //
        // And every edit mints a new key, so the list grows quickly and what you
        // just made was buried: sorted by prefix it sat below every BAO, sorted
        // by number it sat below every earlier AUO. The Repository stamps
        // StoredAt on every Put - the same provenance the Info button reads - so
        // the newest is simply the top.
        var everything = storage.List("BAO")
            .Concat(storage.List("AUO"))
            .Select(assetId => (AssetId: assetId, StoredAt: StoredAtOf(assetId)))
            .OrderByDescending(item => item.StoredAt)
            .ThenByDescending(item => item.AssetId, StringComparer.Ordinal);

        foreach (var (assetId, _) in everything)
        {
            objectsList.Items.Add(assetId);
        }

        suppressSelectionSync = false;
    }

    // When the Repository last stored this Asset. DateTime.MinValue for anything
    // that cannot be read, so a damaged entry sinks to the bottom rather than
    // throwing while a list is being drawn.
    private DateTime StoredAtOf(string assetId)
    {
        try
        {
            return storage.GetKeyInfo(assetId).StoredAt;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    // Orientation was three zeroes here, and nothing anywhere could set it: the
    // canvas arrow reaches yaw alone and never stored it. Object Edit does, so
    // the angles are carried.
    //
    // ROLL, PITCH, YAW - the aerospace order, read as rotations about X, Y and Z.
    private SpaceTime BuildPlacementFrom(
        double x, double y, double z,
        double startTime, double endTime,
        double roll = 0, double pitch = 0, double yaw = 0) => new()
    {
        SpatialAttitude1 = new SpatialAttitude
        {
            ObjectSpatialAttitudeID = Guid.NewGuid().ToString(),
            Position = new Position { PositionID = Guid.NewGuid().ToString(), CartPosition = new double[] { x, y, z } },
            Orientation = new Mpai.Core.Orientation { OrientationID = Guid.NewGuid().ToString(), EulerAngles = new double[] { roll, pitch, yaw } }
        },
        Time = new SimpleTime
        {
            SimpleTimeData = new() { new TimeSegment { FlagsByte = 0, StartTime = startTime, EndTime = endTime, TimeType = false, TimeUnit = "00" } }
        }
    };

    private SpaceTime BuildPlacementFromFields() =>
        BuildPlacementFrom(aimX, aimY, aimZ, aimStartTime, aimEndTime);

    private static (double X, double Y, double Z) ExtractPosition(SpaceTime? spaceTime)
    {
        var pos = spaceTime?.SpatialAttitude1?.Position?.CartPosition;
        return pos is { Length: >= 3 } ? (pos[0], pos[1], pos[2]) : (0, 0, 0);
    }

    private static (double Roll, double Pitch, double Yaw) ExtractAngles(SpaceTime? spaceTime)
    {
        var angles = spaceTime?.SpatialAttitude1?.Orientation?.EulerAngles;
        return angles is { Length: >= 3 } ? (angles[0], angles[1], angles[2]) : (0, 0, 0);
    }

    private static (double Start, double End) ExtractTimeRange(SpaceTime? spaceTime)
    {
        var segment = spaceTime?.Time?.SimpleTimeData?.FirstOrDefault();
        return (segment?.StartTime ?? 0, segment?.EndTime ?? 5);
    }

    // SELECTING DRAWS NOTHING.
    //
    // This used to show the selected Object as a locked dot at the origin, which
    // is EXACTLY how the draft's container is drawn - so there was no way to tell
    // whether you had placed something or merely looked at it. Selecting one
    // Object and then placing another made the second the container, the first
    // vanished, and Ctrl+Z left an empty canvas that had never held anything.
    //
    // Nothing appears until Place is pressed. The canvas shows the editing space
    // and only that; the list shows what exists; and right-clicking an entry will
    // show what it IS, which is the proper way to look before placing.
    //
    // The Position fields are likewise left alone: they are your AIM, not a
    // property of whatever happens to be highlighted.
    private void LoadSelectedObjectOntoCanvas()
    {
        // The editing space, whatever is selected. With no draft that is the
        // listener alone - a silent room with someone in it.
        RedrawDraft();
    }

    private void OnCanvasEmptySpaceClicked(double worldX, double worldY)
    {
        aimX = Math.Clamp(worldX, -50, 50);
        aimY = Math.Clamp(worldY, -50, 50);

        Log($"Aimed at ({aimX:0.0},{aimY:0.0}) - select an Object and press Place.");
    }

    private void OnCanvasItemMoved(PlacementCanvas.Item item)
    {
        if (item.Label == ListenerLabel)
        {
            var wasListener = listener;
            var wasMoved    = listenerMoved;

            undoLastAction = () =>
            {
                listener      = wasListener;
                listenerMoved = wasMoved;

                var (x, y, _) = PositionOf(wasListener);
                Log($"Listener put back at ({x},{y}).");
            };

            listener      = ListenerAt(item.X, item.Y, item.Z);
            listenerMoved = true;

            Log($"Listener at ({item.X},{item.Y}). Objects added from now on record it; " +
                "'Save Changes' writes it to the Object selected.");
            return;
        }

        // WHAT MOVES DEPENDS ON WHAT THE DOT IS, not on what it is called.
        //
        // The same Object can appear twice - once as itself, once as a component
        // - with the same label on both. Matching by name moved the wrong one.
        switch (item.Role)
        {
            // A component of what is being composed. Moving it changes where the
            // CONTAINER says it sits: external to the child, which is untouched.
            // Nothing reaches the Repository until Save.
            case PlacementCanvas.ItemRole.DraftComponent:
            {
                var placement = BuildPlacementFrom(item.X, item.Y, item.Z, item.StartTime, item.EndTime);
                var pending   = draftChildren.FindIndex(c => c.ChildId == item.Label);

                if (pending < 0)
                {
                    Log($"{item.Label} is not in the draft.");
                    return;
                }

                var wasAt = draftChildren[pending].Placement;
                var label = item.Label;

                draftChildren[pending] = (label, placement);

                undoLastAction = () =>
                {
                    var still = draftChildren.FindIndex(c => c.ChildId == label);
                    if (still < 0) return;

                    draftChildren[still] = (label, wasAt);

                    var (x, y, _) = ExtractPosition(wasAt);
                    Log($"{label} put back at ({x},{y}).");

                    RedrawDraft();
                };

                Log($"{item.Label} moved to ({item.X},{item.Y}) in the draft.");

                // REDRAWN FROM THE DATA, not left as the dot the mouse dropped.
                //
                // A composed Object's components are drawn at offsets from where
                // IT sits, so moving it changes where they belong - and dragging
                // one dot moves one dot. Recomputing the whole canvas is the only
                // thing that is always right, and at this size it costs nothing.
                RedrawDraft();
                return;
            }

            // A component of an Object ALREADY SAVED. It does not move: touching
            // a saved thing makes a new thing, so a stored composition is a
            // record and not a workspace. Its arrangement was decided when it
            // was made.
            case PlacementCanvas.ItemRole.SavedComponent:
            {
                Log($"{item.Label} belongs to an Object already saved: its " +
                    "arrangement cannot be changed. Place its components into a " +
                    "new Object instead.");

                RedrawDraft();
                return;
            }

            // An Object in its own right. Its Space/Time is INTERNAL to it -
            // part of what it is - so moving it mints a new version, which is
            // the iron rule. A component's placement is external to the child
            // and mints nothing.
            default:
            {

                Log($"{item.Label} moved to ({item.X},{item.Y}). Its own Space/Time is " +
                    "internal to it, so saving this makes a new version of the Object.");
                return;
            }
        }
    }

    // What an Audio Object CONTAINS, at the placements it gives them.
    //
    // Every component, including the first: a Basic Object materialises as an
    // Object of one, and that one is what you placed - the first component of an
    // Audio Object that does not exist yet. It is drawn because it IS the first
    // component, not because the Object is.
    //
    // A component with no placement of its own sits where the containing Object
    // sits, which the schema names as its default - here, the origin.
    // ALL THE WAY DOWN, and in one representation.
    //
    // A nested Object used to be a single dot while the containing Object was
    // drawn by its parts - the same kind of thing shown two ways, purely because
    // one happened to be the container. Now every Audio Object is drawn by its
    // components, at whatever depth.
    //
    // THE NESTED OBJECT KEEPS ITS OWN DOT, alongside its expanded parts. It is
    // what you take hold of: its components are placed RELATIVE to it, so moving
    // it moves them, rigidly - you can move a thing without rearranging its
    // insides.
    //
    // Positions accumulate. A component's placement is within its immediate
    // container, so where it actually sounds is that offset added to everything
    // above it. Materialize has already resolved the whole tree, so this reads
    // no storage.
    private static IEnumerable<PlacementCanvas.Item> ComponentsOf(
        AudioObject container,
        double offsetX = 0,
        double offsetY = 0,
        double offsetZ = 0,
        PlacementCanvas.ItemRole role = PlacementCanvas.ItemRole.SavedComponent)
    {
        foreach (var entry in container.BasicAudioObjects ?? new List<BasicAudioObjectEntry>())
        {
            var id = entry.BAObjectIDOrBAObject?.BasicAudioObjectID;
            if (string.IsNullOrWhiteSpace(id)) continue;

            var (x, y, z) = ExtractPosition(entry.BasicAudioObjectSpaceTime);
            var (start, end) = ExtractTimeRange(entry.BasicAudioObjectSpaceTime);

            yield return new PlacementCanvas.Item
            {
                Label = id,
                X = offsetX + x, Y = offsetY + y, Z = offsetZ + z,
                StartTime = start, EndTime = end,
                Role = role
            };
        }

        foreach (var entry in container.SubAudioObjects ?? new List<SubAudioObjectEntry>())
        {
            var nested = entry.SubAObjectIDOrSubAObject;
            var id = nested?.AudioObjectID;
            if (nested is null || string.IsNullOrWhiteSpace(id)) continue;

            var (x, y, z) = ExtractPosition(entry.SubAudioObjectSpaceTime);
            var (start, end) = ExtractTimeRange(entry.SubAudioObjectSpaceTime);

            // The nested Object itself - the handle by which it moves.
            yield return new PlacementCanvas.Item
            {
                Label = id,
                X = offsetX + x, Y = offsetY + y, Z = offsetZ + z,
                StartTime = start, EndTime = end,
                Role = role,
                HasContents = true
            };

            // And what it holds, at offsets from where it sits. These belong to
            // an Object already saved, so they do not move: you can move the
            // Object, not rearrange it.
            foreach (var deeper in ComponentsOf(
                         nested,
                         offsetX + x, offsetY + y, offsetZ + z,
                         PlacementCanvas.ItemRole.SavedComponent))
            {
                yield return deeper;
            }
        }
    }

    // Draw the listener where it currently stands, replacing any previous one.
    private void ShowListener()
    {
        canvas.Items.RemoveAll(i => i.Label == ListenerLabel);

        var (x, y, z) = PositionOf(listener);

        canvas.Items.Add(new PlacementCanvas.Item
        {
            Label = ListenerLabel, X = x, Y = y, Z = z, Role = PlacementCanvas.ItemRole.Listener
        });
    }

    private void OnCanvasLockedItemClicked(PlacementCanvas.Item item)
    {
        Log($"{item.Label} is the first component, fixed at the origin: it is the " +
            "point everything else - including the Listener - is placed against.");
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
        using var dialog = new PlacementDetailsDialog(
            item.Label, item.Z, item.StartTime, item.EndTime, item.Pitch, item.Roll);

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        item.Z = dialog.Z;
        item.StartTime = dialog.StartTime;
        item.EndTime = dialog.EndTime;
        item.Pitch = dialog.Pitch;
        item.Roll = dialog.Roll;

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

    // WHAT AN OBJECT IS - internal characteristics, and the only place they
    // are edited. Changing any of them mints a new version.
    private void EditSelectedObject()
    {
        if (objectsList.SelectedItem is not string assetId)
        {
            MessageBox.Show(this, "Select an Object to edit.", "Nothing selected",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        AudioObject materialized;

        try
        {
            materialized = aoe.Materialize(assetId);
        }
        catch (Exception failure)
        {
            MessageBox.Show(this, $"Could not read {assetId}: {failure.Message}",
                "Unreadable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var firstBasic = materialized.BasicAudioObjects?.FirstOrDefault()?.BAObjectIDOrBAObject;

        var profile = materialized.AudioObjectProperties
                      ?? firstBasic?.BasicAudioObjectProperties?.AcousticProfile;

        // Its components, with where the Object says each of them sits. Only the
        // immediate ones: a component that is itself composed is refined by
        // editing THAT Object, which keeps every dialog to one level.
        var components = ComponentsOfDirectly(materialized).ToList();

        using var dialog = new ObjectEditDialog(
            assetId,
            DescriptionOf(materialized, firstBasic),
            profile,
            components);

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            // ONE EDIT, ONE VERSION.
            //
            // This called EditObjectProperties, then EditObjectDescription, then
            // Rearrange - three keys for one press of OK, which is the very
            // fault Rearrange was added to avoid, committed one level up.
            var updated = assetId.StartsWith("BAO", StringComparison.Ordinal)
                ? aoe.EditBasicObject(
                      assetId,
                      dialog.EditedAcousticProfile,
                      dialog.EditedDescrMetadata)
                : aoe.EditObject(
                      assetId,
                      dialog.EditedAcousticProfile,
                      dialog.EditedDescrMetadata,
                      dialog.EditedPlacements.Count == 0
                          ? null
                          : dialog.EditedPlacements.ToDictionary(
                                p => p.Id,
                                p => (SpaceTime?)BuildPlacementFrom(
                                         p.X, p.Y, p.Z, p.StartTime, p.EndTime,
                                         p.Roll, p.Pitch, p.Yaw)));

            Log($"Edited {assetId} -> {updated.AssetId}");

            RefreshList();
            suppressSelectionSync = true;
            objectsList.SelectedItem = updated.AssetId;
            suppressSelectionSync = false;
        }
        catch (Exception failure)
        {
            Log($"ERROR editing {assetId}: {failure.Message}");
        }
    }

    // THE EDITING SPACE, seen as a whole - the Object being made, which could
    // not be examined at all while every saved one could.
    private void ShowDraftInfo()
    {
        var placed = draftChildren
            .Select(c =>
            {
                var (x, y, z) = ExtractPosition(c.Placement);
                return (c.ChildId, x, y, z);
            })
            .ToList();

        using var dialog = new DraftInfoDialog(placed, PositionOf(listener));
        dialog.ShowDialog(this);
    }

    private void SaveObjectEdit()
    {
        // THE DRAFT COMES FIRST, and takes the listener with it.
        //
        // The listener check used to come first and RETURN, so with a listener
        // moved a composition could never be saved: Save minted a new version of
        // whatever was selected, the draft was never composed, and the redraw
        // that followed left the editing space EMPTY. The work was not merely
        // unrecorded, it was discarded.
        //
        // Compose takes a listener as well, so there is nothing to choose
        // between: a save does both.
        if (draftChildren.Count > 0)
        {
            SaveDraft();
            return;
        }

        // A moved listener with NOTHING PLACED is an edit in its own right: one
        // field on the Object selected.
        if (listenerMoved && objectsList.SelectedItem is string listenerTarget)
        {
            try
            {
                var updated = listenerTarget.StartsWith("BAO", StringComparison.Ordinal)
                    ? aoe.EditBasicObjectProperties(listenerTarget, listenerPointOfView: listener)
                    : aoe.EditObjectProperties(listenerTarget, listenerPointOfView: listener);

                Log($"Saved listener -> new version {updated.AssetId} (was {listenerTarget}).");

                listenerMoved = false;

                RefreshList();
                suppressSelectionSync = true;
                objectsList.SelectedItem = updated.AssetId;
                suppressSelectionSync = false;

                RedrawDraft();
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

    // One Object out, however many were placed - and the listener with it.
    private void SaveDraft()
    {
        try
        {
            var placed = draftChildren
                .Select(c => (c.ChildId, (SpaceTime?)c.Placement))
                .ToList();

            var composed = aoe.Compose(placed, listenerMoved ? listener : null);

            // An Object holding ONE Object is Basic, and saving a draft of one
            // is how you clone something to a different place: a new identity,
            // because it is a new thing.
            Log($"Saved -> {composed.AssetId}, holding {placed.Count} Object(s)" +
                (listenerMoved ? ", with the listener where you left it." : "."));

            draftChildren.Clear();
            listenerMoved = false;

            // Saved: the step before referred to a draft that no longer exists.
            // A saved Object is undone by selecting its previous version, which
            // is still in the Repository.
            undoLastAction = null;

            RefreshList();
            suppressSelectionSync = true;
            objectsList.SelectedItem = composed.AssetId;
            suppressSelectionSync = false;

            RedrawDraft();
        }
        catch (Exception failure)
        {
            // The draft is NOT cleared: work that failed to save is work you
            // still have.
            Log($"ERROR saving: {failure.Message}");
        }
    }

    private void ClearObjectEdit()
    {
        if (draftChildren.Count > 0)
        {
            Log($"Discarded the editing space - {draftChildren.Count} placement(s) were never saved.");

            draftChildren.Clear();
            undoLastAction = null;
            RedrawDraft();
            return;
        }

        var hadTarget = objectDraftTargetId;
        objectDraft.Clear();
        objectDraftTargetId = null;
        Log(hadTarget is null ? "Nothing was staged." : $"Discarded staged edit for {hadTarget} - it was never saved.");
    }

    // Deletes the Object named, rather than whatever happens to be selected:
    // it is reached from the right-click menu, which knows which entry was
    // clicked.
    private void DeleteObject(string objectId)
    {
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


    // What an Object is, and the one thing about it a person can write.
    private void ShowObjectDetails(string assetId)
    {
        AudioObject materialized;

        try
        {
            materialized = aoe.Materialize(assetId);
        }
        catch (Exception failure)
        {
            MessageBox.Show(this, $"Could not read {assetId}: {failure.Message}",
                "Unreadable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DateTime? storedAt = null;
        try { storedAt = storage.GetKeyInfo(assetId).StoredAt; } catch { }

        var basicCount = materialized.BasicAudioObjectCount ?? 0;
        var subCount   = materialized.SubAudioObjectCount ?? 0;
        var total      = basicCount + subCount;

        // An Object holding one Object is Basic; one holding more than one is
        // full. The kind is a fact about the content.
        var kind = total > 1
            ? $"Audio Object, {total} components"
            : "Basic Audio Object";

        var components = ComponentsOf(materialized)
            .Select(item => (item.Label, item.X, item.Y, item.Z))
            .ToList();

        // Only meaningful for something composed: a Basic Object materialises as
        // an Object of one, and that one is itself rather than a component.
        if (total <= 1) components.Clear();

        var firstBasic = materialized.BasicAudioObjects?.FirstOrDefault()?.BAObjectIDOrBAObject;

        var listenerPoint = ListenerOf(materialized) is PointOfView stored
            ? PositionOf(stored)
            : ((double, double, double)?)null;

        var (format, duration) = DescribeAudio(firstBasic);

        using var dialog = new ObjectDetailsDialog(
            assetId,
            DescriptionOf(materialized, firstBasic),
            storedAt,
            kind,
            SizeOf(firstBasic),
            listenerPoint,
            components,
            format,
            duration);

        dialog.ShowDialog(this);
    }

    // The IMMEDIATE components of an Object, with everything about where each
    // one sits. ComponentsOf descends the whole tree for the canvas; this stops
    // at one level, because that is what an Object's own placements are.
    private static IEnumerable<ObjectEditDialog.ComponentPlacement> ComponentsOfDirectly(AudioObject container)
    {
        foreach (var entry in container.BasicAudioObjects ?? new List<BasicAudioObjectEntry>())
        {
            var id = entry.BAObjectIDOrBAObject?.BasicAudioObjectID;
            if (string.IsNullOrWhiteSpace(id)) continue;

            yield return Placement(id, entry.BasicAudioObjectSpaceTime);
        }

        foreach (var entry in container.SubAudioObjects ?? new List<SubAudioObjectEntry>())
        {
            var id = entry.SubAObjectIDOrSubAObject?.AudioObjectID;
            if (string.IsNullOrWhiteSpace(id)) continue;

            yield return Placement(id, entry.SubAudioObjectSpaceTime);
        }
    }

    private static ObjectEditDialog.ComponentPlacement Placement(string id, SpaceTime? spaceTime)
    {
        var (x, y, z) = ExtractPosition(spaceTime);
        var (roll, pitch, yaw) = ExtractAngles(spaceTime);
        var (start, end) = ExtractTimeRange(spaceTime);

        return new ObjectEditDialog.ComponentPlacement
        {
            Id = id, X = x, Y = y, Z = z,
            Roll = roll, Pitch = pitch, Yaw = yaw,
            StartTime = start, EndTime = end
        };
    }

    private static string? DescriptionOf(AudioObject materialized, BasicAudioObject? firstBasic) =>
        materialized.DescrMetadata ?? firstBasic?.DescrMetadata;

    private static long? SizeOf(BasicAudioObject? basic) =>
        basic is null ? null : basic.Data.Length;

    // Format and duration, from the Qualifier acquisition now fills. Both say
    // "not recorded" rather than being omitted when they are absent: a Qualifier
    // nothing filled is worth seeing.
    private static (string? Format, double? Seconds) DescribeAudio(BasicAudioObject? basic)
    {
        var pcm = basic?.AudioQualifier?.Formats?.ContentFormat?.RawData?.SampleSpace;
        if (pcm is null) return (null, null);

        var file     = basic?.AudioQualifier?.Formats?.TransportFormat?.FileFormats;
        var channels = basic?.AudioQualifier?.Attributes?.Device?.CaptureConfiguration?.ChannelCount;

        var parts = new List<string>();
        if (file is not null) parts.Add(file);
        if (pcm.SamplingFrequency is double hz) parts.Add($"{hz:N0} Hz");
        if (channels is int count) parts.Add(count == 1 ? "mono" : $"{count} channels");
        if (pcm.Precision is int bits) parts.Add($"{bits} bit");

        var format = parts.Count > 0 ? string.Join(", ", parts) : null;

        // Duration from the stored bytes and the format: a WAV's data is its
        // length less the header, over the bytes per second.
        double? seconds = null;

        if (basic is not null &&
            pcm.SamplingFrequency is double rate && rate > 0 &&
            pcm.Precision is int depth && depth > 0)
        {
            var perSecond = rate * (channels ?? 1) * (depth / 8.0);
            var payload   = Math.Max(0, basic.Data.Length - 44);   // the canonical WAV header

            if (perSecond > 0) seconds = payload / perSecond;
        }

        return (format, seconds);
    }

    // The name and description a person wrote, both in DescrMetadata. Editing
    // them changes what the Object IS, so it mints a new version - the iron rule.
    // SaveDescription is gone: a name and a description are what an Object IS,
    // so they are edited in Object Edit with the rest of what it is, and the
    // details view only shows them.


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