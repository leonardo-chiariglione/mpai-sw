using System;
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

    private double metresPerPixel = 0.2;   // ~40m visible across a 400px-wide canvas
    private int? draggingIndex;

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

        // Listener marker, always at the world origin.
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

        foreach (var item in Items)
        {
            var screen = PlacementCanvasMath.WorldToScreen(item.X, item.Y, Size, metresPerPixel);
            g.FillEllipse(itemBrush, screen.X - 6, screen.Y - 6, 12, 12);
            g.DrawString(item.Label, font, textBrush, screen.X + 8, screen.Y - 6);
        }
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

        if (hit is int leftIndex)
        {
            draggingIndex = leftIndex;
        }
        else
        {
            var (worldX, worldY) = PlacementCanvasMath.ScreenToWorld(e.Location, Size, metresPerPixel);
            EmptySpaceClicked?.Invoke(Math.Round(worldX, 1), Math.Round(worldY, 1));
        }
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (draggingIndex is not int index) return;

        var (worldX, worldY) = PlacementCanvasMath.ScreenToWorld(e.Location, Size, metresPerPixel);
        Items[index].X = Math.Round(worldX, 1);
        Items[index].Y = Math.Round(worldY, 1);
        Invalidate();
    }

    private void OnMouseUp(object? sender, MouseEventArgs e)
    {
        if (draggingIndex is int index)
        {
            ItemMoved?.Invoke(Items[index]);
        }

        draggingIndex = null;
    }
}