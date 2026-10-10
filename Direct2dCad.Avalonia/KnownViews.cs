using Avalonia.Controls;
using Direct2dCad.ViewModels.Toolboxes;
namespace Direct2dCad.Avalonia;
internal static class KnownViews
{
    public static Control? Create(object? model) => model switch
    {
        DocumentExplorerToolboxViewModel vm => new Views.Toolboxes.DocumentsView { DataContext = vm },
        LayersToolboxViewModel vm => new Views.Toolboxes.LayersView { DataContext = vm },
        BlocksToolboxViewModel vm => new Views.Toolboxes.BlocksView { DataContext = vm },
        CommandLineToolboxViewModel vm => new Views.Toolboxes.TerminalView { DataContext = vm },
        DrawingRecoveryToolboxViewModel vm => new Views.Toolboxes.RecoveryView { DataContext = vm },
        AiAssistantToolboxViewModel vm => new Views.Toolboxes.AiView { DataContext = vm },
        EntitySearchToolboxViewModel vm => new Views.Toolboxes.SearchView { DataContext = vm },
        SelectionFilterToolboxViewModel vm => new Views.Toolboxes.FilterView { DataContext = vm },
        MessageToolboxViewModel vm => new Views.Toolboxes.MessagesView { DataContext = vm },
        Direct2dCad.ViewModels.Settings.UserSettings.UserSettingsViewModel vm => Views.SettingsWorkspace.Create(vm),
        Direct2dCad.ViewModels.Settings.DocumentSettingsViewModel vm => Views.SettingsWorkspace.Create(vm),
        Direct2dCad.ViewModels.Settings.UserSettings.InteractionUserSettingsViewModel vm => new Views.InteractionSettingsView { DataContext = vm },
        _ => GeneratedViews.Create(model)
    };
}
