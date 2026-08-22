using System.Windows.Forms;

namespace MPAIApps.ASMApp;

// Shown when the user right-clicks an item on the PlacementCanvas: everything
// about a placement that a PLAN VIEW CANNOT REACH.
//
// That is what decides what belongs here. The canvas gives X and Y by dragging
// and yaw by turning the arrow, because a plan view can express those. Height,
// time, and the two remaining Euler angles have no room in it - so they are
// here, together, rather than scattered.
//
// On OK the caller copies the values back into the PlacementCanvas.Item and
// calls RefreshDisplay().
public sealed class PlacementDetailsDialog : Form
{
    public double Z { get; private set; }
    public double StartTime { get; private set; }
    public double EndTime { get; private set; }
    public double Pitch { get; private set; }
    public double Roll { get; private set; }

    private static NumericUpDown Field(decimal minimum, decimal maximum, decimal increment) =>
        new() { Minimum = minimum, Maximum = maximum, DecimalPlaces = 1, Increment = increment, Width = 90 };

    private readonly NumericUpDown zField     = Field(-50, 50, 0.5M);
    private readonly NumericUpDown startField = Field(0, 3600, 0.5M);
    private readonly NumericUpDown endField   = Field(0, 3600, 0.5M);

    // Degrees, and signed: a source may tilt up or down, and lean either way.
    private readonly NumericUpDown pitchField = Field(-180, 180, 5M);
    private readonly NumericUpDown rollField  = Field(-180, 180, 5M);

    public PlacementDetailsDialog(
        string itemLabel,
        double currentZ,
        double currentStartTime,
        double currentEndTime,
        double currentPitch = 0,
        double currentRoll  = 0)
    {
        Text = $"Details for {itemLabel}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        zField.Value     = (decimal)currentZ;
        startField.Value = (decimal)currentStartTime;
        endField.Value   = (decimal)currentEndTime;
        pitchField.Value = (decimal)currentPitch;
        rollField.Value  = (decimal)currentRoll;

        // A GRID, not a flow, and the form sized from it.
        //
        // The form was a fixed 200 pixels tall with three fields and two docked
        // buttons, which left the OK button half off the bottom edge. Two more
        // fields would have hidden it entirely. The layout now reports the height
        // it needs and the form takes it.
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
            Margin = new Padding(0, 6, 10, 0)
        };

        void Row(string caption, Control field)
        {
            layout.Controls.Add(Caption(caption));
            layout.Controls.Add(field);
        }

        Row("Z (height, m):", zField);
        Row("Start time (s):", startField);
        Row("End time (s):",   endField);

        // A separator, so the angles read as their own group rather than as two
        // more numbers in a list of five.
        layout.Controls.Add(new Label
        {
            Text = "Orientation - yaw is the arrow on the canvas",
            AutoSize = true,
            ForeColor = System.Drawing.Color.DimGray,
            Font = new System.Drawing.Font("Segoe UI", 7.5f),
            Margin = new Padding(0, 12, 0, 2)
        });
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);

        Row("Pitch (deg):", pitchField);
        Row("Roll (deg):",  rollField);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 44,
            Padding = new Padding(8)
        };

        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90, Height = 28 };
        okButton.Click += (_, _) =>
        {
            Z         = (double)zField.Value;
            StartTime = (double)startField.Value;
            EndTime   = (double)endField.Value;
            Pitch     = (double)pitchField.Value;
            Roll      = (double)rollField.Value;
        };

        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 28 };

        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        Controls.Add(layout);
        Controls.Add(buttons);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        // Sized to what it holds, once the layout knows how tall that is.
        Width  = 300;
        Height = layout.PreferredSize.Height + buttons.Height + 40;
    }
}