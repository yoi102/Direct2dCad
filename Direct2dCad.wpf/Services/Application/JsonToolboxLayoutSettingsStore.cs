using System.IO;
using System.Text.Json;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Services.Application;

internal sealed class JsonToolboxLayoutSettingsStore : IToolboxLayoutSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _syncRoot = new();
    private readonly string _filePath;
    private CadToolboxLayoutSettings? _settings;

    public JsonToolboxLayoutSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Direct2dCad",
            "toolbox-settings.json"))
    {
    }

    internal JsonToolboxLayoutSettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public CadToolboxState? Load(string contentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        lock (_syncRoot)
        {
            var settings = GetOrLoadSettings();
            return settings.Toolboxes.TryGetValue(contentId, out var state)
                ? state.Clone()
                : null;
        }
    }

    public void Save(IEnumerable<KeyValuePair<string, CadToolboxState>> toolboxes)
    {
        ArgumentNullException.ThrowIfNull(toolboxes);
        lock (_syncRoot)
        {
            var settings = new CadToolboxLayoutSettings
            {
                Toolboxes = toolboxes
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                    .ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.Clone(),
                        StringComparer.Ordinal)
            };
            settings.Normalize();

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var temporaryFilePath = _filePath + ".tmp";
            File.WriteAllText(
                temporaryFilePath,
                JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(temporaryFilePath, _filePath, overwrite: true);
            _settings = settings;
        }
    }

    private CadToolboxLayoutSettings GetOrLoadSettings()
    {
        if (_settings is not null)
            return _settings;

        if (!File.Exists(_filePath))
            return _settings = new CadToolboxLayoutSettings();

        try
        {
            var settings = JsonSerializer.Deserialize<CadToolboxLayoutSettings>(
                               File.ReadAllText(_filePath),
                               SerializerOptions) ??
                           new CadToolboxLayoutSettings();
            settings.Normalize();
            return _settings = settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return _settings = new CadToolboxLayoutSettings();
        }
    }
}
