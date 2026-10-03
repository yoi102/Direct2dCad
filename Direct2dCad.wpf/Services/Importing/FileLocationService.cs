using System.Diagnostics;
using System.IO;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Services.Importing;

internal sealed class FileLocationService : IFileLocationService
{
    public bool CanOpenContainingFolder(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        try { return Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(filePath))); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }
    }

    public void OpenContainingFolder(string filePath)
    {
        if (!CanOpenContainingFolder(filePath)) return;
        var path = Path.GetFullPath(filePath);
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        if (File.Exists(path)) start.Arguments = $"/select,\"{path}\"";
        else start.ArgumentList.Add(Path.GetDirectoryName(path)!);
        Process.Start(start);
    }
}
