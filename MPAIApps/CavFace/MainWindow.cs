using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using Mpai.Core.OSD;

namespace CavFace;

// The CAV's 2D visual delivery: a FACS-driven face that shows the machine's
// (generated) Personal Status as an expression, and lip-syncs when speaking.
// Emotion buttons set the expression via EM-FACS -> Action Units; Speak animates
// the mouth (lip-sync) for a few seconds.
public sealed class MainWindow : Window
{
    private readonly FaceControl _face = new();
    private CancellationTokenSource? _speaking;

    public MainWindow()
    {
        Title = "CAV - Visual Delivery (FACS)";
        Width = 520; Height = 640;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x24, 0x2E));

        _face.HorizontalAlignment = HorizontalAlignment.Stretch;
        _face.VerticalAlignment = VerticalAlignment.Stretch;

        var status = new TextBlock
        {
            Text = "CALMNESS / calm", Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new(0, 12)
        };

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 8) };
        void AddEmotion(string label, string category)
        {
            var b = new Button
            {
                Content = label, Margin = new(4), Padding = new(14, 8), FontSize = 14,
                Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF2)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x24, 0x2E))
            };
            b.Click += (_, _) =>
            {
                _face.SetExpression(EmFacs.ToActionUnits(category, 0.85));
                status.Text = $"{category} / {label.ToLowerInvariant()}";
            };
            buttons.Children.Add(b);
        }
        AddEmotion("Happy", "HAPPINESS");
        AddEmotion("Sad", "SADNESS");
        AddEmotion("Angry", "ANGER");
        AddEmotion("Fearful", "FEAR");
        AddEmotion("Disgusted", "DISGUST");
        AddEmotion("Surprised", "SURPRISE");
        AddEmotion("Calm", "CALMNESS");

        var speak = new Button
        {
            Content = "Speak", Margin = new(4, 10), Padding = new(28, 10),
            HorizontalAlignment = HorizontalAlignment.Center, FontSize = 16,
            Background = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        speak.Click += async (_, _) => await SpeakAsync();

        var root = new DockPanel();
        DockPanel.SetDock(status, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(speak, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(buttons);
        root.Children.Add(speak);
        root.Children.Add(_face);
        Content = root;

        // Start calm.
        _face.SetExpression(EmFacs.ToActionUnits("CALMNESS", 0.0));
    }

    // Lip-sync animation: oscillate mouth openness for a few seconds to read as speaking.
    // (A later refinement drives this from the actual Machine Speech amplitude envelope.)
    private async Task SpeakAsync()
    {
        _speaking?.Cancel();
        _speaking = new CancellationTokenSource();
        var token = _speaking.Token;
        var rng = new Random();
        double t = 0;
        try
        {
            while (!token.IsCancellationRequested && t < 2.5)
            {
                // A talking envelope: syllable-like open/close with jitter.
                double env = Math.Abs(Math.Sin(t * 12)) * (0.5 + 0.5 * rng.NextDouble());
                await Dispatcher.UIThread.InvokeAsync(() => _face.SetMouthOpen(env));
                await Task.Delay(30, token);
                t += 0.03;
            }
        }
        catch (TaskCanceledException) { }
        await Dispatcher.UIThread.InvokeAsync(() => _face.SetMouthOpen(0));
    }
}
