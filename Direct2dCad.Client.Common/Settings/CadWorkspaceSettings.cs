namespace Direct2dCad.Client.Common.Settings;

public sealed class CadWorkspaceSettings
{
    public int Version { get; set; } = 1;

    public Dictionary<string, CadDocumentWorkspaceSettings> Documents { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        Version = Math.Max(Version, 1);

        var normalizedDocuments = new Dictionary<string, CadDocumentWorkspaceSettings>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (path, settings) in Documents ?? [])
        {
            if (string.IsNullOrWhiteSpace(path) || settings is null)
                continue;

            settings.Normalize();
            normalizedDocuments[path] = settings;
        }

        Documents = normalizedDocuments;
    }
}

public sealed class CadDocumentWorkspaceSettings
{
    public HashSet<string> DisabledSelectionEntityTypes { get; set; }
        = new(StringComparer.Ordinal);

    public void Normalize()
    {
        DisabledSelectionEntityTypes = new HashSet<string>(
            (DisabledSelectionEntityTypes ?? [])
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim()),
            StringComparer.Ordinal);
    }

    public CadDocumentWorkspaceSettings Clone()
    {
        return new CadDocumentWorkspaceSettings
        {
            DisabledSelectionEntityTypes = new HashSet<string>(
                DisabledSelectionEntityTypes,
                StringComparer.Ordinal)
        };
    }
}
