using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Client.Common.Settings;

namespace Direct2dCad.ViewModels.Services.Platform;

/// <summary>The directory a desktop front-end keeps its settings files in.</summary>
/// <remarks>
/// <see cref="DirectoryEnvironmentVariable"/> overrides every front-end's default (tests and portable runs use it).
/// Each front-end keeps its own default so existing installations find their files where they already are.
/// </remarks>
public sealed class CadSettingsLocation
{
    public const string DirectoryEnvironmentVariable = "DIRECT2DCAD_SETTINGS_DIRECTORY";

    public CadSettingsLocation(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = Path.GetFullPath(directory);
    }

    public string Directory { get; }

    /// <summary>True when <see cref="DirectoryEnvironmentVariable"/> chose this location.</summary>
    public bool IsOverridden { get; private init; }

    public string GetPath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Path.Combine(Directory, fileName);
    }

    public static CadSettingsLocation Resolve(string defaultDirectory)
    {
        var configured = Environment.GetEnvironmentVariable(DirectoryEnvironmentVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? new CadSettingsLocation(defaultDirectory)
            : new CadSettingsLocation(configured) { IsOverridden = true };
    }
}

/// <summary>Source-generated JSON reads and crash-safe writes for settings files (NativeAOT-safe).</summary>
public static class CadSettingsFile
{
    /// <summary>The file's value, or <see langword="default"/> when it is missing, unreadable or malformed.</summary>
    public static T? Read<T>(string path, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), typeInfo) : default;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return default;
        }
    }

    /// <summary>
    /// Writes beside the target, flushes it to disk and swaps it in, so a crash or power loss mid-save cannot leave a
    /// truncated file that the next start would silently replace with defaults.
    /// </summary>
    public static void WriteAtomically<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            System.IO.Directory.CreateDirectory(directory);

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(value, typeInfo);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}

[JsonSerializable(typeof(CadUserSettings))]
[JsonSerializable(typeof(CadWorkspaceSettings))]
[JsonSerializable(typeof(CadToolboxLayoutSettings))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
internal sealed partial class CadSettingsJsonContext : JsonSerializerContext;

// The AI assistant file has always been camelCase in the WPF host; reading is case-insensitive, so files the
// Avalonia host wrote with PascalCase names still load.
[JsonSerializable(typeof(AiAssistantSettings))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    PropertyNameCaseInsensitive = true)]
internal sealed partial class CadAiSettingsJsonContext : JsonSerializerContext;

public sealed class JsonUserSettingsStore : IUserSettingsStore
{
    private readonly string _filePath;
    private readonly string? _fallbackFilePath;

    /// <param name="fallbackFilePath">Read when <paramref name="filePath"/> does not exist yet (first run of a host
    /// that can adopt another host's preferences); never written.</param>
    public JsonUserSettingsStore(string filePath, string? fallbackFilePath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _fallbackFilePath = fallbackFilePath;
    }

    public CadUserSettings Load()
    {
        var settings = CadSettingsFile.Read(_filePath, CadSettingsJsonContext.Default.CadUserSettings);
        if (settings is null && _fallbackFilePath is not null && !File.Exists(_filePath))
            settings = CadSettingsFile.Read(_fallbackFilePath, CadSettingsJsonContext.Default.CadUserSettings);
        settings ??= CadUserSettings.CreateDefault();
        settings.Normalize();
        return settings;
    }

    public void Save(CadUserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        CadSettingsFile.WriteAtomically(_filePath, settings, CadSettingsJsonContext.Default.CadUserSettings);
    }
}

public sealed class JsonWorkspaceSettingsStore(string filePath) : IWorkspaceSettingsStore
{
    private readonly object _syncRoot = new();
    private CadWorkspaceSettings? _settings;

    public CadDocumentWorkspaceSettings LoadDocument(string documentFilePath)
    {
        var key = NormalizeDocumentPath(documentFilePath);
        lock (_syncRoot)
        {
            return GetOrLoad().Documents.TryGetValue(key, out var settings)
                ? settings.Clone()
                : new CadDocumentWorkspaceSettings();
        }
    }

    public void SaveDocument(string documentFilePath, CadDocumentWorkspaceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var key = NormalizeDocumentPath(documentFilePath);
        lock (_syncRoot)
        {
            var workspace = GetOrLoad();
            var copy = settings.Clone();
            copy.Normalize();
            workspace.Documents[key] = copy;
            workspace.Normalize();
            CadSettingsFile.WriteAtomically(filePath, workspace, CadSettingsJsonContext.Default.CadWorkspaceSettings);
        }
    }

    private CadWorkspaceSettings GetOrLoad()
    {
        if (_settings is not null)
            return _settings;
        var settings = CadSettingsFile.Read(filePath, CadSettingsJsonContext.Default.CadWorkspaceSettings) ?? new CadWorkspaceSettings();
        settings.Normalize();
        return _settings = settings;
    }

    private static string NormalizeDocumentPath(string documentFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentFilePath);
        return Path.GetFullPath(documentFilePath);
    }
}

public sealed class JsonToolboxLayoutSettingsStore(string filePath) : IToolboxLayoutSettingsStore
{
    private readonly object _syncRoot = new();
    private CadToolboxLayoutSettings? _settings;

    public CadToolboxState? Load(string contentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);
        lock (_syncRoot)
        {
            return GetOrLoad().Toolboxes.TryGetValue(contentId, out var state) ? state.Clone() : null;
        }
    }

    public void Save(IEnumerable<KeyValuePair<string, CadToolboxState>> toolboxes)
    {
        ArgumentNullException.ThrowIfNull(toolboxes);
        lock (_syncRoot)
        {
            var saved = new Dictionary<string, CadToolboxState>(StringComparer.Ordinal);
            foreach (var (contentId, state) in toolboxes)
            {
                var id = contentId?.Trim();
                if (string.IsNullOrWhiteSpace(id) || state is null)
                    continue;
                saved[id] = state.Clone();
            }

            var settings = new CadToolboxLayoutSettings { Toolboxes = saved };
            settings.Normalize();
            CadSettingsFile.WriteAtomically(filePath, settings, CadSettingsJsonContext.Default.CadToolboxLayoutSettings);
            _settings = settings;
        }
    }

    private CadToolboxLayoutSettings GetOrLoad()
    {
        if (_settings is not null)
            return _settings;
        var settings = CadSettingsFile.Read(filePath, CadSettingsJsonContext.Default.CadToolboxLayoutSettings) ?? new CadToolboxLayoutSettings();
        settings.Normalize();
        return _settings = settings;
    }
}

public sealed class JsonAiAssistantSettingsStore : IAiAssistantSettingsStore
{
    private readonly string _filePath;

    public JsonAiAssistantSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public AiAssistantSettings Load()
    {
        var settings = CadSettingsFile.Read(_filePath, CadAiSettingsJsonContext.Default.AiAssistantSettings) ?? new AiAssistantSettings();
        settings.Normalize();
        return settings;
    }

    public void Save(AiAssistantSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        CadSettingsFile.WriteAtomically(_filePath, settings, CadAiSettingsJsonContext.Default.AiAssistantSettings);
    }
}
