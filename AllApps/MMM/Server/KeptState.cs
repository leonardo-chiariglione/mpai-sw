using System.Text.Json.Nodes;

namespace Mpai.Mmm.Server;

// THE M-INSTANCE BETWEEN ITS RUNS (2026-11-26). The world of the MMM starts a first time; then it may close, and when it starts again it goes on from the state it had
// when it closed (MInstance.SaveState and RestoreState say what is kept). The state is a file, state.json, in a folder of this machine (by default
// %LOCALAPPDATA%\MPAI\MMM\<the M-Instance>); it is written a few seconds after the M-Instance changes, and when the server closes, through a file beside it that then replaces it,
// so that a server stopped while writing leaves the last whole state. --fresh starts the M-Instance as the first time; the state it had is kept beside, as state-<when>.json.
public sealed class KeptState(MInstance m, string folder)
{
    public string Folder { get; } = folder;
    private string File => Path.Combine(Folder, "state.json");
    private string? last;
    private readonly object writing = new();

    public static string DefaultFolder(string instance) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MPAI", "MMM", instance);

    // At the start: the state of the last run over the M-Instance just set up, or nothing (the first time, or --fresh). Returns what it did, in words.
    public string Restore(bool fresh)
    {
        Directory.CreateDirectory(Folder);
        if (!System.IO.File.Exists(File)) return $"The M-Instance starts for the first time (its state will be kept in {Folder}).";
        if (fresh)
        {
            var aside = Path.Combine(Folder, $"state-{DateTime.Now:yyyy-MM-dd-HHmmss}.json");
            System.IO.File.Move(File, aside);
            return $"The M-Instance starts as the first time (--fresh); the state it had is kept in {aside}.";
        }
        try
        {
            var said = m.RestoreState(JsonNode.Parse(System.IO.File.ReadAllText(File))!.AsObject());
            return "The M-Instance goes on from its last run: " + said + ".";
        }
        catch (Exception e)
        {
            var aside = Path.Combine(Folder, $"state-unreadable-{DateTime.Now:yyyy-MM-dd-HHmmss}.json");
            System.IO.File.Move(File, aside);
            return $"The state of the last run could not be read ({e.Message}): the M-Instance starts as the first time; the file is kept in {aside}.";
        }
    }

    // The state written if it changed since it was last written.
    public void Save()
    {
        lock (writing)
        {
            var now = m.SaveState();
            var compare = now.DeepClone().AsObject(); compare.Remove("Saved");
            var text = compare.ToJsonString();
            if (text == last) return;
            var temporary = File + ".writing";
            System.IO.File.WriteAllText(temporary, now.ToJsonString());
            System.IO.File.Move(temporary, File, overwrite: true);
            last = text;
        }
    }

    // Written every few seconds while the server runs.
    public async Task KeepAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3), stop); } catch (OperationCanceledException) { break; }
            try { Save(); } catch (Exception e) { Console.Error.WriteLine("keeping the state: " + e.Message); }
        }
    }
}
