using System;
using System.Collections.Generic;
using System.Windows.Forms;

using Mpai.Core;

namespace MPAIApps.ASMApp;

// WHAT AN OBJECT IS - the internal characteristics, and the only place they are
// edited.
//
// The division: an Object's own attributes are INTERNAL, part of what it is, so
// changing any of them makes a new Object. Where something sits in a composition
// is EXTERNAL - the containing Object's description of that use - and belongs to
// the composition rather than to the thing placed. This dialog holds the first;
// the details view holds the second.
//
// It replaces Stage Edit and Discard Edit, which set the AcousticProfile fields
// in the main window and then needed a second press to apply them. A person
// opens the thing they mean to change and changes it.
public sealed class ObjectEditDialog : Form
{
    public string? EditedDescrMetadata { get; private set; }
    public AcousticProfile? EditedAcousticProfile { get; private set; }

    // Each component's placement as edited: position, orientation and times.
    public IReadOnlyList<ComponentPlacement> EditedPlacements => editedPlacements;

    private readonly List<ComponentPlacement> editedPlacements = new();
    private readonly List<(string Id, NumericUpDown[] Fields)> componentRows = new();

    public sealed class ComponentPlacement
    {
        public required string Id { get; init; }
        public double X { get; init; }
        public double Y { get; init; }
        public double Z { get; init; }
        public double Roll { get; init; }
        public double Pitch { get; init; }
        public double Yaw { get; init; }
        public double StartTime { get; init; }
        public double EndTime { get; init; }
    }

    private readonly TextBox nameBox = new() { Width = 320 };
    private readonly TextBox descriptionBox = new()
    {
        Width = 320,
        Height = 64,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical
    };

    private static NumericUpDown Field(decimal minimum, decimal maximum, decimal increment, int places) =>
        new() { Minimum = minimum, Maximum = maximum, DecimalPlaces = places, Increment = increment, Width = 100 };

    private static NumericUpDown Placement(decimal minimum, decimal maximum, double value)
    {
        var field = new NumericUpDown
        {
            Minimum = minimum, Maximum = maximum,
            DecimalPlaces = 1, Increment = 0.5M, Width = 62,
            Margin = new Padding(2)
        };

        field.Value = (decimal)Math.Clamp(value, (double)minimum, (double)maximum);
        return field;
    }

    private readonly NumericUpDown minFrequencyHz = Field(0, 22000, 100, 0);
    private readonly NumericUpDown maxFrequencyHz = Field(0, 22000, 100, 0);
    private readonly NumericUpDown loudnessLufs   = Field(-60, 0, 0.5M, 1);

    public ObjectEditDialog(
        string assetId,
        string? descrMetadata,
        AcousticProfile? acousticProfile,
        IReadOnlyList<ComponentPlacement> components)
    {
        Text = $"Edit {assetId}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var (name, description) = SplitDescrMetadata(descrMetadata);
        nameBox.Text = name;
        descriptionBox.Text = description;

        minFrequencyHz.Value = (decimal)(acousticProfile?.FrequencyRange?.MinFrequencyHz ?? 80);
        maxFrequencyHz.Value = (decimal)(acousticProfile?.FrequencyRange?.MaxFrequencyHz ?? 12000);
        loudnessLufs.Value   = (decimal)(acousticProfile?.Loudness ?? -16);

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
            Margin = new Padding(0, 6, 12, 0)
        };

        void Row(string caption, Control control)
        {
            layout.Controls.Add(Caption(caption));
            layout.Controls.Add(control);
        }

        void Heading(string text)
        {
            layout.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold),
                Margin = new Padding(0, 12, 0, 4)
            });
            layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);
        }

        Row("Name", nameBox);
        Row("Description", descriptionBox);

        Heading("Acoustic Profile");

        Row("Lowest frequency (Hz)", minFrequencyHz);
        Row("Highest frequency (Hz)", maxFrequencyHz);
        Row("Loudness (LUFS)", loudnessLufs);

        // WHERE EACH COMPONENT SITS, in figures.
        //
        // This is refinement, not arranging: nudging one thing that came out
        // wrong. Arranging is done on the canvas, by placing and dragging, and
        // wanting a different arrangement altogether means making a new Object
        // rather than retyping this one.
        //
        // Alpha, beta and gamma are here because x, y and z are: an orientation
        // is as much a fact about a placement as a position, and until now
        // nothing anywhere could set one - the canvas arrow reaches yaw alone
        // and never stored it.
        if (components.Count > 0)
        {
            Heading("Components");

            var grid = new TableLayoutPanel
            {
                ColumnCount = 9,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 2, 0, 0)
            };

            static Label Head(string text) => new()
            {
                Text = text,
                AutoSize = true,
                Font = new System.Drawing.Font("Segoe UI", 7.5f),
                ForeColor = System.Drawing.Color.DimGray,
                Margin = new Padding(3, 4, 3, 2)
            };

            grid.Controls.Add(Head(""));
            foreach (var caption in new[] { "x", "y", "z", "alpha", "beta", "gamma", "from (s)", "to (s)" })
            {
                grid.Controls.Add(Head(caption));
            }

            foreach (var component in components)
            {
                grid.Controls.Add(new Label
                {
                    Text = component.Id,
                    AutoSize = true,
                    Margin = new Padding(0, 6, 10, 0)
                });

                var fields = new[]
                {
                    Placement(-50, 50, component.X),
                    Placement(-50, 50, component.Y),
                    Placement(-50, 50, component.Z),
                    Placement(-180, 180, component.Roll),
                    Placement(-180, 180, component.Pitch),
                    Placement(-180, 180, component.Yaw),
                    Placement(0, 3600, component.StartTime),
                    Placement(0, 3600, component.EndTime)
                };

                foreach (var field in fields) grid.Controls.Add(field);

                componentRows.Add((component.Id, fields));
            }

            layout.Controls.Add(grid);
            layout.SetColumnSpan(grid, 2);
        }

        layout.Controls.Add(new Label
        {
            Text = "Saving makes a new version: these are what the Object IS.",
            AutoSize = true,
            ForeColor = System.Drawing.Color.DimGray,
            Font = new System.Drawing.Font("Segoe UI", 7.5f),
            Margin = new Padding(0, 14, 0, 0)
        });
        layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);

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
            EditedDescrMetadata = JoinDescrMetadata(nameBox.Text, descriptionBox.Text);

            EditedAcousticProfile = new AcousticProfile
            {
                AcousticProfileID = acousticProfile?.AcousticProfileID ?? Guid.NewGuid().ToString(),
                FrequencyRange = new FrequencyRange
                {
                    MinFrequencyHz = (double)minFrequencyHz.Value,
                    MaxFrequencyHz = (double)maxFrequencyHz.Value
                },
                Loudness = (double)loudnessLufs.Value
            };

            foreach (var (id, fields) in componentRows)
            {
                editedPlacements.Add(new ComponentPlacement
                {
                    Id    = id,
                    X     = (double)fields[0].Value,
                    Y     = (double)fields[1].Value,
                    Z     = (double)fields[2].Value,
                    Roll  = (double)fields[3].Value,
                    Pitch = (double)fields[4].Value,
                    Yaw   = (double)fields[5].Value,
                    StartTime = (double)fields[6].Value,
                    EndTime   = (double)fields[7].Value
                });
            }
        };

        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90, Height = 28 };

        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);

        Controls.Add(layout);
        Controls.Add(buttons);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Width  = components.Count > 0 ? 700 : 470;
        Height = layout.PreferredSize.Height + buttons.Height + 46;
    }

    // First line the name, the rest the description. The schemas have no Name
    // field, and adding one to every Data Type a person handles is a question
    // for MPAI rather than a change to make here.
    private static (string Name, string Description) SplitDescrMetadata(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return ("", "");

        var lines = stored.Replace("\r\n", "\n").Split('\n');
        var name  = lines[0].Trim();
        var rest  = lines.Length > 1 ? string.Join(Environment.NewLine, lines[1..]).Trim() : "";

        return (name, rest);
    }

    private static string? JoinDescrMetadata(string name, string description)
    {
        name = name.Trim();
        description = description.Trim();

        if (name.Length == 0 && description.Length == 0) return null;
        if (description.Length == 0) return name;

        return name + Environment.NewLine + description;
    }
}