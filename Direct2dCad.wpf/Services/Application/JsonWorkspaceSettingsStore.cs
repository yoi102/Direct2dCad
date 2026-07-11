using System.IO;
using System.Text.Json;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Services.Application;

internal sealed class JsonWorkspaceSettingsStore : IWorkspaceSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _syncRoot = new();
    private readonly string _filePath;
    private CadWorkspaceSettings? _settings;

    public JsonWorkspaceSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Direct2dCad",
            "workspace-settings.json"))
    {
    }

    internal JsonWorkspaceSettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public CadDocumentWorkspaceSettings LoadDocument(string documentFilePath)
    {
        var normalizedPath = NormalizeDocumentPath(documentFilePath);
        lock (_syncRoot)
        {
            var settings = GetOrLoadSettings();
            return settings.Documents.TryGetValue(normalizedPath, out var documentSettings)
                ? documentSettings.Clone()
                : new CadDocumentWorkspaceSettings();
        }
    }

    public void SaveDocument(string documentFilePath, CadDocumentWorkspaceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalizedPath = NormalizeDocumentPath(documentFilePath);

        lock (_syncRoot)
        {
            var workspaceSettings = GetOrLoadSettings();
            var normalizedSettings = settings.Clone();
            normalizedSettings.Normalize();
            workspaceSettings.Documents[normalizedPath] = normalizedSettings;
            SaveSettings(workspaceSettings);
        }
    }

    private CadWorkspaceSettings GetOrLoadSettings()
    {
        if (_settings is not null)
            return _settings;

        _settings = LoadSettings();
        return _settings;
    }

    private CadWorkspaceSettings LoadSettings()
    {
        if (!File.Exists(_filePath))
            return new CadWorkspaceSettings();

        try
        {
            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<CadWorkspaceSettings>(json, SerializerOptions)
                           ?? new CadWorkspaceSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new CadWorkspaceSettings();
        }
    }

    private void SaveSettings(CadWorkspaceSettings settings)
    {
        settings.Normalize();
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        var temporaryFilePath = _filePath + ".tmp";
        File.WriteAllText(temporaryFilePath, json);
        File.Move(temporaryFilePath, _filePath, overwrite: true);
    }

    private static string NormalizeDocumentPath(string documentFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentFilePath);
        return Path.GetFullPath(documentFilePath);
    }
}
