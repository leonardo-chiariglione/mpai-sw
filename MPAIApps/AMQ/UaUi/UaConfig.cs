using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace UaUi;

// Loads path configuration from ua-config.json next to the executable.
//
// Portability: a group member on a different drive/layout edits ONE line
// (MpaiRoot) and the other paths derive from it via the %MpaiRoot% token.
// If the config file is absent or unreadable, the built-in D:\AI defaults are
// used, so existing setups keep working with no config file at all.
public sealed class UaConfig
{
    public string MpaiRoot      { get; init; } = @"D:\AI";
    public string AmdRepository { get; init; } = @"D:\AI\AIMs\AMDs";
    public string SettingsFile  { get; init; } = @"D:\AI\AIMs\aim-settings.json";
    public string OutputFolder  { get; init; } = @"D:\AI\MPAIApps\AMQ\Output";

    // When set (e.g. "http://localhost:5005"), the UI runs as a MAS Remote
    // Client Application (RCA), talking to that SCI. When empty (default),
    // the UI runs in-process with a local Controller + models, as before.
    public string MasServerUrl  { get; init; } = "";

    // Log file derives from the output folder's parent (the app folder).
    public string LogFile => Path.Combine(
        Path.GetDirectoryName(OutputFolder) ?? @"D:\AI\MPAIApps\AMQ",
        "UaUi", "ua-ui.log");

    private sealed class Raw
    {
        public string? MpaiRoot      { get; set; }
        public string? AmdRepository { get; set; }
        public string? SettingsFile  { get; set; }
        public string? OutputFolder  { get; set; }
        public string? MasServerUrl  { get; set; }
    }

    public static UaConfig Load()
    {
        // A config named after THIS executable, then the shared one; beside the
        // executable, then in bin.
        //
        // Two copies of this application are published side by side - a
        // standalone one and a MAS client - and they would otherwise read the
        // same ua-config.json and one of them would be wrong. Each now reads a
        // file named after itself, which is how they can share a folder at all.
        //
        // And the application folder should hold only what a person LAUNCHES, so
        // the configs live in bin with the server and the client. Beside the
        // executable is still tried first, so a config placed there deliberately
        // still wins and no existing setup changes.
        var executable = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "UaUi");

        var candidates = new List<string>();
        foreach (var directory in new[] { AppContext.BaseDirectory,
                                          Path.Combine(AppContext.BaseDirectory, "bin") })
        foreach (var name in new[] { executable + "-config.json", "ua-config.json" })
        {
            candidates.Add(Path.Combine(directory, name));
        }

        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null)
            return new UaConfig();   // built-in D:\AI defaults

        try
        {
            var raw = JsonSerializer.Deserialize<Raw>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (raw is null) return new UaConfig();

            var root = string.IsNullOrWhiteSpace(raw.MpaiRoot) ? @"D:\AI" : raw.MpaiRoot;

            string Expand(string? value, string fallback) =>
                string.IsNullOrWhiteSpace(value)
                    ? fallback
                    : value.Replace("%MpaiRoot%", root);

            return new UaConfig
            {
                MpaiRoot      = root,
                AmdRepository = Expand(raw.AmdRepository, Path.Combine(root, "AIMs", "AMDs")),
                SettingsFile  = Expand(raw.SettingsFile,  Path.Combine(root, "AIMs", "aim-settings.json")),
                OutputFolder  = Expand(raw.OutputFolder,  Path.Combine(root, "MPAIApps", "AMQ", "Output")),
                MasServerUrl  = raw.MasServerUrl ?? ""
            };
        }
        catch
        {
            return new UaConfig();   // any error -> safe defaults
        }
    }
}
