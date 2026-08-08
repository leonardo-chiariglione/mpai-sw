using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

using Mpai.Core;
using Mpai.Amq;

// Observer window (host concern, Windows). Top-down: controls under the title,
// then image, recognised question, answer.
//
// Flow:
//   1) Select image -> CVE-VOA picks the image (shown), then TTS speaks
//      "Ask your question now" and recording STARTS immediately.
//   2) Stop -> recording stops; ASR -> TIQ -> TTS answer, spoken and shown.
internal sealed class ObserverWindow : Form
{
    private const string Cue = "Ask your question now";

    private readonly AmqCompositeAim _amq;
    private readonly string _imagesFolderHint;
    private BasicVisualObject? _image;

    private readonly Button _select = new()
    {
        Text = "1.  Select image  \u2192  ask",
        Dock = DockStyle.Fill
    };
    private readonly Button _stop = new()
    {
        Text = "2.  Stop  (I\u2019m done speaking)",
        Dock = DockStyle.Fill,
        Enabled = false
    };
    private readonly Button _again = new()
    {
        Text = "3.  Ask again  (same image)",
        Dock = DockStyle.Fill,
        Enabled = false
    };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "Select an image to begin."
    };
    private readonly PictureBox _picture = new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly TextBox _question = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox _answer = new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        Font = new Font("Segoe UI", 12F, FontStyle.Bold)
    };

    // The surface the Visual Object Delivery AIM renders onto, and the AIM
    // itself. The window owns the surface; the AIM owns how an object is put
    // on it — the same split as audio delivery and the sound device.
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    [System.ComponentModel.Browsable(false)]
    public PictureBox ImageSurface => _picture;

    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    [System.ComponentModel.Browsable(false)]
    public IVisualDeliveryAim? VisualDelivery { get; set; }

    public ObserverWindow(AmqCompositeAim amq, string imagesFolderHint)
    {
        _amq = amq;
        _imagesFolderHint = imagesFolderHint;

        Text = "Answer to Multimodal Question (MMC-AMQ-V2.5)";
        Width = 820;
        Height = 780;
        StartPosition = FormStartPosition.CenterScreen;

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        buttons.Controls.Add(_select, 0, 0);
        buttons.Controls.Add(_stop, 1, 0);
        buttons.Controls.Add(_again, 2, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));   // buttons
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // status
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // image
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));   // question
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));   // answer

        layout.Controls.Add(buttons, 0, 0);
        layout.Controls.Add(_status, 0, 1);
        layout.Controls.Add(Labeled("Image", _picture), 0, 2);
        layout.Controls.Add(Labeled("Recognised question", _question), 0, 3);
        layout.Controls.Add(Labeled("Answer", _answer), 0, 4);
        Controls.Add(layout);

        _select.Click += async (_, _) => await SelectAndListenAsync();
        _stop.Click += async (_, _) => await StopAndAnswerAsync();
        _again.Click += async (_, _) => await AskAgainAsync();
    }

    private static Control Labeled(string caption, Control inner)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        inner.Dock = DockStyle.Fill;
        var label = new Label { Text = caption, Dock = DockStyle.Top, Height = 18, ForeColor = Color.DimGray };
        panel.Controls.Add(inner);
        panel.Controls.Add(label);
        return panel;
    }

    private void SetStatus(string text, bool recording = false)
    {
        _status.Text = text;
        _status.ForeColor = recording ? Color.Firebrick : SystemColors.ControlText;
        _status.Font = new Font(_status.Font, recording ? FontStyle.Bold : FontStyle.Regular);
        _status.Refresh();
    }

    private async Task SelectAndListenAsync()
    {
        _select.Enabled = false;
        try
        {
            _again.Enabled = false;
            SetStatus("Selecting image\u2026");
            _image = await _amq.AcquireImageAsync(
                new VisualAcquisitionRequest { SourcePath = _imagesFolderHint });

            if (VisualDelivery is not null)
            {
                await VisualDelivery.DeliverAsync(_image);   // CVE-VOD
            }
            _question.Text = "";
            _answer.Text = "";

            // Spoken cue, then start recording immediately after it finishes.
            SetStatus("\u25CF  " + Cue);
            await _amq.SpeakAsync(Cue);

            _amq.StartListening();
            SetStatus("\u25CF  Recording\u2026 speak, then click Stop", recording: true);
            _stop.Enabled = true;
        }
        catch (OperationCanceledException oce)
        {
            SetStatus(oce.Message);
            _select.Enabled = true;
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
            _select.Enabled = true;
        }
    }

    private async Task AskAgainAsync()
    {
        if (_image is null) return;

        _select.Enabled = false;
        _again.Enabled = false;
        try
        {
            _question.Text = "";
            _answer.Text = "";

            SetStatus("\u25CF  " + Cue);
            await _amq.SpeakAsync(Cue);

            _amq.StartListening();
            SetStatus("\u25CF  Recording\u2026 speak, then click Stop", recording: true);
            _stop.Enabled = true;
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
            _select.Enabled = true;
            _again.Enabled = true;
        }
    }

    private async Task StopAndAnswerAsync()
    {
        if (_image is null) return;

        _stop.Enabled = false;
        SetStatus("Processing\u2026");
        try
        {
            var result = await _amq.StopAndAnswerAsync(_image);
            _question.Text = result.Question.GetText();
            _answer.Text = result.Answer.GetText();
            _again.Enabled = true;
            SetStatus("Done \u2014 ask again about this image, or select another.");
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
        }
        finally
        {
            _select.Enabled = true;
        }
    }
}
