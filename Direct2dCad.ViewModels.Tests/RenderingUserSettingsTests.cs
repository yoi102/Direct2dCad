using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Settings.UserSettings;

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
    public void MultiDeviceRenderingSettings_ArePreservedAndClamped()
    {
        var settings = CadUserSettings.CreateDefault();
        settings.Rendering.IsMultiDeviceRenderingEnabled = true;
        settings.Rendering.MultiDeviceRenderingDeviceCount = 9;

        settings.Normalize();
        var clone = settings.Clone();
        var viewModel = new RenderingUserSettingsViewModel(clone.Rendering);

        Assert.True(clone.Rendering.IsMultiDeviceRenderingEnabled);
        Assert.Equal(4, clone.Rendering.MultiDeviceRenderingDeviceCount);
        Assert.True(viewModel.IsMultiDeviceRenderingEnabled);
        Assert.Equal(4, viewModel.MultiDeviceRenderingDeviceCount);
    }
}
