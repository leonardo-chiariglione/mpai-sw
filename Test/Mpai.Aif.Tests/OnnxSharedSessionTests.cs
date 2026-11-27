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
}
