using System.Runtime.CompilerServices;
using Avalonia.Threading;

namespace Direct2dCad.Avalonia.Services;

internal static class AppCrashLog
{
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write(e.ExceptionObject, "Unhandled");
        Dispatcher.UIThread.UnhandledException += (_, e) => Write(e.Exception, "Dispatcher");
    }
    public static void Write(object error, string source)
    {
        try
        {
            var settings = Environment.GetEnvironmentVariable("DIRECT2DCAD_SETTINGS_DIRECTORY");
            var directory = Path.Combine(string.IsNullOrWhiteSpace(settings)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Direct2dCad", "Avalonia") : settings, "logs");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss-fffffff}-{Environment.ProcessId}.log"),
                $"{DateTimeOffset.UtcNow:o}\nSource: {source}\nExecutable: {Environment.ProcessPath}\nNativeAOT: {!RuntimeFeature.IsDynamicCodeSupported}\n{error}");
        }
        catch { /* Logging must not replace the original exception. */ }
    }
}
