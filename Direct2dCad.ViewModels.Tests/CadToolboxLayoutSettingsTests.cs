using AvalonDock.Core;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Toolboxes;

namespace Direct2dCad.ViewModels.Tests;

public sealed class CadToolboxLayoutSettingsTests
{
    [Fact]
    public void Normalize_CollapsesWhitespaceEquivalentContentIds_LastStateWins()
    {
        var settings = new CadToolboxLayoutSettings
        {
            Version = 99,
            Toolboxes = new Dictionary<string, CadToolboxState>
            {
                [" toolbox.blocks "] = new() { Zone = "LeftTop", IsOpen = true },
                ["toolbox.blocks"] = new() { Zone = "BottomLeft", IsOpen = false },
                [" "] = new() { Zone = "RightTop", IsOpen = true }
            }
        };

        settings.Normalize();

        Assert.Equal(CadToolboxLayoutSettings.CurrentVersion, settings.Version);
        var state = Assert.Single(settings.Toolboxes);
        Assert.Equal("toolbox.blocks", state.Key);
        Assert.Equal("BottomLeft", state.Value.Zone);
        Assert.False(state.Value.IsOpen);
    }

    [Fact]
    public void ToolboxBase_RestoresSavedZoneAndOpenState()
    {
        var toolbox = new TestToolbox(
            new InMemoryStore(new CadToolboxState { Zone = "RightBottom", IsOpen = true }),
            DockZone.LeftTop,
            isOpenByDefault: false);

        Assert.Equal(DockZone.RightBottom, toolbox.Zone);
        Assert.True(toolbox.IsOpenByDefault);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("not-a-zone")]
    [InlineData("")]
    public void ToolboxBase_InvalidSavedZone_FallsBackToDefault(string savedZone)
    {
        var toolbox = new TestToolbox(
            new InMemoryStore(new CadToolboxState { Zone = savedZone, IsOpen = true }),
            DockZone.LeftBottom,
            isOpenByDefault: false);

        Assert.Equal(DockZone.LeftBottom, toolbox.Zone);
        Assert.True(toolbox.IsOpenByDefault);
    }

    private sealed class TestToolbox : CadToolboxViewModelBase
    {
        public TestToolbox(
            IToolboxLayoutSettingsStore settingsStore,
            DockZone defaultZone,
            bool isOpenByDefault)
            : base(settingsStore, "toolbox.test", defaultZone, isOpenByDefault)
        {
        }
    }

    private sealed class InMemoryStore : IToolboxLayoutSettingsStore
    {
        private readonly CadToolboxState? _state;

        public InMemoryStore(CadToolboxState? state)
        {
            _state = state;
        }

        public CadToolboxState? Load(string contentId) => _state?.Clone();

        public void Save(IEnumerable<KeyValuePair<string, CadToolboxState>> toolboxes)
        {
        }
    }
}
