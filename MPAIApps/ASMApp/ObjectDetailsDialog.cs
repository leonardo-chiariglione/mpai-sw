using System;
using System.Collections.Generic;
using System.Windows.Forms;

using Mpai.Core;

namespace MPAIApps.ASMApp;

// WHAT AN OBJECT IS, on right-clicking it in the list.
//
// The Objects list shows identifiers, which say nothing: AUO000004 is not a
// thing anyone can recognise. This is the answer to "what is that?" - asked
// before placing something, which is why it is a right-click on the list rather
// than a button that acts on a selection.
//
// IT SHOWS AND DOES NOT EDIT. Object Edit is where an Object is changed; this is
// where it is examined. The two used to overlap - both offering a name and a
// description - which is two dialogs editing one thing and differing only in
// which fields they happen to carry.
//
// Name and Description are the first line and the rest of DescrMetadata: the
// schemas have no Name field, and adding one to every Data Type a person handles
// is a question for MPAI rather than a change to make here.
public sealed class ObjectDetailsDialog : Form
{

    public ObjectDetailsDialog(
        string assetId,
        string? descrMetadata,
        DateTime? storedAt,
        string kind,
        long? sizeInBytes,
        (double X, double Y, double Z)? listener,
        IReadOnlyList<(string Id, double X, double Y, double Z)> components,
        string? format,
        double? durationSeconds)
    {
        Text = assetId;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        var (name, description) = SplitDescrMetadata(descrMetadata);

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

        static Label Value(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0)
        };

        void Row(string caption, Control control)
        {
            layout.Controls.Add(Caption(caption));
            layout.Controls.Add(control);
        }

        Row("Name", Value(name.Length > 0 ? name : "unnamed"));
        Row("Description", Value(description.Length > 0 ? description : "none"));

        // WHAT IS ABSENT SAYS SO. A Qualifier nothing filled is worth seeing:
        // omitting the line would hide the gap rather than report it.
        Row("Created", Value(storedAt is null
            ? "not recorded"
            : storedAt.Value.ToLocalTime().ToString("d MMM yyyy, HH:mm")));

        Row("Kind", Value(kind));

        Row("Size", Value(sizeInBytes is null
            ? "not known"
            : $"{sizeInBytes.Value / 1024.0:N0} kB"));

        Row("Format", Value(format ?? "not recorded"));

        Row("Duration", Value(durationSeconds is null
            ? "not recorded"
            : $"{durationSeconds.Value:0.0} s"));

        Row("Listener", Value(listener is null
            ? "not recorded"
            : $"({listener.Value.X:0.0}, {listener.Value.Y:0.0}, {listener.Value.Z:0.0})"));

        if (components.Count > 0)
        {
            layout.Controls.Add(new Label
            {
                Text = "Components",
                AutoSize = true,
                Font = new System.Drawing.Font("Segoe UI", 8, System.Drawing.FontStyle.Bold),
                Margin = new Padding(0, 14, 0, 4)
            });
            layout.SetColumnSpan(layout.Controls[layout.Controls.Count - 1], 2);

            foreach (var (id, x, y, z) in components)
            {
                Row("   " + id, Value($"({x:0.0}, {y:0.0}, {z:0.0})"));
            }
        }

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

        Width  = 460;
        Height = layout.PreferredSize.Height + buttons.Height + 46;
    }

    // First line the name, the rest the description.
    private static (string Name, string Description) SplitDescrMetadata(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return ("", "");

        var lines = stored.Replace("\r\n", "\n").Split('\n');
        var name  = lines[0].Trim();
        var rest  = lines.Length > 1 ? string.Join(Environment.NewLine, lines[1..]).Trim() : "";

        return (name, rest);
    }


}