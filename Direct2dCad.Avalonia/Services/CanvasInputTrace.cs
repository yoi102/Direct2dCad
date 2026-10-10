using System.Runtime.CompilerServices;

namespace Direct2dCad.Avalonia.Services;

internal static class CanvasInputTrace
{
    private static readonly string? Output=Environment.GetEnvironmentVariable("DIRECT2DCAD_CANVAS_INPUT_TRACE");
    public static bool Enabled=>!string.IsNullOrWhiteSpace(Output);
    public static void Write(string message)
    {
        if(!Enabled)return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Output!))!);
            File.AppendAllText(Output!,$"{DateTimeOffset.UtcNow:o} pid={Environment.ProcessId} dynamicCodeSupported={RuntimeFeature.IsDynamicCodeSupported} {message}\n");
        }
        catch { }
    }
}
