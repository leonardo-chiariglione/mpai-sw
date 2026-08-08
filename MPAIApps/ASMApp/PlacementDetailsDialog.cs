using System.Windows.Forms;

namespace MPAIApps.ASMApp;

// Shown when the user right-clicks an item on the PlacementCanvas - editing
// Z (height) and the time range, which are deliberately NOT part of the
// always-visible canvas UI. On OK, the caller is expected to copy the
// values back into the corresponding PlacementCanvas.Item and call
// RefreshDisplay().
public sealed class PlacementDetailsDialog : Form
{
    public double Z { get; private set; }
    public double StartTime { get; private set; }
    public double EndTime { get; private set; }

    private readonly NumericUpDown zField = new() { Minimum = -50, Maximum = 50, DecimalPlaces = 1, Increment = 0.5M, Width = 90 };
    private readonly NumericUpDown startField = new() { Minimum = 0, Maximum = 3600, DecimalPlaces = 1, Increment = 0.5M, Width = 90 };
    private readonly NumericUpDown endField = new() { Minimum = 0, Maximum = 3600, DecimalPlaces = 1, Increment = 0.5M, Width = 90 };

    public PlacementDetailsDialog(string itemLabel, double currentZ, double currentStartTime, double currentEndTime)
    {
        Text = $"Details for {itemLabel}";
        Width = 260;
        Height = 200;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        zField.Value = (decimal)currentZ;
        startField.Value = (decimal)currentStartTime;
        endField.Value = (decimal)currentEndTime;

        var layout = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10), AutoSize = true };
        layout.Controls.Add(new Label { Text = "Z (height, metres):", AutoSize = true });
        layout.Controls.Add(zField);
        layout.Controls.Add(new Label { Text = "Start time (s):", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
        layout.Controls.Add(startField);
        layout.Controls.Add(new Label { Text = "End time (s):", AutoSize = true, Margin = new Padding(0, 10, 0, 0) });
        layout.Controls.Add(endField);

        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 32 };
        okButton.Click += (_, _) =>
        {
            Z = (double)zField.Value;
            StartTime = (double)startField.Value;
            EndTime = (double)endField.Value;
        };

        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Dock = DockStyle.Bottom, Height = 32 };

        Controls.Add(layout);
        Controls.Add(cancelButton);
        Controls.Add(okButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;
    }
}