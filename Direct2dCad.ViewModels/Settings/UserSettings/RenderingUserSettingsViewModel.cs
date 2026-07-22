using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Settings.UserSettings;

public partial class RenderingUserSettingsViewModel : UserSettingsSectionViewModel
{
    public RenderingUserSettingsViewModel(CadRenderingUserSettings settings)
        : base(Localized("Rendering"))
    {
        Load(settings);
    }

    private void Load(CadRenderingUserSettings settings)
    {
        IsAntialiasingEnabled = settings.IsAntialiasingEnabled;
        IsTextAntialiasingEnabled = settings.IsTextAntialiasingEnabled;
        ShowFramesPerSecond = settings.ShowFramesPerSecond;
        IsZoomSnapshotPreviewEnabled =
            settings.IsZoomSnapshotPreviewEnabled;
        IsLevelOfDetailEnabled = settings.IsLevelOfDetailEnabled;
        AllowApproximateTileScaleFallback = settings.AllowApproximateTileScaleFallback;
    }

    [ObservableProperty] public partial bool IsAntialiasingEnabled { get; set; }

    [ObservableProperty] public partial bool IsTextAntialiasingEnabled { get; set; }

    [ObservableProperty] public partial bool ShowFramesPerSecond { get; set; }

    [ObservableProperty]
    public partial bool IsZoomSnapshotPreviewEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsLevelOfDetailEnabled { get; set; }

    [ObservableProperty]
    public partial bool AllowApproximateTileScaleFallback { get; set; }

    internal override bool TryApplyTo(CadUserSettings settings)
    {
        settings.Rendering.IsAntialiasingEnabled = IsAntialiasingEnabled;
        settings.Rendering.IsTextAntialiasingEnabled = IsTextAntialiasingEnabled;
        settings.Rendering.ShowFramesPerSecond = ShowFramesPerSecond;
        settings.Rendering.IsZoomSnapshotPreviewEnabled =
            IsZoomSnapshotPreviewEnabled;
        settings.Rendering.IsLevelOfDetailEnabled = IsLevelOfDetailEnabled;
        settings.Rendering.AllowApproximateTileScaleFallback =
            AllowApproximateTileScaleFallback;
        return true;
    }

    internal override void ResetToDefaults()
    {
        Load(new CadRenderingUserSettings());
    }
}
