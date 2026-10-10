using Avalonia.Controls;
using Direct2dCad.Avalonia.Controls;
using Direct2dCad.ViewModels.Toolboxes;

namespace Direct2dCad.Avalonia.Views.Toolboxes;

public partial class DocumentsView : UserControl
{
    public DocumentsView()
    {
        InitializeComponent();
        var menu = new ContextMenu();
        var activate = CadListContextMenu.Item(menu, "ActivateDocument");
        var save = CadListContextMenu.Item(menu, "Save");
        var folder = CadListContextMenu.Item(menu, "OpenContainingFolder");
        menu.Items.Add(new Separator());
        var close = CadListContextMenu.Item(menu, "Close");
        var refresh = CadListContextMenu.Item(menu, "Refresh");
        CadListContextMenu.Attach(Documents, menu, selected =>
        {
            var item = selected as DocumentExplorerItemViewModel;
            CadListContextMenu.Target(activate, item?.Workspace?.ActivateEditorDocumentCommand, item?.Document);
            CadListContextMenu.Target(save, item?.Document.SaveFileCommand);
            CadListContextMenu.Target(folder, item?.Workspace?.OpenFileFolderCommand, item?.FilePath);
            CadListContextMenu.Target(close, item?.Workspace?.CloseEditorDocumentCommand, item?.Document);
            CadListContextMenu.Target(refresh, (DataContext as DocumentExplorerToolboxViewModel)?.RefreshCommand);
        });
    }
}
