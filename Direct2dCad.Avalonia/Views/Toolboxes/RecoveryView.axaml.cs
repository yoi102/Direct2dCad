using Avalonia.Controls;
using Avalonia.Interactivity;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.ViewModels.Services.Documents;
using Direct2dCad.Avalonia.Controls;
namespace Direct2dCad.Avalonia.Views.Toolboxes;
public partial class RecoveryView : UserControl
{
    public RecoveryView()
    {
        InitializeComponent();
        var menu = new ContextMenu();
        var folder = CadListContextMenu.Item(menu, "OpenContainingFolder");
        var open = CadListContextMenu.Item(menu, "OpenRecoveryCopy");
        var source = CadListContextMenu.Item(menu, "OpenSourceFolder");
        menu.Items.Add(new Separator());
        var clear = CadListContextMenu.Item(menu, "ClearRecoveryEntry");
        var clearAll = CadListContextMenu.Item(menu, "ClearAllRecovery");
        CadListContextMenu.Attach(Entries, menu, selected =>
        {
            var entry = selected as CadRecoveryEntry;
            var workspace = (DataContext as DrawingRecoveryToolboxViewModel)?.Workspace;
            CadListContextMenu.Target(folder, entry is null ? null : workspace?.OpenRecoveryFolderCommand, entry);
            CadListContextMenu.Target(open, entry is null ? null : workspace?.OpenRecoveryCopyCommand, entry);
            CadListContextMenu.Target(source, entry is null ? null : workspace?.OpenFileFolderCommand, entry?.SourcePath);
            CadListContextMenu.Target(clear, entry is null ? null : workspace?.ClearRecoveryEntryCommand, entry);
            CadListContextMenu.Target(clearAll, workspace?.ClearAllRecoveryCommand);
        });
    }
    private void OpenCopy(object? sender, RoutedEventArgs e) { if (DataContext is DrawingRecoveryToolboxViewModel { Workspace: { } vm } && sender is Control { DataContext: CadRecoveryEntry entry }) vm.OpenRecoveryCopyCommand.Execute(entry); }
    private void OpenFolder(object? sender, RoutedEventArgs e) { if (DataContext is DrawingRecoveryToolboxViewModel { Workspace: { } vm } && sender is Control { DataContext: CadRecoveryEntry entry }) vm.OpenRecoveryFolderCommand.Execute(entry); }
    private void OpenSourceFolder(object? sender,RoutedEventArgs e) { if(DataContext is DrawingRecoveryToolboxViewModel {Workspace: {} vm} && sender is Control {DataContext: CadRecoveryEntry entry}) vm.OpenFileFolderCommand.Execute(entry.SourcePath); }
    private void ClearEntry(object? sender, RoutedEventArgs e) { if (DataContext is DrawingRecoveryToolboxViewModel { Workspace: { } vm } && sender is Control { DataContext: CadRecoveryEntry entry }) vm.ClearRecoveryEntryCommand.Execute(entry); }
}
