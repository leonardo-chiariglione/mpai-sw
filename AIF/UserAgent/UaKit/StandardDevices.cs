using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;

using Mpai.Aif.Api;          // AvatarUtterance
using Mpai.Core;
using Mpai.PhysicalLayer;    // CameraUnit

namespace Mpai.UaKit;

// THE DEVICES THAT MORE THAN ONE APP NEEDS, each as a function a window puts into its DeviceRegistry. They answer with the Object as
// JSON, ready to be the result of an acquisition; nothing here knows an App or a workflow.
public static class StandardDevices
{
    // THE AVATAR MODEL (OSD-B3O) this client holds and draws.
    public static string AvatarModel(string modelId = "cav-avatar.glb") => AvatarUtterance.AvatarDatum(modelId);

    // THE FACE OF THE PERSON (OSD-BVO whose type is a Face), a frame from the camera; null when the camera gave nothing.
    public static async Task<string?> FaceAsync()
    {
        var face = await Task.Run(() => new CameraUnit().AcquireFaceAsync());
        return face is null ? null : MpaiJson.ToJson(face);
    }

    // A PICTURE THE PERSON CHOOSES (OSD-BVO): the file chooser opens; null when the person gives none.
    public static async Task<string?> PictureAsync(Dispatcher ui)
    {
        var chosen = await ui.InvokeAsync(() =>
        {
            var dialog = new OpenFileDialog { Title = "Choose a picture", Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files|*.*" };
            if (TestData() is { } start) dialog.InitialDirectory = start;
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        });
        if (chosen is null) return null;
        var bytes = await File.ReadAllBytesAsync(chosen);
        return MpaiJson.ToJson(BasicVisualObject.FromFile(Path.GetFileName(chosen), bytes, "Visual Object"));
    }

    // WHERE THE FILE CHOOSER STARTS (the author, 2026-11-27, testing the Apps in Thalia): Test\Data of the software, where the test pictures are; where there is
    // none (a participant's machine) the chooser starts where Windows would.
    public static string? TestData()
    {
        try { var d = Path.Combine(MpaiPaths.Root, "Test", "Data"); return Directory.Exists(d) ? d : null; }
        catch { return null; }
    }

    // A PORTABLE AVATAR'S UTTERANCE, PRESENTED on the avatar; the step ends when she has finished speaking.
    public static async Task PresentAsync(AvatarUaHost avatar, AvatarUtterance utterance)
    {
        await avatar.PresentAsync(utterance);
        await Task.Delay(TimeSpan.FromSeconds(AvatarUaHost.WavDurationSeconds(utterance.MachineSpeechWav) + 0.3));
    }
}
