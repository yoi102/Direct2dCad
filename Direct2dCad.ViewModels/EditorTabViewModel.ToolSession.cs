using Direct2dCad.Application.Tools;

namespace Direct2dCad.ViewModels;

public partial class EditorTabViewModel : ICadWorkspaceDocument
{
    ICadToolDocumentSession ICadWorkspaceDocument.Session => CadDocumentViewModel;

    void ICadWorkspaceDocument.Load(Direct2dCad.Db.Cad.CadDocument document, string filePath) => Load(document, filePath);

    Task<T> ICadWorkspaceDocument.RunAsync<T>(string message,
        Func<CancellationToken, Task<T>> operation, CancellationToken token) =>
        Operation.RunAsync(message, operation, token);
}
