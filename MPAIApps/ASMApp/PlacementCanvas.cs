using System;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MPAIApps.ASMApp;

// ---------------------------------------------------------------------------
//  A 2D placement canvas: shows a set of positionable items on an X/Y plane.
//  Drag an item to reposition it; right-click an item to edit its Z and
//  time range (kept out of the always-visible UI - "adding Z makes the GUI
//  much more complex... right click and the Z cursor, time cursor appear").
//  Clicking empty space reports that point back out, for "click where you
//  want the next object, then Add" workflows.
//
//  One reusable component, per the earlier design discussion: the Scenes
//  window uses it with however many placements are in the current draft;
//  the Objects window can use it with a list of exactly one.
//
//  NOTE: this file could not be verified in the sandbox it was developed
//  in - there is no display available to actually render or interact with
//  a WinForms control. Written carefully against standard GDI+/WinForms
//  APIs, with the coordinate math itself (PlacementCanvasMath) kept
//  separate and genuinely unit-tested, but the real check for THIS file
//  has to happen on your machine.
// ---------------------------------------------------------------------------
public sealed class PlacementCanvas : Panel
{
    public sealed class Item
    {
        public required string Label { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; } = 5;

        // Which way it faces, in degrees, 0 = up the screen and increasing
        // clockwise. A plan view can only render rotation about the vertical
        // axis, so this is YAW; pitch and roll have no room here and are left
        // unset until something needs them.
        public double Yaw { get; set; }

        // The two angles a plan view cannot show. Yaw is the arrow; these are
        // set in the details dialog, which is where everything the canvas cannot
        // reach now lives.
        public double Pitch { get; set; }
        public double Roll { get; set; }

        // Shown, hovered and rotated - but not dragged.
        //
        // A lone Object IS the origin: it is the thing being auditioned, and
        // there is nothing to move it relative to. What moves is the EAR. So the
        // Objects window locks the Object and lets the listener travel, which is
        // the opposite of what happens in a Scene.
        public bool Locked { get; set; }
    }

    public List<Item> Items { get; } = new();

    // Fired after a drag finishes, with the item's new X/Y already applied.
    public event Action<Item>? ItemMoved;

    // Fired on right-click of an item - the hosting form is expected to
    // show a small dialog (PlacementDetailsDialog) to edit Z/time, then
    // call RefreshDisplay() afterwards.
    public event Action<Item>? ItemRightClicked;

    // Fired on left-click of EMPTY space (no item hit) - reports the world
    // position clicked, for "click where you want it, then Add" workflows.
    public event Action<double, double>? EmptySpaceClicked;

    // Fired after a rotation finishes, with the item's new Yaw already applied.
    public event Action<Item>? ItemRotated;

    // Fired when an item that cannot be dragged is clicked, so the window can
    // explain why rather than let the click seem to do nothing.
    public event Action<Item>? LockedItemClicked;

    private double metresPerPixel = 0.2;   // ~40m visible across a 400px-wide canvas
    private int? draggingIndex;

    // Orientation is shown only for the item under the pointer. Showing every
    // arrow at once turns a scene of eight objects into a hedgehog; showing one
    // answers the question actually being asked - "which way is THAT facing?"
    private int? hoverIndex;
    private int? rotatingIndex;

    private const int ArrowLength = 34;   // pixels from the centre
    private const int GrabRadius  = 9;    // how near the head counts as grabbing it

    public PlacementCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
    }

    public void RefreshDisplay() => Invalidate();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        DrawGrid(g);
        DrawItems(g);
    }

    private void DrawGrid(Graphics g)
    {
        using var axisPen = new Pen(Color.Gainsboro, 1);
        using var originPen = new Pen(Color.DarkGray, 1.5f);

        for (double worldX = -50; worldX <= 50; worldX += 5)
        {
            var top = PlacementCanvasMath.WorldToScreen(worldX, 50, Size, metresPerPixel);
            var bottom = PlacementCanvasMath.WorldToScreen(worldX, -50, Size, metresPerPixel);
            g.DrawLine(worldX == 0 ? originPen : axisPen, top, bottom);
        }

        for (double worldY = -50; worldY <= 50; worldY += 5)
        {
            var left = PlacementCanvasMath.WorldToScreen(-50, worldY, Size, metresPerPixel);
            var right = PlacementCanvasMath.WorldToScreen(50, worldY, Size, metresPerPixel);
            g.DrawLine(worldY == 0 ? originPen : axisPen, left, right);
        }

        // The listener at the origin - drawn ONLY when nobody else is drawing one.
        //
        // This marker assumed the listener was always at the world origin, which
        // is true in the Objects window: an object's placement there is where it
        // sits RELATIVE to the listener, so the listener is the origin by
        // definition. It is NOT true in the Scenes window, where the listener has
        // a Point of View of its own that the user can move, and which
        // AseAim.SetSceneListener persists.
        //
        // With both drawn, moving the listener produced two of them - the real
        // one following the mouse and this one staying behind. The data was never
        // wrong; the picture contradicted itself.
        //
        // So: if the form has supplied a listener among the Items, that one is
        // the listener, and this marker stands aside.
        var suppliedByForm = Items.Any(i =>
            string.Equals(i.Label, "Listener", StringComparison.OrdinalIgnoreCase));

        if (suppliedByForm) return;

        using var listenerBrush = new SolidBrush(Color.Black);
        var originScreen = PlacementCanvasMath.WorldToScreen(0, 0, Size, metresPerPixel);
        g.FillRectangle(listenerBrush, originScreen.X - 5, originScreen.Y - 5, 10, 10);
        using var listenerFont = new Font("Segoe UI", 8, FontStyle.Bold);
        g.DrawString("Listener", listenerFont, listenerBrush, originScreen.X + 8, originScreen.Y - 6);
    }

    private void DrawItems(Graphics g)
    {
        using var itemBrush = new SolidBrush(Color.SteelBlue);
        using var textBrush = new SolidBrush(Color.Black);
        using var font = new Font("Segoe UI", 8);

        for (var index = 0; index < Items.Count; index++)
        {
            var item   = Items[index];
            var screen = PlacementCanvasMath.WorldToScreen(item.X, item.Y, Size, metresPerPixel);

            // The arrow first, so the dot sits on top of its tail.
            if (index == hoverIndex || index == rotatingIndex)
            {
                DrawOrientation(g, item, screen, index == rotatingIndex);
            }

            g.FillEllipse(itemBrush, screen.X - 6, screen.Y - 6, 12, 12);
            g.DrawString(item.Label, font, textBrush, screen.X + 8, screen.Y - 6);
        }
    }

    // An ARROW, not a bar. A bar through the object shows an axis and is
    // symmetrical - it cannot tell facing north from facing south. A voice or a
    // loudspeaker has a direction, not merely an axis.
    private void DrawOrientation(Graphics g, Item item, PointF centre, bool active)
    {
        var head = HeadOf(centre, item.Yaw);

        using var pen = new Pen(active ? Color.OrangeRed : Color.SteelBlue, active ? 2.2f : 1.6f);
        g.DrawLine(pen, centre, head);

        // The head, as a small triangle pointing the way it faces.
        var radians = (item.Yaw - 90) * Math.PI / 180.0;
        var left    = radians + 2.5;
        var right   = radians - 2.5;

        g.FillPolygon(new SolidBrush(pen.Color), new[]
        {
            head,
            new PointF(head.X + (float)(Math.Cos(left)  * 9), head.Y + (float)(Math.Sin(left)  * 9)),
            new PointF(head.X + (float)(Math.Cos(right) * 9), head.Y + (float)(Math.Sin(right) * 9))
        });

        // While rotating, the angle in figures - so it can be set precisely and
        // not merely approximately.
        if (!active) return;

        using var font  = new Font("Segoe UI", 8, FontStyle.Bold);
        using var brush = new SolidBrush(Color.OrangeRed);
        g.DrawString($"{Math.Round(item.Yaw)}\u00b0", font, brush, head.X + 10, head.Y - 6);
    }

    // 0 degrees is up the screen, increasing clockwise - which is how a compass
    // reads, and how anyone describes a heading out loud.
    private static PointF HeadOf(PointF centre, double yaw)
    {
        var radians = (yaw - 90) * Math.PI / 180.0;

        return new PointF(
            centre.X + (float)(Math.Cos(radians) * ArrowLength),
            centre.Y + (float)(Math.Sin(radians) * ArrowLength));
    }

    private int? ArrowHeadAt(Point location)
    {
        var index = hoverIndex ?? rotatingIndex;
        if (index is not int shown || shown >= Items.Count) return null;

        var item   = Items[shown];
        var centre = PlacementCanvasMath.WorldToScreen(item.X, item.Y, Size, metresPerPixel);
        var head   = HeadOf(centre, item.Yaw);

        var dx = location.X - head.X;
        var dy = location.Y - head.Y;

        return (dx * dx + dy * dy) <= GrabRadius * GrabRadius ? shown : null;
    }

    // The nearest item that can actually be dragged, ignoring locked ones.
    private int? NearestDraggable(Point location)
    {
        int? best = null;
        var bestDistance = double.MaxValue;

        for (var index = 0; index < Items.Count; index++)
        {
            if (Items[index].Locked) continue;

            var screen = PlacementCanvasMath.WorldToScreen(
                Items[index].X, Items[index].Y, Size, metresPerPixel);

            var dx = screen.X - location.X;
            var dy = screen.Y - location.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance <= 12 && distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }

        return best;
    }

    // Is the pointer on the arrow of the item currently shown - anywhere along
    // it, not only at the head? This is what keeps the arrow visible while the
    // pointer travels out to grab it.
    private int? OnTheArrow(Point location)
    {
        var index = hoverIndex ?? rotatingIndex;
        if (index is not int shown || shown >= Items.Count) return null;

        var item   = Items[shown];
        var centre = PlacementCanvasMath.WorldToScreen(item.X, item.Y, Size, metresPerPixel);
        var head   = HeadOf(centre, item.Yaw);

        return DistanceToSegment(location, centre, head) <= GrabRadius ? shown : null;
    }

    // Perpendicular distance from a point to a line segment, clamped to the
    // segment's ends so that a point beyond the head measures from the head.
    private static double DistanceToSegment(PointF point, PointF from, PointF to)
    {
        double vx = to.X - from.X, vy = to.Y - from.Y;
        double wx = point.X - from.X, wy = point.Y - from.Y;

        var lengthSquared = vx * vx + vy * vy;
        if (lengthSquared <= double.Epsilon) return Math.Sqrt(wx * wx + wy * wy);

        var t = Math.Clamp((wx * vx + wy * vy) / lengthSquared, 0, 1);

        var dx = wx - t * vx;
        var dy = wy - t * vy;

        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void OnMouseDown(object? sender, MouseEventArgs e)
    {
        var hit = PlacementCanvasMath.HitTest(Items, i => (i.X, i.Y), e.Location, Size, metresPerPixel);

        if (e.Button == MouseButtons.Right)
        {
            if (hit is int rightIndex)
            {
                ItemRightClicked?.Invoke(Items[rightIndex]);
            }
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        // The arrow head is tested BEFORE the dot, because it is only visible
        // when the pointer is already near its item and would otherwise be
        // unreachable - the dot would take every click.
        if (ArrowHeadAt(e.Location) is int rotateIndex)
        {
            rotatingIndex = rotateIndex;
            hoverIndex    = rotateIndex;
            Invalidate();
            return;
        }

        // A DRAGGABLE ITEM WINS OVER A LOCKED ONE at the same point.
        //
        // HitTest returns the nearest item, and when a child sits exactly on its
        // container the container may be nearer by a fraction. The locked one
        // then took the click and refused it, so a child on top of its container
        // could never be pulled off. What can move is preferred to what cannot.
        var draggable = NearestDraggable(e.Location);

        if (draggable is int draggableIndex)
        {
            draggingIndex = draggableIndex;
        }
        else if (hit is int locked && Items[locked].Locked)
        {
            // Say so, rather than appearing broken.
            LockedItemClicked?.Invoke(Items[locked]);
        }
        else
        {
            var (worldX, worldY) = PlacementCanvasMath.ScreenToWorld(e.Location, Size, metresPerPixel);
            EmptySpaceClicked?.Invoke(Math.Round(worldX, 1), Math.Round(worldY, 1));
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (rotatingIndex is int rotating)
        {
            var item   = Items[rotating];
            var centre = PlacementCanvasMath.WorldToScreen(item.X, item.Y, Size, metresPerPixel);

            // Where the pointer is, relative to the object, IS the heading.
            var degrees = Math.Atan2(e.Location.Y - centre.Y, e.Location.X - centre.X) * 180.0 / Math.PI + 90;
            if (degrees < 0) degrees += 360;

            item.Yaw = Math.Round(degrees);
            Invalidate();
            return;
        }

        if (draggingIndex is int index)
        {
            var (worldX, worldY) = PlacementCanvasMath.ScreenToWorld(e.Location, Size, metresPerPixel);
            Items[index].X = Math.Round(worldX, 1);
            Items[index].Y = Math.Round(worldY, 1);
            Invalidate();
            return;
        }

        // Not dragging: track what the pointer is over, and redraw only when the
        // answer changes - a repaint on every mouse move would flicker.
        //
        // THE ARROW HOLDS THE HOVER. Moving from the dot out towards the head
        // leaves the dot's hit radius before reaching the head, and the first
        // version cleared hoverIndex at that moment: the arrow vanished on the
        // way to it and the head could never be grabbed. So the arrow of the
        // item already hovered counts as part of that item, along its whole
        // length rather than at its tip.
        var over = OnTheArrow(e.Location) is int held
            ? held
            : PlacementCanvasMath.HitTest(Items, i => (i.X, i.Y), e.Location, Size, metresPerPixel);

        if (over != hoverIndex)
        {
            hoverIndex = over;
            Invalidate();
        }

        Cursor = ArrowHeadAt(e.Location) is not null ? Cursors.Hand : Cursors.Default;
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (draggingIndex is int index)
        {
            ItemMoved?.Invoke(Items[index]);
        }

        if (rotatingIndex is int rotated)
        {
            ItemRotated?.Invoke(Items[rotated]);
        }

        draggingIndex = null;
        rotatingIndex = null;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (hoverIndex is null) return;

        hoverIndex = null;
        Invalidate();
    }
}