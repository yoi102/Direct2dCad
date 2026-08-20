using System.Windows;
using AvalonDock;
using AvalonDock.Core;
using AvalonDock.Layout;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Toolboxes;
using Direct2dCad.wpf.Services.Application;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class ToolboxLayoutPersistenceIntegrationTests
{
    [Fact]
    public void SaveAndRestore_PreservesToolboxOrderAndDockSize()
    {
        RunSta(() =>
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"Direct2dCad-toolbox-layout-{Guid.NewGuid():N}");
            var layoutPath = Path.Combine(directory, "toolbox-layout.json");

            try
            {
                var store = new InMemoryToolboxLayoutSettingsStore();
                var service = new ToolboxLayoutPersistenceService(store, layoutPath);
                var first = new TestToolbox(store, "toolbox.first", DockZone.LeftTop, true);
                var second = new TestToolbox(store, "toolbox.second", DockZone.LeftTop, false);
                var source = new ToggleDockingManager
                {
                    Layout = CreateLayout(first, second, dockWidth: 347)
                };

                service.Save(source, [first, second]);

                var target = new ToggleDockingManager
                {
                    Layout = CreateLayout(second, first, dockWidth: 180)
                };

                Assert.True(service.Restore(target, [first, second]));

                var restoredPane = target.Layout.Descendents()
                    .OfType<LayoutAnchorablePane>()
                    .Single();
                Assert.Equal(347, restoredPane.DockWidth.Value);
                Assert.Equal(
                    ["toolbox.first", "toolbox.second"],
                    restoredPane.Children.Select(child => child.ContentId));
                Assert.Same(first, restoredPane.Children[0].Content);
                Assert.Same(second, restoredPane.Children[1].Content);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        });
    }

    [Fact]
    public void Restore_InvalidLayout_KeepsCurrentLayout()
    {
        RunSta(() =>
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                $"Direct2dCad-toolbox-layout-{Guid.NewGuid():N}");
            var layoutPath = Path.Combine(directory, "toolbox-layout.json");

            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(layoutPath, "{ invalid layout }");

                var store = new InMemoryToolboxLayoutSettingsStore();
                var toolbox = new TestToolbox(store, "toolbox.current", DockZone.RightTop, true);
                var manager = new ToggleDockingManager
                {
                    Layout = CreateSingleToolboxLayout(toolbox, dockWidth: 240)
                };
                var currentLayout = manager.Layout;
                var service = new ToolboxLayoutPersistenceService(store, layoutPath);

                Assert.False(service.Restore(manager, [toolbox]));
                Assert.Same(currentLayout, manager.Layout);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        });
    }

    private static LayoutRoot CreateLayout(
        IToolbox first,
        IToolbox second,
        double dockWidth)
    {
        var pane = new LayoutAnchorablePane
        {
            DockWidth = new GridLength(dockWidth)
        };
        pane.Children.Add(CreateAnchorable(first));
        pane.Children.Add(CreateAnchorable(second));

        var rootPanel = new LayoutPanel(
            new LayoutDocumentPaneGroup(new LayoutDocumentPane()));
        rootPanel.Children.Add(new LayoutAnchorablePaneGroup(pane));
        return new LayoutRoot { RootPanel = rootPanel };
    }

    private static LayoutRoot CreateSingleToolboxLayout(IToolbox toolbox, double dockWidth)
    {
        var pane = new LayoutAnchorablePane
        {
            DockWidth = new GridLength(dockWidth)
        };
        pane.Children.Add(CreateAnchorable(toolbox));

        var rootPanel = new LayoutPanel(
            new LayoutDocumentPaneGroup(new LayoutDocumentPane()));
        rootPanel.Children.Add(new LayoutAnchorablePaneGroup(pane));
        return new LayoutRoot { RootPanel = rootPanel };
    }

    private static LayoutAnchorable CreateAnchorable(IToolbox toolbox) => new()
    {
        Title = toolbox.Title,
        ContentId = toolbox.Id,
        Content = toolbox
    };

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw failure;
    }

    private sealed class TestToolbox : CadToolboxViewModelBase
    {
        public TestToolbox(
            IToolboxLayoutSettingsStore settingsStore,
            string id,
            DockZone zone,
            bool isOpenByDefault)
            : base(settingsStore, id, zone, isOpenByDefault)
        {
            Title = id;
        }
    }

    private sealed class InMemoryToolboxLayoutSettingsStore : IToolboxLayoutSettingsStore
    {
        private Dictionary<string, CadToolboxState> _states = new(StringComparer.Ordinal);

        public CadToolboxState? Load(string contentId) =>
            _states.TryGetValue(contentId, out var state)
                ? state.Clone()
                : null;

        public void Save(IEnumerable<KeyValuePair<string, CadToolboxState>> toolboxes)
        {
            _states = toolboxes.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Clone(),
                StringComparer.Ordinal);
        }
    }
}
