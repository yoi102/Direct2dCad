using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Settings.UserSettings;
using Direct2dCad.Rendering;

namespace Direct2dCad.ViewModels.Tests;

public sealed class RenderingUserSettingsTests
{
    [Fact]
    public void BackgroundChunkRecordingSetting_IsPreservedByCloneAndViewModel()
    {
        var settings = CadUserSettings.CreateDefault();
        settings.Rendering.IsBackgroundChunkRecordingEnabled = true;

        var clone = settings.Clone();
        var viewModel = new RenderingUserSettingsViewModel(clone.Rendering);

        Assert.True(clone.Rendering.IsBackgroundChunkRecordingEnabled);
        Assert.True(viewModel.IsBackgroundChunkRecordingEnabled);
    }

    [Fact]
    public void ParallelRenderingSettings_ArePreservedAndClamped()
    {
        var settings = CadUserSettings.CreateDefault();
        settings.Rendering.IsParallelRenderingEnabled = true;
        settings.Rendering.ParallelRenderingMode =
            CadParallelRenderingMode.SharedDeviceContexts;
        settings.Rendering.ParallelRenderingWorkerCount = 9;

        settings.Normalize();
        var clone = settings.Clone();
        var viewModel = new RenderingUserSettingsViewModel(clone.Rendering);

        Assert.True(clone.Rendering.IsParallelRenderingEnabled);
        Assert.Equal(
            CadParallelRenderingMode.SharedDeviceContexts,
            clone.Rendering.ParallelRenderingMode);
        Assert.Equal(4, clone.Rendering.ParallelRenderingWorkerCount);
        Assert.True(viewModel.IsParallelRenderingEnabled);
        Assert.Equal(
            CadParallelRenderingMode.SharedDeviceContexts,
            viewModel.SelectedParallelRenderingMode?.Mode);
        Assert.Equal(4, viewModel.ParallelRenderingWorkerCount);
    }
}
