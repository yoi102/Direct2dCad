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
    bool SupportsViewCapture => false;
    bool SupportsPrinting => false;
    Task<CadToolImage> CaptureViewAsync(string documentId, int maximumSize, CancellationToken cancellationToken) =>
        throw new NotSupportedException("View capture is unavailable in this host.");
    Task<bool> PrintDocumentAsync(string documentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Printing is unavailable in this host.");
}

public sealed record CadToolImage(byte[] Data, string MimeType, int Width, int Height);
