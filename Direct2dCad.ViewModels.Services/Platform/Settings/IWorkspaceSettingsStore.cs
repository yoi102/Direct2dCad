using Direct2dCad.Client.Common.Settings;

namespace Direct2dCad.ViewModels.Services.Platform;

public interface IWorkspaceSettingsStore
{
    CadDocumentWorkspaceSettings LoadDocument(string documentFilePath);

    void SaveDocument(string documentFilePath, CadDocumentWorkspaceSettings settings);
}
