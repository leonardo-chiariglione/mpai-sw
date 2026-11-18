"""N-34 (night 2026-11-18): the TMA test gives Face Identity Recognition the face model its L3 pins (the one ACR registers with), not Models\\glintr100.onnx, a different file."""
from pathlib import Path

P = Path(r"D:\AI\Test\Mpai.Aif.Tests\TmaEndToEndTests.cs")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
old = """            ["1PAF-FIR-V1.6-I02"] = new JsonObject { ["ScrfdModel"] = Models("scrfd_10g_bnkps.onnx"), ["ArcFaceModel"] = Models("glintr100.onnx") },"""
new = """            ["1PAF-FIR-V1.6-I02"] = new JsonObject { ["ScrfdModel"] = Models("scrfd_10g_bnkps.onnx"), ["ArcFaceModel"] = PinnedFaceModel() ?? Models("glintr100.onnx") },"""
assert s.count(old) == 1
s = s.replace(old, new)
anchor = """    // ffmpeg: on the PATH, else where winget puts it"""
add = """    // THE FACE MODEL THE L3 PINS (2026-11-18): its AccessSources "face-embedder", SHA-256 4AB1D643..., the one Access Control Registration registers faces with, where the
    // Controller has obtained it on this machine (AIF\\Access). Models\\glintr100.onnx is another file of the same name: its features cannot be compared with ACR's.
    private static string? PinnedFaceModel()
    {
        var access = Path.Combine(Repository.Root, "AIF", "Access", "view");
        if (!Directory.Exists(access)) return null;
        foreach (var file in Directory.EnumerateFiles(access, "glintr100.onnx", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(file);
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).StartsWith("4AB1D6435D639628", StringComparison.Ordinal)) return file;
        }
        return null;
    }

"""
assert s.count(anchor) == 1
s = s.replace(anchor, add + anchor)
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
