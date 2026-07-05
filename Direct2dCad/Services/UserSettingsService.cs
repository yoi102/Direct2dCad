using System.IO;
using System.Text.Json;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Services.ViewServices;

namespace Direct2dCad.wpf.Services;

internal sealed class UserSettingsService : IUserSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public UserSettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Direct2dCad",
            "user-settings.json"))
    {
    }

    internal UserSettingsService(string filePath)
    {
        _filePath = filePath;
    }

    public CadUserSettings Load()
    {
        if (!File.Exists(_filePath))
            return CreateDefaultSettings();

        try
        {
            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<CadUserSettings>(json, SerializerOptions);
            if (settings is null)
                return CreateDefaultSettings();

            settings.Normalize();
            return settings;
        }
        catch (JsonException)
        {
            return CreateDefaultSettings();
        }
        catch (IOException)
        {
            return CreateDefaultSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return CreateDefaultSettings();
        }
    }

    public void Save(CadUserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }

    private static CadUserSettings CreateDefaultSettings()
    {
        var settings = CadUserSettings.CreateDefault();
        settings.Normalize();
        return settings;
    }
}
