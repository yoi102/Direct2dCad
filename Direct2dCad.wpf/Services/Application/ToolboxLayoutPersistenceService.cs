using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Controls;
using AvalonDock.Core;
using AvalonDock.Core.Serialization;
using AvalonDock.Serializer.Json;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Services.Application;

public sealed class ToolboxLayoutPersistenceService
{
    private readonly IToolboxLayoutSettingsStore _toolboxSettingsStore;
    private readonly string _layoutFilePath;

    public ToolboxLayoutPersistenceService(IToolboxLayoutSettingsStore toolboxSettingsStore)
        : this(
            toolboxSettingsStore,
            ApplicationSettingsPathResolver.Resolve("toolbox-layout.json"))
    {
    }

    internal ToolboxLayoutPersistenceService(
        IToolboxLayoutSettingsStore toolboxSettingsStore,
        string layoutFilePath)
    {
        _toolboxSettingsStore = toolboxSettingsStore ??
                                throw new ArgumentNullException(nameof(toolboxSettingsStore));
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutFilePath);
        _layoutFilePath = layoutFilePath;
    }

    public void Save(ToggleDockingManager dockingManager, IEnumerable<IDockable> anchorables)
    {
        ArgumentNullException.ThrowIfNull(dockingManager);
        ArgumentNullException.ThrowIfNull(anchorables);

        try
        {
            var toolboxes = CollectToolboxes(anchorables).Values.ToArray();
            SynchronizeToolboxZones(dockingManager);
            foreach (var toolbox in toolboxes)
                toolbox.IsOpenByDefault = toolbox.IsOpen;

            _toolboxSettingsStore.Save(toolboxes.Select(toolbox =>
                new KeyValuePair<string, CadToolboxState>(
                    toolbox.Id!,
                    new CadToolboxState
                    {
                        Zone = toolbox.Zone.ToString(),
                        IsOpen = toolbox.IsOpen
                    })));

            SaveDockLayout(dockingManager);
        }
        catch (Exception ex) when (IsPersistenceException(ex))
        {
            Debug.WriteLine($"Failed to save toolbox settings: {ex}");
        }
    }

    public bool Restore(ToggleDockingManager dockingManager, IEnumerable<IDockable> anchorables)
    {
        ArgumentNullException.ThrowIfNull(dockingManager);
        ArgumentNullException.ThrowIfNull(anchorables);

        if (!File.Exists(_layoutFilePath))
            return false;

        try
        {
            var toolboxes = CollectToolboxes(anchorables);
            using var stream = new FileStream(
                _layoutFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            var serializer = new JsonLayoutSerializer(dockingManager);
            serializer.LayoutSerializationCallback += (_, args) =>
                RestoreLayoutContent(args, toolboxes);
            serializer.Deserialize(stream);

            SynchronizeToolboxZones(dockingManager);
            return true;
        }
        catch (Exception ex) when (IsPersistenceException(ex))
        {
            Debug.WriteLine($"Failed to restore toolbox layout: {ex}");
            return false;
        }
    }

    private void SaveDockLayout(ToggleDockingManager dockingManager)
    {
        var directory = Path.GetDirectoryName(_layoutFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryFilePath = $"{_layoutFilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryFilePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                new JsonLayoutSerializer(dockingManager).Serialize(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryFilePath, _layoutFilePath, overwrite: true);
        }
        finally
        {
            TryDeleteTemporaryFile(temporaryFilePath);
        }
    }

    private static void RestoreLayoutContent(
        LayoutSerializationCallbackEventArgs args,
        IReadOnlyDictionary<string, IToolbox> toolboxes)
    {
        if (args.Model is ISerializableLayoutAnchorable)
        {
            var contentId = args.Model.ContentId?.Trim();
            if (!string.IsNullOrWhiteSpace(contentId) &&
                toolboxes.TryGetValue(contentId, out var toolbox))
            {
                args.Content = toolbox;
            }
            else
            {
                args.Cancel = true;
            }

            return;
        }

        // Keep only documents that already exist in the startup layout, such as Welcome.
        // Stored editor tabs are restored by document services, not by the toolbox layout.
        if (args.Model is ISerializableLayoutDocument && args.Content is null)
            args.Cancel = true;
    }

    private static Dictionary<string, IToolbox> CollectToolboxes(
        IEnumerable<IDockable> anchorables)
    {
        var toolboxes = new Dictionary<string, IToolbox>(StringComparer.Ordinal);
        foreach (var toolbox in anchorables.OfType<IToolbox>())
        {
            var contentId = toolbox.Id?.Trim();
            if (!string.IsNullOrWhiteSpace(contentId))
                toolboxes[contentId] = toolbox;
        }

        return toolboxes;
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Failed to delete temporary toolbox layout file: {ex}");
        }
    }

    private static bool IsPersistenceException(Exception exception) =>
        exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            InvalidOperationException or
            NotSupportedException or
            System.Security.SecurityException or
            JsonException;

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
