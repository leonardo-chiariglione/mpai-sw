using System.Collections.Generic;
using System.Windows.Forms;

namespace MPAIApps.ASMApp;

// THE EDITING SPACE, seen as a whole.
//
// Every saved Object could be examined and the one being MADE could not - though
// it is the thing you are working on. This shows what is placed, where, and
// where it is being heard from.
//
// What it does NOT show is telling: no identifier, no creation time, no size.
// The Object does not exist yet. That absence is what distinguishes a draft from
// a thing, and printing "not recorded" against each would suggest a gap where
// there is none.
public sealed class DraftInfoDialog : Form
{
    public DraftInfoDialog(
        IReadOnlyList<(string Id, double X, double Y, double Z)> placed,
        (double X, double Y, double Z) listener)
    {
        Text = "Editing space";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 12, 12, 8)
        };

        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        static Label Caption(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 5, 12, 0)
        };

        static Label Value(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 5, 0, 0)
        };

        void Row(string caption, string value)
        {
            layout.Controls.Add(Caption(caption));
            layout.Controls.Add(Value(value));
        }

        Row("Listener", $"({listener.X:0.0}, {listener.Y:0.0}, {listener.Z:0.0})");

        layout.Controls.Add(new Label
        {
            Text = placed.Count == 0 ? "Nothing placed yet" : $"Placed ({placed.Count})",
            AutoSize = true,
            Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold),
            Margin = new Padding(0, 14, 0, 4)
        });
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);

        foreach (var (id, x, y, z) in placed)
        {
            Row("   " + id, $"({x:0.0}, {y:0.0}, {z:0.0})");
        }

        layout.Controls.Add(new Label
        {
            Text = placed.Count == 0
                ? "Select an Object, choose a position, and press Place."
                : "Save Changes makes one Object holding these, at these positions.",
            AutoSize = true,
            ForeColor = System.Drawing.Color.DimGray,
            Font = new System.Drawing.Font("Segoe UI", 7.5f),
            Margin = new Padding(0, 16, 0, 0)
        });
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8)
        };

        var closeButton = new Button { Text = "Close", DialogResult = DialogResult.OK, Width = 90, Height = 28 };
        buttons.Controls.Add(closeButton);

        Controls.Add(layout);
        Controls.Add(buttons);

        AcceptButton = closeButton;
        CancelButton = closeButton;

        Width  = 400;
        Height = layout.PreferredSize.Height + buttons.Height + 46;
    }
}