using System;
using System.IO;

using Avalonia;

namespace CavMac;

// Entry point for CAV-MAC V2.0. Mirrors TstUi's bootstrap: build the Avalonia app
// and, if anything throws before the UI is up, record it to a crash log so a
// headless failure is not silent.
internal static class Program
{
    public static readonly string CrashLog =
        Path.Combine(AppContext.BaseDirectory, "cav-mac-crash.log");

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception fatal)
        {
            Record("fatal", fatal);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    public static void Record(string context, Exception error)
    {
        try { File.AppendAllText(CrashLog, $"{DateTime.Now:o}  [{context}]  {error}{Environment.NewLine}"); }
        catch { /* nothing more we can do */ }
    }
}
