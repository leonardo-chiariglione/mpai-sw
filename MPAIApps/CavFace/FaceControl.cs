using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using Mpai.Core.OSD;

namespace CavFace;

// A 2D face, drawn programmatically, whose features are deformed by FACS Action
// Units - the visible delivery of the CAV. Set ActionUnits (from the generative
// face description / EM-FACS) to change the expression; set MouthOpen (0..1, from
// lip-sync) to animate speaking. Each Action Unit moves the anatomically-correct
// feature, so the expression is FACS-principled rather than a canned preset.
public sealed class FaceControl : Control
{
    private FaceActionUnits _aus = FaceActionUnits.Of(new Dictionary<ActionUnit, double>());
    private double _mouthOpen;

    public void SetExpression(FaceActionUnits aus) { _aus = aus; InvalidateVisual(); }
    public void SetMouthOpen(double open) { _mouthOpen = Math.Clamp(open, 0, 1); InvalidateVisual(); }

    private double AU(ActionUnit au) => _aus.Weight(au);

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        double cx = w / 2, cy = h / 2;
        double s = Math.Min(w, h) / 2.6;   // face scale

        // --- Head ---
        var skin = new SolidColorBrush(Color.FromRgb(0xF2, 0xD3, 0xB6));
        var outline = new Pen(new SolidColorBrush(Color.FromRgb(0x6B, 0x4E, 0x37)), 3);
        ctx.DrawEllipse(skin, outline, new Point(cx, cy), s, s * 1.15);

        // Feature geometry, resting positions relative to centre.
        double eyeY   = cy - s * 0.15;
        double eyeDX  = s * 0.42;
        double eyeR   = s * 0.16;
        double browY0 = eyeY - eyeR * 1.7;
        double mouthY = cy + s * 0.5;

        // --- Action Unit activations (each in 0..1) ---
        double au1 = AU(ActionUnit.AU1_InnerBrowRaise);
        double au2 = AU(ActionUnit.AU2_OuterBrowRaise);
        double au4 = AU(ActionUnit.AU4_BrowLower);
        double au5 = AU(ActionUnit.AU5_UpperLidRaise);
        double au6 = AU(ActionUnit.AU6_CheekRaise);
        double au7 = AU(ActionUnit.AU7_LidTighten);
        double au9 = AU(ActionUnit.AU9_NoseWrinkle);
        double au12 = AU(ActionUnit.AU12_LipCornerPull);
        double au15 = AU(ActionUnit.AU15_LipCornerDepress);
        double au17 = AU(ActionUnit.AU17_ChinRaise);
        double au20 = AU(ActionUnit.AU20_LipStretch);
        double au23 = AU(ActionUnit.AU23_LipTighten);
        double au25 = AU(ActionUnit.AU25_LipsPart);
        double au26 = AU(ActionUnit.AU26_JawDrop);

        var eyeBrush   = new SolidColorBrush(Colors.White);
        var eyePen     = new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x2C, 0x24)), 2);
        var pupilBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x2C, 0x24));
        var browPen    = new Pen(new SolidColorBrush(Color.FromRgb(0x5B, 0x3E, 0x2A)), s * 0.06) { LineCap = PenLineCap.Round };
        var mouthPen   = new Pen(new SolidColorBrush(Color.FromRgb(0x8B, 0x3A, 0x3A)), s * 0.07) { LineCap = PenLineCap.Round };
        var mouthFill  = new SolidColorBrush(Color.FromRgb(0x5A, 0x22, 0x22));

        // --- Eyes (left, right). AU5 opens, AU6/AU7 narrow. ---
        double lidOpen = 1.0 + 0.5 * au5 - 0.45 * au6 - 0.5 * au7;
        lidOpen = Math.Clamp(lidOpen, 0.25, 1.6);
        foreach (int sign in new[] { -1, 1 })
        {
            double ex = cx + sign * eyeDX;
            double ry = eyeR * lidOpen;
            ctx.DrawEllipse(eyeBrush, eyePen, new Point(ex, eyeY), eyeR, ry);
            // pupil looks forward
            ctx.DrawEllipse(pupilBrush, null, new Point(ex, eyeY), eyeR * 0.42, eyeR * 0.42);
        }

        // --- Brows. AU1 raises inner, AU2 raises outer, AU4 lowers + draws together. ---
        foreach (int sign in new[] { -1, 1 })
        {
            double innerX = cx + sign * (eyeDX - eyeR * 1.1);
            double outerX = cx + sign * (eyeDX + eyeR * 1.1);
            double innerY = browY0 - au1 * s * 0.16 + au4 * s * 0.14;
            double outerY = browY0 - au2 * s * 0.16 + au4 * s * 0.10;
            // AU4 also pulls inner ends toward centre (furrow)
            innerX -= sign * au4 * s * 0.05;
            var brow = new StreamGeometry();
            using (var g = brow.Open())
            {
                g.BeginFigure(new Point(innerX, innerY), false);
                g.CubicBezierTo(
                    new Point((innerX + outerX) / 2, Math.Min(innerY, outerY) - s * 0.03),
                    new Point((innerX + outerX) / 2, Math.Min(innerY, outerY) - s * 0.03),
                    new Point(outerX, outerY));
            }
            ctx.DrawGeometry(null, browPen, brow);
        }

        // --- Nose (small); AU9 wrinkles it up slightly. ---
        double noseTop = eyeY + eyeR * 1.2 - au9 * s * 0.05;
        double noseBot = cy + s * 0.18;
        ctx.DrawLine(new Pen(outline.Brush, 2), new Point(cx, noseTop), new Point(cx - s * 0.06, noseBot));
        ctx.DrawLine(new Pen(outline.Brush, 2), new Point(cx - s * 0.06, noseBot), new Point(cx + s * 0.05, noseBot));

        // --- Mouth. Corners: AU12 up, AU15 down. Width: AU20 wider, AU23 thinner.
        //     Opening: AU25/AU26 + lip-sync MouthOpen. ---
        double corner = (au12 - au15) * s * 0.28;          // + up, - down
        double half   = s * (0.30 + au20 * 0.10 - au23 * 0.06);
        double open   = Math.Clamp(au25 * 0.5 + au26 * 0.8 + _mouthOpen, 0, 1) * s * 0.30;
        double my     = mouthY - au17 * s * 0.05;          // AU17 raises lower lip/chin

        double lx = cx - half, rx = cx + half;
        double lyUp = my - corner, ryUp = my - corner;

        if (open < s * 0.03)
        {
            // Closed mouth: a curved line, corners up/down by AU12/AU15.
            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(new Point(lx, lyUp), false);
                g.QuadraticBezierTo(new Point(cx, my + corner * 0.4 + (au12 + au15) * 0), new Point(rx, ryUp));
            }
            ctx.DrawGeometry(null, mouthPen, line);
        }
        else
        {
            // Open mouth: an ellipse-ish lip contour with dark interior.
            var lips = new StreamGeometry();
            using (var g = lips.Open())
            {
                g.BeginFigure(new Point(lx, my), true);
                g.QuadraticBezierTo(new Point(cx, my - corner - open * 0.5), new Point(rx, my));
                g.QuadraticBezierTo(new Point(cx, my + open), new Point(lx, my));
            }
            ctx.DrawGeometry(mouthFill, mouthPen, lips);
        }
    }
}
