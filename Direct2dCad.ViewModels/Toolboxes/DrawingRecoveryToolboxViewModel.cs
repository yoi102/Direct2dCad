using AvalonDock.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Lang;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class DrawingRecoveryToolboxViewModel : CadToolboxViewModelBase
{
    public DrawingRecoveryToolboxViewModel(
        IToolboxLayoutSettingsStore settingsStore,
        IToolboxIconProvider iconProvider)
        : base(settingsStore, "toolbox.drawing-assistant", DockZone.RightTop, isOpenByDefault: true)
    {
        RefreshTitle();
        Icon = iconProvider.Recovery;
        Shortcut = "Ctrl+Shift+D";
        CanClose = false;
    }

    // Bind through the workspace so the same view also works in a floating dock.
    // The workspace owns the active document and all long-operation lifetimes.
    [ObservableProperty]
    public partial MainViewModel? Workspace { get; internal set; }

    internal void RefreshTitle() => Title = CadUiText.Get(LangKeys.DrawingRecovery);
}
