using System.Text.Json;
using Direct2dCad.AI.Contracts;

namespace Direct2dCad.AI.LmStudio;

public sealed class JsonAiAssistantSettingsStore : IAiAssistantSettingsStore
{
    private readonly string _filePath;

    public JsonAiAssistantSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Direct2dCad",
            "ai-assistant-settings.json"))
    {
    }

    public JsonAiAssistantSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public AiAssistantSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var settings = JsonSerializer.Deserialize(
                    File.ReadAllText(_filePath),
                    AiSettingsJsonContext.Default.AiAssistantSettings);
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }

        return new AiAssistantSettings();
    }

    public void Save(AiAssistantSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Write beside the target, then replace it: a crash mid-write must not leave a truncated file
        // that Load would silently replace with defaults.
        var temporaryPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(settings, AiSettingsJsonContext.Default.AiAssistantSettings));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
