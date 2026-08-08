using System;
using System.IO;
using System.Windows.Forms;

using AIF.Store;

namespace MPAIApps.StoreApp;

// The window an implementer sees. It never touches AMDs directly: every
// validation and every write goes through MpaiStore, so this form and the
// StoreApp CLI (if kept) agree on what "valid" and "published" mean.
public sealed class StoreForm : Form
{
    // Same default the CLI used. Consider moving this to a settings file if
    // the store folder ever needs to differ per machine.
    private const string StoreFolder = @"D:\AI\AIMs\AMDs";

    private readonly MpaiStore store = new(StoreFolder);

    private readonly ListBox publishedList = new() { Dock = DockStyle.Fill };
    private readonly TextBox statusBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new System.Drawing.Font("Consolas", 9)
    };

    public StoreForm()
    {
        Text = "MPAI Store";
        Width = 720;
        Height = 480;
        StartPosition = FormStartPosition.CenterScreen;

        var submitButton = new Button { Text = "Submit AIM Metadata...", Dock = DockStyle.Top, Height = 36 };
        submitButton.Click += (_, _) => SubmitFile();

        var refreshButton = new Button { Text = "Refresh", Dock = DockStyle.Top, Height = 28 };
        refreshButton.Click += (_, _) => RefreshList();

        var leftPanel = new Panel { Dock = DockStyle.Left, Width = 260 };
        leftPanel.Controls.Add(publishedList);
        leftPanel.Controls.Add(new Label { Text = $"Published in {StoreFolder}:", Dock = DockStyle.Top, Height = 20 });
        leftPanel.Controls.Add(refreshButton);
        leftPanel.Controls.Add(submitButton);

        Controls.Add(statusBox);
        Controls.Add(leftPanel);

        RefreshList();
    }

    private void RefreshList()
    {
        publishedList.Items.Clear();

        try
        {
            foreach (var name in store.List())
            {
                publishedList.Items.Add(name);
            }
        }
        catch (Exception failure)
        {
            Log($"Could not list the store: {failure.Message}");
        }
    }

    // The one entry point an implementer uses: pick a file, get validated,
    // get published (or told exactly why not).
    private void SubmitFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Submit an AIM Metadata instance",
            Filter = "AIM Metadata (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        string json;

        try
        {
            json = File.ReadAllText(dialog.FileName);
        }
        catch (Exception failure)
        {
            Log($"Could not read {dialog.FileName}: {failure.Message}");
            return;
        }

        var validation = store.Validate(json);

        Log($"--- {Path.GetFileName(dialog.FileName)} ---");

        foreach (var error in validation.Errors)
        {
            Log($"    ERROR   {error}");
        }

        foreach (var warning in validation.Warnings)
        {
            Log($"    warning {warning}");
        }

        if (!validation.IsValid)
        {
            Log("    REJECTED");
            MessageBox.Show(
                this,
                $"{Path.GetFileName(dialog.FileName)} did not pass validation. See the log for details.",
                "Rejected",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var replace = false;

        if (store.Exists(validation.AimName))
        {
            var answer = MessageBox.Show(
                this,
                $"{validation.AimName} is already published. Replace it with this version?",
                "Already published",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                Log($"    Publish of {validation.AimName} was cancelled by the user.");
                return;
            }

            replace = true;
        }

        var result = store.Publish(json, replace);

        if (result.WasPublished)
        {
            Log($"    published {result.AimName} -> {result.Path}");
            RefreshList();
        }
        else
        {
            // Validate() already passed, so this branch is the store
            // rejecting the publish itself (e.g. exists and replace=false
            // slipped through a race). Surface it rather than hide it.
            foreach (var error in result.Errors)
            {
                Log($"    ERROR   {error}");
            }

            Log("    REJECTED");
        }
    }

    private void Log(string line)
    {
        statusBox.AppendText(line + Environment.NewLine);
    }
}
