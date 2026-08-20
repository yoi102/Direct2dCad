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
               TryParseDockZone(savedState.Zone, out var savedZone)
            ? savedZone
            : defaultZone;
        IsOpenByDefault = savedState?.IsOpen ?? isOpenByDefault;
    }

    public string ContentId { get; }

    private static bool TryParseDockZone(string? value, out DockZone zone)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse(value, ignoreCase: true, out zone) &&
            Enum.IsDefined(zone))
        {
            return true;
        }

        zone = default;
        return false;
    }
}
