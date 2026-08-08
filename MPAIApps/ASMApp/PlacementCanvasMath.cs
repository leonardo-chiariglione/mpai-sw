using System;
using System.Collections.Generic;
using System.Drawing;

namespace MPAIApps.ASMApp;

// Pure coordinate math for the 2D placement canvas - deliberately separate
// from any WinForms/GDI+ painting or event code, so it can be tested
// without a Windows display at all (unlike the canvas widget itself, which
// cannot be verified in a sandbox with no display). World coordinates are
// metres, origin at the canvas centre, X right, Y UP (screen Y is
// flipped - "up" in the world is up on screen, not down, matching how a
// person would expect a top-down stage plan to read).
public static class PlacementCanvasMath
{
    public static PointF WorldToScreen(double worldX, double worldY, Size canvasSize, double metresPerPixel)
    {
        var px = canvasSize.Width / 2.0 + worldX / metresPerPixel;
        var py = canvasSize.Height / 2.0 - worldY / metresPerPixel;
        return new PointF((float)px, (float)py);
    }

    public static (double X, double Y) ScreenToWorld(PointF screenPoint, Size canvasSize, double metresPerPixel)
    {
        var worldX = (screenPoint.X - canvasSize.Width / 2.0) * metresPerPixel;
        var worldY = (canvasSize.Height / 2.0 - screenPoint.Y) * metresPerPixel;
        return (worldX, worldY);
    }

    // Finds the closest item within hitRadiusPixels of screenPoint, or null
    // if none qualify - the closest one wins if several are in range.
    // Returns an INDEX into items rather than the item itself, so the
    // caller can mutate the actual list entry (items are mutable position
    // holders, not immutable records, since dragging updates them in place).
    public static int? HitTest<T>(
        IReadOnlyList<T> items,
        Func<T, (double X, double Y)> getWorldPosition,
        PointF screenPoint,
        Size canvasSize,
        double metresPerPixel,
        float hitRadiusPixels = 12f)
    {
        int? best = null;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < items.Count; i++)
        {
            var (worldX, worldY) = getWorldPosition(items[i]);
            var screenPos = WorldToScreen(worldX, worldY, canvasSize, metresPerPixel);
            var dx = screenPos.X - screenPoint.X;
            var dy = screenPos.Y - screenPoint.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance <= hitRadiusPixels && distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }
}