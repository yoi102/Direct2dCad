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
}
