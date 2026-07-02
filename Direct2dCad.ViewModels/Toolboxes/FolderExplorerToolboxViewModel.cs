using AvalonDock.Core;
using AvalonDock.Mvvm.CommunityToolkit;
using Direct2dCad.ViewModels.Services;

namespace Direct2dCad.ViewModels.Toolboxes;

public class FolderExplorerToolboxViewModel : ObservableToolboxBase
{
    private readonly IToolboxIconsService _toolboxIconsService;

    public FolderExplorerToolboxViewModel(IToolboxIconsService toolboxIconsService)
    {
        Id = Guid.NewGuid().ToString();
        Title = "FolderExplorer";
        _toolboxIconsService = toolboxIconsService;
        Icon = toolboxIconsService.Explorer;
        Shortcut = "Ctrl+Shift+E";
        IsOpenByDefault = true;
    }




}
