using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

using Mpai.Core;

namespace Mpai.Aims.Visual;

public sealed class WinFormsVisualDelivery : IVisualDeliveryAim
{
    private readonly PictureBox surface;

    public WinFormsVisualDelivery(PictureBox surface)
    {
        this.surface = surface;
    }

    public Task DeliverAsync(BasicVisualObject visual)
    {
        Image? image = null;

        // Load via file path when available - avoids GDI+ retaining a stream.
        if (!string.IsNullOrWhiteSpace(visual.FileName) &&
            File.Exists(visual.FileName))
        {
            image = new Bitmap(visual.FileName);
        }
        else if (visual.Data.Length > 0)
        {
            // Copy into a Bitmap so the MemoryStream is not retained by GDI+.
            using var ms  = new MemoryStream(visual.Data);
            using var tmp = Image.FromStream(ms);
            image = new Bitmap(tmp);
        }

        if (image is null)
            return Task.CompletedTask;

        void Show()
        {
            var old = surface.Image;
            surface.Image = image;
            surface.Refresh();
            old?.Dispose();
        }

        if (surface.InvokeRequired)
            surface.Invoke(Show);
        else
            Show();

        return Task.CompletedTask;
    }
}
