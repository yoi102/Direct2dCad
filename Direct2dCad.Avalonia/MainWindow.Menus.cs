using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Avalonia.Services;
using Direct2dCad.ViewModels;

namespace Direct2dCad.Avalonia;

public partial class MainWindow
{
    private void ApplicationIconClicked(object? sender, RoutedEventArgs e) => ShowApplicationMenu();
    private void ApplicationIconContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        ShowApplicationMenu(); e.Handled = true;
    }
    private void ShowApplicationMenu() => WindowsSystemMenu.Show(this, ApplicationIconButton.PointToScreen(new Point(0, ApplicationIconButton.Bounds.Height)));

    private ContextMenu CreateDocumentMenu(EditorTabViewModel document)
    {
        var menu = new ContextMenu();
        MenuItem Item(string key, System.Windows.Input.ICommand command, object? parameter = null, string? gesture = null)
        {
            var item = new MenuItem {Command=command,CommandParameter=parameter,InputGesture=gesture is null?null:KeyGesture.Parse(gesture)};
            Loc.Track(item, MenuItem.HeaderProperty, key); menu.Items.Add(item); return item;
        }
        Item("Save", document.SaveFileCommand, gesture:"Ctrl+S");
        Item("SaveAs", document.SaveAsFileCommand, gesture:"Ctrl+Shift+S");
        var folder = Item("OpenContainingFolder", _model.OpenFileFolderCommand, document.CurrentFilePath);
        menu.Items.Add(new Separator());
        Item("Close", _model.CloseEditorDocumentCommand, document);
        Item("CloseOtherDocuments", new AsyncRelayCommand(()=>CloseDocumentsAsync(document), ()=>_tabs.Count>1));
        Item("CloseAllDocuments", new AsyncRelayCommand(()=>CloseDocumentsAsync(null)));
        menu.Opening += (_,_) =>
        {
            _model.ActivateEditorDocumentCommand.Execute(document);
            folder.CommandParameter = document.CurrentFilePath;
            foreach(var item in menu.Items.OfType<MenuItem>())
                if(item.Command is IRelayCommand relay) relay.NotifyCanExecuteChanged();
        };
        return menu;
    }
    private async Task CloseDocumentsAsync(EditorTabViewModel? keep)
    {
        foreach(var document in _tabs.Keys.Where(document=>!ReferenceEquals(document,keep)).ToArray())
        {
            await _model.CloseEditorDocumentCommand.ExecuteAsync(document);
            // Cancel must also stop a batch close, leaving all remaining documents open.
            if(_tabs.ContainsKey(document)) break;
        }
    }
}
