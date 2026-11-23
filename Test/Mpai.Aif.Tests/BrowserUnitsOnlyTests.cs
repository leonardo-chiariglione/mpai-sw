using System.Text.RegularExpressions;

namespace Mpai.Aif.Tests;

// MEDIA IS CAPTURED AND DELIVERED ONLY THROUGH UNITS (the author, 2026-11-23). In a browser the Units are
// AIF\UserAgent\PhysicalLayer\Browser\mpai-units.js: the only file of a page that may open a microphone or a camera
// (getUserMedia) or open a sound (AudioContext, an audio element). Every other page of the Apps asks the Units.
//
// The pages that do not go through the Units yet are NAMED below; the list is to become empty, one page at a time, and a
// new page that opens a device itself fails here. Converted so far: the door page of the MMM viewer (door.html) and the
// ABV page (Apps\ABV\AVS\Server\wwwroot\app.js), the MMM viewer (microphone, the room's sound, the shared screen with its compressed video and sound) rca.js, the browser client of the MAS App, the EAE room page (the channels of a microphone array, the voices of the room) the XRV stage page (the sounds of the show) the Thalia Launcher (the devices of an App: their names and their test) and the avatar page (her voice: a speech with a clock). The page that plays a video on the stage plays it through the Video Unit (a YouTube link is played by YouTube's own player, which the Units cannot reach).
[Trait("Group", "Fast")]
[Trait("Blocks", "No")]
public class BrowserUnitsOnlyTests
{
    private static readonly Regex OpensADevice = new(@"getUserMedia|getDisplayMedia|new\s+(VideoEncoder|AudioEncoder|VideoDecoder|AudioDecoder)|new\s+(window\.)?(AudioContext|webkitAudioContext)|createMediaStreamSource|new\s+Audio\(|<audio\b|<video\b|createElement\(\s*[\x27\x22](audio|video)[\x27\x22]\s*\)", RegexOptions.Compiled);

    // TO CONVERT (relative to the repository, forward slashes).
    private static readonly HashSet<string> NotYetConverted = new(StringComparer.OrdinalIgnoreCase)
    {
    };

    [Fact]
    public void OnlyTheUnitsOpenADevice()
    {
        var root = Repository.Root;
        var offenders = new List<string>();
        var seenNotYet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var top in new[] { "Apps", "AllApps", "AIF", "Thalia", "MPAIStore" })
        {
            var dir = Path.Combine(root, top);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                         .Where(p => p.EndsWith(".html") || p.EndsWith(".js") || p.EndsWith(".mjs") || p.EndsWith(".razor")))
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/") || rel.Contains("/lib/") || rel.Contains("/node_modules/") || rel.Contains("/deploy/")) continue;
                if (rel.EndsWith("/mpai-units.js")) continue;
                if (!OpensADevice.IsMatch(File.ReadAllText(f))) continue;
                if (NotYetConverted.Contains(rel)) seenNotYet.Add(rel); else offenders.Add(rel);
            }
        }
        Assert.True(offenders.Count == 0, "These pages open a microphone, a camera or a sound themselves; they must ask the Units (mpai-units.js): " + string.Join(", ", offenders));
        // A page converted must leave the list: otherwise the list lies.
        var converted = NotYetConverted.Where(p => !seenNotYet.Contains(p)).ToList();
        Assert.True(converted.Count == 0, "These pages no longer open a device: take them off the list: " + string.Join(", ", converted));
    }
}
