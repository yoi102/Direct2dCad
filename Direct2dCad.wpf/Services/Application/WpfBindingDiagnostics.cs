using System.Diagnostics;
using System.IO;

namespace Direct2dCad.wpf.Services.Application;

/// <summary>Opt-in diagnostics for exercising the real window without changing WPF's debug output.</summary>
internal sealed class WpfBindingDiagnostics : IDisposable
{
    private readonly TextWriterTraceListener _listener;
    private readonly SourceLevels _previousLevel;

    private WpfBindingDiagnostics(string path)
    {
        // WPF does not activate its trace sources outside a debugger until Refresh is called.
        PresentationTraceSources.Refresh();
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _listener = new TextWriterTraceListener(new StreamWriter(fullPath, append: false) { AutoFlush = true });
        var source = PresentationTraceSources.DataBindingSource;
        _previousLevel = source.Switch.Level;
        source.Listeners.Add(_listener);
        source.Switch.Level = SourceLevels.Warning;
    }

    public static WpfBindingDiagnostics? StartFromEnvironment()
    {
        var path = Environment.GetEnvironmentVariable("DIRECT2DCAD_BINDING_TRACE_PATH");
        return string.IsNullOrWhiteSpace(path) ? null : Start(path);
    }

    internal static WpfBindingDiagnostics Start(string path) => new(path);

    public void Dispose()
    {
        var source = PresentationTraceSources.DataBindingSource;
        source.Listeners.Remove(_listener);
        source.Switch.Level = _previousLevel;
        _listener.Dispose();
    }
}
