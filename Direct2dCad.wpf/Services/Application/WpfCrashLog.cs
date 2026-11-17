using System.IO;
using System.Windows.Threading;

namespace Direct2dCad.wpf.Services.Application;

/// <summary>
/// Persists unhandled exceptions next to the user settings (logs/crash-*.log), matching the
/// Avalonia client's AppCrashLog. It only records; it never marks an exception as handled.
/// </summary>
internal static class WpfCrashLog
{
    private static int _installed;

    public static void Install(Dispatcher dispatcher)
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0) return;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(e.ExceptionObject, "Unhandled");
        dispatcher.UnhandledException += (_, e) => Write(e.Exception, "Dispatcher");
        TaskScheduler.UnobservedTaskException += (_, e) => Write(e.Exception, "UnobservedTask");
    }

    public static void Write(object error, string source)
    {
        try
        {
            var directory = ApplicationSettingsPathResolver.Resolve("logs");
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Environment.ProcessId}.log"),
                $"{DateTimeOffset.UtcNow:o}\nSource: {source}\nExecutable: {Environment.ProcessPath}\n{error}");
        }
        catch { /* Logging must not replace the original exception. */ }
    }
}
