using System;
using System.IO;
using System.Threading.Tasks;

using Mpai.Core;

namespace Mpai.Aims.Visual;

// Visual Object Delivery (CVE-VOD) — file destination.
// Writes the Basic Visual Object to a folder; portable, and the delivery used
// when there is no display (headless or server-side operation).
public sealed class FileVisualDelivery : IVisualDeliveryAim
{
    private readonly string destinationFolder;

    public FileVisualDelivery(
        string destinationFolder)
    {
        this.destinationFolder = destinationFolder;
    }

    public Task DeliverAsync(
        BasicVisualObject visual)
    {
        Directory.CreateDirectory(destinationFolder);

        var name =
            string.IsNullOrWhiteSpace(visual.FileName)
            ? $"{visual.BasicVisualObjectID}.img"
            : Path.GetFileName(visual.FileName);

        var path =
            Path.Combine(destinationFolder, name);

        if (visual.Data.Length > 0)
        {
            File.WriteAllBytes(path, visual.Data);
        }
        else if (File.Exists(visual.FileName))
        {
            File.Copy(visual.FileName!, path, overwrite: true);
        }

        return Task.CompletedTask;
    }
}
