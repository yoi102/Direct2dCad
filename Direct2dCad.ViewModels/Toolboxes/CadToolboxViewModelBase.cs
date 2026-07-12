using AvalonDock.Core;
using AvalonDock.Mvvm.CommunityToolkit;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Toolboxes;

public abstract class CadToolboxViewModelBase : ObservableToolboxBase
{
    protected CadToolboxViewModelBase(
        IToolboxLayoutSettingsStore layoutSettingsStore,
        string contentId,
        DockZone defaultZone,
        bool isOpenByDefault)
    {
        ArgumentNullException.ThrowIfNull(layoutSettingsStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentId);

        ContentId = Id = contentId;
        var savedState = layoutSettingsStore.Load(contentId);
        Zone = savedState is not null &&
               Enum.TryParse(savedState.Zone, ignoreCase: true, out DockZone savedZone)
            ? savedZone
            : defaultZone;
        IsOpenByDefault = savedState?.IsOpen ?? isOpenByDefault;
    }

    public string ContentId { get; }
}
