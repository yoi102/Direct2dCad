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
        IsBackgroundChunkRecordingEnabled =
            settings.IsBackgroundChunkRecordingEnabled;
        IsMultiDeviceRenderingEnabled =
            settings.IsMultiDeviceRenderingEnabled;
        MultiDeviceRenderingDeviceCount =
            settings.MultiDeviceRenderingDeviceCount;
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

    [ObservableProperty]
    public partial bool IsBackgroundChunkRecordingEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsMultiDeviceRenderingEnabled { get; set; }

    [ObservableProperty]
    public partial int MultiDeviceRenderingDeviceCount { get; set; }

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
        settings.Rendering.IsBackgroundChunkRecordingEnabled =
            IsBackgroundChunkRecordingEnabled;
        settings.Rendering.IsMultiDeviceRenderingEnabled =
            IsMultiDeviceRenderingEnabled;
        settings.Rendering.MultiDeviceRenderingDeviceCount =
            Math.Clamp(MultiDeviceRenderingDeviceCount, 2, 4);
        return true;
    }

    internal override void ResetToDefaults()
    {
        Load(new CadRenderingUserSettings());
    }
}
