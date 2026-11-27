using Mpai.Onnx;

namespace Mpai.Aif.Tests;

// ONE SESSION OF A MODEL FOR EVERY AIM THAT HOLDS IT (N-51, 2026-11-27): OnnxSessions.Shared gives the session already open for that model file, and the last Release closes it.
// The model is a copy of a small one of the test data under a name of this test, so that no other test holds it.
public class OnnxSharedSessionTests
{
    [Fact]
    public void TwoHoldersShareOneSessionAndTheLastClosesIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mpai-onnx-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var model = Path.Combine(folder, "head.onnx");
        File.Copy(Path.Combine(Repository.Root, "Test", "Data", "Fl", "cxr-head.onnx"), model);
        try
        {
            var first = OnnxSessions.Shared(model);
            var second = OnnxSessions.Shared(Path.Combine(folder, ".", "head.onnx"));   // the same file, named otherwise
            Assert.Same(first, second);
            Assert.Equal(2, OnnxSessions.HoldersOf(model));

            OnnxSessions.Release(first);
            Assert.Equal(1, OnnxSessions.HoldersOf(model));
            Assert.NotEmpty(second.InputMetadata);         // still open for the other holder

            OnnxSessions.Release(second);
            Assert.Equal(0, OnnxSessions.HoldersOf(model));

            var again = OnnxSessions.Shared(model);          // opened anew after the last one closed it
            Assert.NotSame(first, again);
            OnnxSessions.Release(again);
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }

    // The same model in two folders (as the Apps have it in AIF\Access\view) is one session; another model is a session of its own (2026-11-27).
    [Fact]
    public void TheSameModelInTwoFoldersIsOneSession()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mpai-onnx-copies-" + Guid.NewGuid().ToString("N"));
        string a = Path.Combine(folder, "a", "head.onnx"), b = Path.Combine(folder, "b", "head.onnx"), other = Path.Combine(folder, "c", "head.onnx");
        foreach (var f in new[] { a, b, other }) Directory.CreateDirectory(Path.GetDirectoryName(f)!);
        File.Copy(Path.Combine(Repository.Root, "Test", "Data", "Fl", "cxr-head.onnx"), a);
        File.Copy(a, b);
        File.Copy(Path.Combine(Repository.Root, "Test", "Data", "Music", "dataset", "TapeIrregularityClassifier.onnx"), other);    // another model under the same name
        try
        {
            var first = OnnxSessions.Shared(a);
            var copy = OnnxSessions.Shared(b);
            var different = OnnxSessions.Shared(other);
            Assert.Same(first, copy);
            Assert.NotSame(first, different);
            Assert.Equal(2, OnnxSessions.HoldersOf(b));
            OnnxSessions.Release(first); OnnxSessions.Release(copy); OnnxSessions.Release(different);
            Assert.Equal(0, OnnxSessions.HoldersOf(a));
            Assert.Equal(0, OnnxSessions.HoldersOf(other));
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }
}
