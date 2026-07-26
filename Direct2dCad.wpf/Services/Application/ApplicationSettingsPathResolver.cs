using System.IO;

namespace Direct2dCad.wpf.Services.Application;

internal static class ApplicationSettingsPathResolver
{
    internal const string SettingsDirectoryEnvironmentVariable =
        "DIRECT2DCAD_SETTINGS_DIRECTORY";

    public static string Resolve(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var configuredDirectory = Environment.GetEnvironmentVariable(
            SettingsDirectoryEnvironmentVariable);
        var directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Direct2dCad")
            : Path.GetFullPath(configuredDirectory);
        return Path.Combine(directory, fileName);
    }
}
