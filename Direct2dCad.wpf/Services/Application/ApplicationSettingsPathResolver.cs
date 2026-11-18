using System.IO;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Services.Application;

internal static class ApplicationSettingsPathResolver
{
    internal const string SettingsDirectoryEnvironmentVariable = CadSettingsLocation.DirectoryEnvironmentVariable;

    /// <summary>%APPDATA%\Direct2dCad unless <see cref="SettingsDirectoryEnvironmentVariable"/> overrides it.</summary>
    public static CadSettingsLocation Location => CadSettingsLocation.Resolve(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Direct2dCad"));

    public static string Resolve(string fileName) => Location.GetPath(fileName);
}
