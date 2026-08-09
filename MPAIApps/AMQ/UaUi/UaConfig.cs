using System;
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
    }

    public static UaConfig Load()
    {
        // Look for ua-config.json next to the executable.
        var dir  = AppContext.BaseDirectory;
        var path = Path.Combine(dir, "ua-config.json");

        if (!File.Exists(path))
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
                OutputFolder  = Expand(raw.OutputFolder,  Path.Combine(root, "MPAIApps", "AMQ", "Output"))
            };
        }
        catch
        {
            return new UaConfig();   // any error -> safe defaults
        }
    }
}
