namespace Direct2dCad.Client.Common.Settings;

public sealed class CadToolboxLayoutSettings
{
    public int Version { get; set; } = 1;

    public Dictionary<string, CadToolboxState> Toolboxes { get; set; }
        = new(StringComparer.Ordinal);

    public void Normalize()
    {
        Version = Math.Max(Version, 1);
        Toolboxes = new Dictionary<string, CadToolboxState>(
            (Toolboxes ?? [])
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
                .Select(pair => new KeyValuePair<string, CadToolboxState>(
                    pair.Key.Trim(),
                    pair.Value.Clone())),
            StringComparer.Ordinal);
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
