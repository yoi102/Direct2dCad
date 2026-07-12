using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Core;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Services.Application;

public sealed class ToolboxLayoutPersistenceService
{
    private readonly IToolboxLayoutSettingsStore _toolboxSettingsStore;

    public ToolboxLayoutPersistenceService(IToolboxLayoutSettingsStore toolboxSettingsStore)
    {
        _toolboxSettingsStore = toolboxSettingsStore ??
                                throw new ArgumentNullException(nameof(toolboxSettingsStore));
    }

    public void Save(ToggleDockingManager dockingManager, IEnumerable<IDockable> anchorables)
    {
        ArgumentNullException.ThrowIfNull(dockingManager);
        ArgumentNullException.ThrowIfNull(anchorables);

        var toolboxes = anchorables.OfType<IToolbox>().ToArray();
        SynchronizeToolboxZones(dockingManager);
        foreach (var toolbox in toolboxes)
            toolbox.IsOpenByDefault = toolbox.IsOpen;

        try
        {
            _toolboxSettingsStore.Save(toolboxes.Select(toolbox =>
                new KeyValuePair<string, CadToolboxState>(
                    toolbox.Id ?? string.Empty,
                    new CadToolboxState
                    {
                        Zone = toolbox.Zone.ToString(),
                        IsOpen = toolbox.IsOpen
                    })));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Failed to save toolbox settings: {ex}");
        }
    }

    private static void SynchronizeToolboxZones(DependencyObject root)
    {
        foreach (var buttonBar in FindVisualChildren<ToggleDockButtonBar>(root))
        {
            foreach (var button in buttonBar.Items.OfType<ToggleDockButton>())
            {
                if (button.Anchorable?.Content is IToolbox toolbox)
                    toolbox.Zone = button.Zone;
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
