using Direct2dCad.Client.Common.Settings;

namespace Direct2dCad.ViewModels.Services.Platform;

/// <summary>
/// Keeps MCP settings supplied for one launch (<c>--mcp-port</c>, <c>--mcp-token</c>,
/// <c>DIRECT2DCAD_MCP_TOKEN</c>, or a start by the MCP stdio bridge) out of the saved user
/// settings. Every save in the session goes through this store, so changing the theme or any
/// other preference writes the MCP section that was on disk, not the launch values; a token
/// from the environment is never written to disk that way. Changing MCP itself (in the settings
/// dialog or through a settings patch) is a deliberate choice: it is saved and ends the override.
/// </summary>
public sealed class SessionMcpUserSettingsStore(IUserSettingsStore inner) : IUserSettingsStore
{
    private readonly object _gate = new();
    private CadMcpUserSettings? _saved;
    private CadMcpUserSettings? _session;

    /// <summary>True while launch values are in effect and saves keep the stored MCP section.</summary>
    public bool HasSessionOverride
    {
        get { lock (_gate) return _session is not null; }
    }

    public CadUserSettings Load()
    {
        var settings = inner.Load();
        lock (_gate)
        {
            _saved = Copy(settings.Mcp);
            if (_session is { } session)
                settings.Mcp = Copy(session);
        }
        return settings;
    }

    /// <summary>Makes <paramref name="effective"/> the MCP section for this session only.</summary>
    public void UseForSession(CadMcpUserSettings effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        lock (_gate)
        {
            _saved ??= Copy(inner.Load().Mcp);
            _session = Copy(effective);
        }
    }

    public void Save(CadUserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            if (_session is { } session && Same(settings.Mcp, session))
            {
                var stored = settings.Clone();
                stored.Mcp = Copy(_saved!);
                inner.Save(stored);
                return;
            }
            inner.Save(settings);
            _saved = Copy(settings.Mcp);
            _session = null;
        }
    }

    private static bool Same(CadMcpUserSettings left, CadMcpUserSettings right) =>
        left.IsEnabled == right.IsEnabled && left.Port == right.Port &&
        string.Equals(left.AccessToken?.Trim() ?? string.Empty, right.AccessToken?.Trim() ?? string.Empty, StringComparison.Ordinal);

    private static CadMcpUserSettings Copy(CadMcpUserSettings source) =>
        new() { IsEnabled = source.IsEnabled, Port = source.Port, AccessToken = source.AccessToken };
}
