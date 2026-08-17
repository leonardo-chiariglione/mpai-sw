using System;
using System.IO;

using Avalonia;

namespace TstUi;

internal static class Program
{
    // A WinExe has no console, so anything thrown before the window appears is
    // lost: the process exits, nothing is printed, and the window simply never
    // shows. Everything is therefore wrapped, and whatever went wrong is written
    // where it can be read afterwards.
    internal static readonly string CrashLog =
        Path.Combine(Path.GetTempPath(), "tstui-crash.txt");

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Record("unhandled", e.ExceptionObject as Exception);

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            Record("unobserved task", e.Exception);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception failure)
        {
            Record("startup", failure);
            throw;
        }
    }

    internal static void Record(string what, Exception? failure)
    {
        if (failure is null) return;

        try
        {
            File.AppendAllText(
                CrashLog,
                $"--- {DateTime.Now:yyyy-MM-dd HH:mm:ss}  {what}{Environment.NewLine}" +
                failure + Environment.NewLine + Environment.NewLine);
        }
        catch { /* nothing left to try */ }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .LogToTrace();
}