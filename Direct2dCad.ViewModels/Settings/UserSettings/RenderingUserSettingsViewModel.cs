using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Lang.Strings;
using Direct2dCad.Rendering;

namespace Direct2dCad.ViewModels.Settings.UserSettings;

public sealed record ParallelRenderingModeOption(
    CadParallelRenderingMode Mode,
    string DisplayName);

public partial class RenderingUserSettingsViewModel : UserSettingsSectionViewModel
{
    public RenderingUserSettingsViewModel(CadRenderingUserSettings settings)
        : base(Localized("Rendering"))
    {
        ParallelRenderingModeOptions =
        [
            new(
                CadParallelRenderingMode.MultipleDevices,
                Localized("ParallelRenderingMultipleDevices")),
            new(
                CadParallelRenderingMode.SharedDeviceContexts,
                Localized("ParallelRenderingSharedDeviceContexts"))
        ];
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
        IsParallelRenderingEnabled = settings.IsParallelRenderingEnabled;
        SelectedParallelRenderingMode =
            ParallelRenderingModeOptions.First(option =>
                option.Mode == settings.ParallelRenderingMode);
        ParallelRenderingWorkerCount = settings.ParallelRenderingWorkerCount;
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
    public partial bool IsParallelRenderingEnabled { get; set; }

    [ObservableProperty]
    public partial ParallelRenderingModeOption? SelectedParallelRenderingMode { get; set; }

    [ObservableProperty]
    public partial int ParallelRenderingWorkerCount { get; set; }

    public IReadOnlyList<ParallelRenderingModeOption> ParallelRenderingModeOptions { get; }

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
        settings.Rendering.IsParallelRenderingEnabled =
            IsParallelRenderingEnabled;
        settings.Rendering.ParallelRenderingMode =
            SelectedParallelRenderingMode?.Mode ??
            CadParallelRenderingMode.MultipleDevices;
        settings.Rendering.ParallelRenderingWorkerCount =
            Math.Clamp(ParallelRenderingWorkerCount, 2, 4);
        return true;
    }

    internal override void ResetToDefaults()
    {
        Load(new CadRenderingUserSettings());
    }
}
