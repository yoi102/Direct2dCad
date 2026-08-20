namespace Direct2dCad.Client.Common.Settings;

public sealed class CadToolboxLayoutSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public Dictionary<string, CadToolboxState> Toolboxes { get; set; }
        = new(StringComparer.Ordinal);

    public void Normalize()
    {
        Version = CurrentVersion;

        var normalizedToolboxes = new Dictionary<string, CadToolboxState>(StringComparer.Ordinal);
        foreach (var (contentId, state) in Toolboxes ?? [])
        {
            var normalizedContentId = contentId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedContentId) || state is null)
                continue;

            // JSON can contain whitespace-variant keys. The later entry wins so
            // a malformed settings file cannot prevent application startup.
            normalizedToolboxes[normalizedContentId] = state.Clone();
        }

        Toolboxes = normalizedToolboxes;
    }
}

public sealed class CadToolboxState
{
    public string Zone { get; set; } = string.Empty;
    public bool IsOpen { get; set; }

    public CadToolboxState Clone() => new()
    {
        Zone = Zone?.Trim() ?? string.Empty,
        IsOpen = IsOpen
    };
}
