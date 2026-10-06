namespace Direct2dCad.ViewModels.Tests;

internal static class ToolWorkspaceTestExtensions
{
    public static CadDocumentViewModel GetViewModel(this CadToolWorkspaceDocument document) => (CadDocumentViewModel)document.Session;
}
