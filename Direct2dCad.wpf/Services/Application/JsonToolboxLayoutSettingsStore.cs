using System.IO;
using System.Text;
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
        : this(ApplicationSettingsPathResolver.Resolve("toolbox-settings.json"))
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
            var savedToolboxes = new Dictionary<string, CadToolboxState>(StringComparer.Ordinal);
            foreach (var (contentId, state) in toolboxes)
            {
                var normalizedContentId = contentId?.Trim();
                if (string.IsNullOrWhiteSpace(normalizedContentId) || state is null)
                    continue;

                savedToolboxes[normalizedContentId] = state.Clone();
            }

            var settings = new CadToolboxLayoutSettings
            {
                Toolboxes = savedToolboxes
            };
            settings.Normalize();

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            WriteSettingsAtomically(settings);
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
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return _settings = new CadToolboxLayoutSettings();
        }
    }

    private void WriteSettingsAtomically(CadToolboxLayoutSettings settings)
    {
        var temporaryFilePath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            using (var stream = new FileStream(
                       temporaryFilePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryFilePath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryFilePath))
                File.Delete(temporaryFilePath);
        }
    }
}
