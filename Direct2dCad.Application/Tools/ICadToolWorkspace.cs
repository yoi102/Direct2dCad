using Direct2dCad.Db.Cad;

namespace Direct2dCad.Application.Tools;

/// <summary>Host-owned document lifetime and long-operation boundary. No window or dock types cross this contract.</summary>
public interface ICadWorkspaceDocument
{
    ICadToolDocumentSession Session { get; }
    void Load(CadDocument document, string filePath);
    Task<T> RunAsync<T>(string message, Func<CancellationToken, Task<T>> operation, CancellationToken token = default);
}

public sealed record CadToolWorkspaceDocument(
    string DocumentId,
    Guid CadDocumentId,
    string Name,
    string FilePath,
    bool IsModified,
    bool IsActive,
    ICadWorkspaceDocument Host)
{
    public ICadToolDocumentSession Session => Host.Session;
}

public interface ICadToolWorkspace
{
    IReadOnlyList<CadToolWorkspaceDocument> GetDocuments();
    CadToolWorkspaceDocument? GetActiveDocument();
    CadToolWorkspaceDocument GetRequiredDocument(string documentId);
    CadToolWorkspaceDocument CreateDocument(string? name);
    Task<CadToolWorkspaceDocument> OpenDocumentAsync(string filePath, CancellationToken cancellationToken);
    bool ActivateDocument(string documentId);
    bool RenameDocument(string documentId, string name);
    Task<bool> SaveDocumentAsync(string documentId, string? filePath, CancellationToken cancellationToken);
    Task<bool> CloseDocumentAsync(string documentId);
    CadToolWorkspaceDocument CreateDocument(string? name, string? template) =>
        template is null ? CreateDocument(name) : throw new NotSupportedException("Engineering templates are unavailable in this host.");
    Task<bool> SaveDocumentAsync(string documentId, string? filePath, CadToolSaveOptions options, CancellationToken cancellationToken) =>
        options == new CadToolSaveOptions() ? SaveDocumentAsync(documentId, filePath, cancellationToken) :
            throw new NotSupportedException("Explicit save decisions are unavailable in this host.");
    Task<bool> CloseDocumentAsync(string documentId, CadToolCloseOptions options, CancellationToken cancellationToken) =>
        options == new CadToolCloseOptions() ? CloseDocumentAsync(documentId) :
            throw new NotSupportedException("Explicit close decisions are unavailable in this host.");
    IReadOnlyList<CadToolRecoveryEntry> ListRecoveryCopies() =>
        throw new NotSupportedException("Recovery copies are unavailable in this host.");
    Task<CadToolWorkspaceDocument> OpenRecoveryCopyAsync(string recoveryId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Recovery copies are unavailable in this host.");
    Task ClearRecoveryCopiesAsync(string? sourceDocumentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Recovery copies are unavailable in this host.");
    Task<bool> SaveCompatibleCopyAsync(string documentId, string filePath, CadToolSaveOptions options, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Compatible copies are unavailable in this host.");
    System.Text.Json.JsonElement GetApplicationSettings() =>
        throw new NotSupportedException("Application settings are unavailable in this host.");
    System.Text.Json.JsonElement SetApplicationSettings(System.Text.Json.JsonElement settings, bool reset) =>
        throw new NotSupportedException("Application settings are unavailable in this host.");
    bool SupportsViewCapture => false;
    bool SupportsPrinting => false;
    Task<CadToolImage> CaptureViewAsync(string documentId, int maximumSize, CancellationToken cancellationToken) =>
        throw new NotSupportedException("View capture is unavailable in this host.");
    Task<bool> PrintDocumentAsync(string documentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Printing is unavailable in this host.");
}

public sealed record CadToolImage(byte[] Data, string MimeType, int Width, int Height);

public sealed record CadToolSaveOptions(bool Overwrite = false, string? ExpectedSha256 = null, bool Interactive = true);
public sealed record CadToolCloseOptions(string OnUnsaved = "ask", string? FilePath = null, bool Overwrite = false, string? ExpectedSha256 = null);
public sealed record CadToolRecoveryEntry(string RecoveryId, string SourceDocumentId, string Name, string SourcePath,
    DateTimeOffset SavedAt, long DocumentVersion);
