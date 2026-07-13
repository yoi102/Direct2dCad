using System.Collections.ObjectModel;
using AvalonDock.Core;
using AvalonDock.Mvvm.CommunityToolkit;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Lang;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using MessagePipe;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class LayersToolboxViewModel : CadToolboxViewModelBase, IDisposable
{
    private CadDocumentViewModel? _documentViewModel;
    private readonly IDisposable _interactionStateChangedSubscription;
    private readonly IDialogService _dialogService;
    private readonly ISnackbarService _snackbarService;

    public LayersToolboxViewModel(
        IToolboxLayoutSettingsStore toolboxLayoutSettingsStore,
        IDialogService dialogService,
        ISnackbarService snackbarService,
        IToolboxIconProvider toolboxIconProvider,
        ISubscriber<CadDocumentInteractionStateChangedMessage> interactionStateChangedSubscriber)
        : base(toolboxLayoutSettingsStore, "toolbox.layers", DockZone.BottomLeft, isOpenByDefault: true)
    {
        Title = Strings.Layers;
        _interactionStateChangedSubscription = interactionStateChangedSubscriber.Subscribe(OnInteractionStateChanged);
        Icon = toolboxIconProvider.Layers;
        Shortcut = "Ctrl+Shift+L";
        _dialogService = dialogService;
        _snackbarService = snackbarService;
        CanClose = false;
    }
    public ObservableCollection<LayerItemViewModel> Layers { get; } = [];

    public bool HasDocument => _documentViewModel is not null;

    public bool HasLayers => Layers.Count > 0;

    [ObservableProperty]
    public partial LayerItemViewModel? SelectedLayer { get; set; }

    public void Attach(CadDocumentViewModel? documentViewModel)
    {
        if (ReferenceEquals(_documentViewModel, documentViewModel))
        {
            RefreshLayers();
            return;
        }

        _documentViewModel = documentViewModel;

        OnPropertyChanged(nameof(HasDocument));
        RefreshLayers();
    }

    public void MoveLayer(LayerItemViewModel layer, int insertIndex)
    {
        if (_documentViewModel is null)
            return;

        var oldIndex = Layers.IndexOf(layer);
        if (oldIndex < 0)
            return;

        var ordered = Layers.ToList();
        ordered.RemoveAt(oldIndex);

        if (insertIndex > oldIndex)
            insertIndex--;

        insertIndex = Math.Clamp(insertIndex, 0, ordered.Count);
        if (oldIndex == insertIndex)
            return;

        ordered.Insert(insertIndex, layer);
        ApplyLayerOrder(ordered);
        RefreshLayers(layer.LayerId);
    }

    internal void RenameLayer(LayerItemViewModel layer, string name)
    {
        if (_documentViewModel is null)
            return;

        if (string.IsNullOrWhiteSpace(name))
        {
            RefreshLayers(layer.LayerId);
            return;
        }

        var currentLayer = _documentViewModel.CadEditor.Document.GetLayer(layer.LayerId);
        if (string.Equals(currentLayer.Name, name.Trim(), StringComparison.Ordinal))
            return;

        if (_documentViewModel.CadEditor.Document.Layers.Values.Any(x =>
                !x.Id.Equals(layer.LayerId) &&
                string.Equals(x.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            _snackbarService.Enqueue(Strings.LayerNameAlreadyExists);
            RefreshLayers(layer.LayerId);
            return;
        }

        ExecuteAndRefresh(
            () => _documentViewModel.CadEditor.RenameLayer(layer.LayerId, name),
            layer.LayerId);
    }

    internal void SetLayerState(LayerItemViewModel layer)
    {
        if (_documentViewModel is null)
            return;

        ExecuteAndRefresh(
            () => _documentViewModel.CadEditor.SetLayerState(
                layer.LayerId,
                layer.IsVisible,
                layer.IsLocked,
                layer.IsFrozen),
            layer.LayerId);
    }

    internal void SetLayerAppearance(LayerItemViewModel layer)
    {
        if (_documentViewModel is null)
            return;

        var lineWeight = ResolveLayerLineWeight(layer.LineWeight);
        var currentLayer = _documentViewModel.CadEditor.Document.GetLayer(layer.LayerId);
        if (currentLayer.Color == layer.Color && currentLayer.LineWeight == lineWeight)
            return;

        ExecuteAndRefresh(
            () => _documentViewModel.CadEditor.SetLayerAppearance(
                layer.LayerId,
                layer.Color,
                lineWeight),
            layer.LayerId);
        _documentViewModel.UpdateDrawingDefaultsForLayerAppearance(
            layer.LayerId,
            layer.Color,
            lineWeight);
    }

    partial void OnSelectedLayerChanged(LayerItemViewModel? value)
    {
        DeleteSelectedLayerCommand.NotifyCanExecuteChanged();
        MoveSelectedLayerUpCommand.NotifyCanExecuteChanged();
        MoveSelectedLayerDownCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Refresh()
    {
        RefreshLayers(SelectedLayer?.LayerId);
    }

    [RelayCommand(CanExecute = nameof(CanAddLayer))]
    private void AddLayer()
    {
        if (_documentViewModel is null)
            return;

        var document = _documentViewModel.CadEditor.Document;
        var name = CreateUniqueLayerName(document);
        var priority = Layers.Count == 0 ? 0 : Layers.Min(x => x.Priority) - 1;
        var layerId = _documentViewModel.CadEditor.CreateLayer(
            name,
            CadColor.Green,
            CadLineWeight.Default,
            drawingPriority: priority);

        RefreshLayers(layerId);
    }

    private bool CanAddLayer()
    {
        return _documentViewModel is not null;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedLayer))]
    private async Task DeleteSelectedLayer()
    {
        if (_documentViewModel is null || SelectedLayer is not { } layer || Layers.Count < 1)
            return;

        var message = string.Format(
           Strings.LayerDeleteConfirmMessageFormat,
           layer.Name,
           Environment.NewLine);

        var deleteConfirmed = await _dialogService.ShowOrReplaceMessageDialogWithCancelAsync(
            message,
            Strings.LayerDeleteConfirmTitle,
            ViewServiceIdentifiers.RootDialogHost);

        if (!deleteConfirmed)
            return;

        var fallbackSelection = Layers.FirstOrDefault(x => !ReferenceEquals(x, layer))?.LayerId;
        ExecuteAndRefresh(
            () => _documentViewModel.CadEditor.DeleteLayer(layer.LayerId),
            fallbackSelection);
    }

    private bool CanDeleteSelectedLayer()
    {
        return Layers.Count > 1;
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelectedLayerUp))]
    private void MoveSelectedLayerUp()
    {
        if (SelectedLayer is null)
            return;

        MoveLayer(SelectedLayer, Math.Max(0, Layers.IndexOf(SelectedLayer) - 1));
    }

    private bool CanMoveSelectedLayerUp()
    {
        return SelectedLayer is { } selected && Layers.IndexOf(selected) > 0;
    }

    [RelayCommand(CanExecute = nameof(CanMoveSelectedLayerDown))]
    private void MoveSelectedLayerDown()
    {
        if (SelectedLayer is null)
            return;

        MoveLayer(SelectedLayer, Layers.IndexOf(SelectedLayer) + 2);
    }

    private bool CanMoveSelectedLayerDown()
    {
        return SelectedLayer is { } selected &&
               Layers.IndexOf(selected) >= 0 &&
               Layers.IndexOf(selected) < Layers.Count - 1;
    }

    private void OnInteractionStateChanged(CadDocumentInteractionStateChangedMessage message)
    {
        if (!ReferenceEquals(message.DocumentViewModel, _documentViewModel))
            return;

        RefreshLayers(SelectedLayer?.LayerId);
    }

    public void Dispose()
    {
        _interactionStateChangedSubscription.Dispose();
    }

    private void RefreshLayers(LayerId? selectedLayerId = null)
    {
        Layers.Clear();

        if (_documentViewModel is not null)
        {
            var document = _documentViewModel.CadEditor.Document;
            foreach (var layer in document.Layers.Values
                         .OrderByDescending(x => document.DocumentSettings.LayerDrawingPriority.GetPriority(x.Id))
                         .ThenByDescending(x => x.Id.Value))
            {
                Layers.Add(new LayerItemViewModel(
                    this,
                    layer,
                    document.DocumentSettings.LayerDrawingPriority.GetPriority(layer.Id),
                    document.GetEntityIdsOnLayer(layer.Id).Count));
            }
        }

        OnPropertyChanged(nameof(HasLayers));
        SelectedLayer = selectedLayerId is { } layerId
            ? Layers.FirstOrDefault(x => x.LayerId.Equals(layerId)) ?? Layers.FirstOrDefault()
            : Layers.FirstOrDefault();

        AddLayerCommand.NotifyCanExecuteChanged();
        DeleteSelectedLayerCommand.NotifyCanExecuteChanged();
        MoveSelectedLayerUpCommand.NotifyCanExecuteChanged();
        MoveSelectedLayerDownCommand.NotifyCanExecuteChanged();
    }

    private void ApplyLayerOrder(IReadOnlyList<LayerItemViewModel> orderedLayers)
    {
        if (_documentViewModel is null)
            return;

        var priorities = orderedLayers
            .Select((layer, index) => new { layer.LayerId, Priority = orderedLayers.Count - index - 1 })
            .ToDictionary(x => x.LayerId, x => x.Priority);
        _documentViewModel.CadEditor.SetLayerDrawingPriorities(priorities);
    }

    private void ExecuteAndRefresh(Action execute, LayerId? selectedLayerId)
    {
        try
        {
            execute();
        }
        finally
        {
            RefreshLayers(selectedLayerId);
        }
    }

    private static string CreateUniqueLayerName(CadDocument document)
    {
        for (var index = 1; ; index++)
        {
            var name = $"Layer {index}";
            if (document.Layers.Values.All(x => !string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                return name;
        }
    }

    private static CadLineWeight ResolveLayerLineWeight(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value)
            ? new CadLineWeight(value)
            : CadLineWeight.Default;
    }
}

public sealed partial class LayerItemViewModel : ObservableObject
{
    private readonly LayersToolboxViewModel _owner;
    private bool _isRefreshing;

    public LayerItemViewModel(
        LayersToolboxViewModel owner,
        CadLayer layer,
        int priority,
        int entityCount)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        LayerId = layer.Id;
        RefreshFromLayer(layer, priority, entityCount);
    }

    public LayerId LayerId { get; }

    public string LayerIdText => LayerId.Value.ToString();

    public bool IsDefaultLayer => LayerId.Equals(LayerId.Default);

    public string ToolTipText => $"Id: {LayerIdText}, Entities: {EntityCount}";

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial bool IsLocked { get; set; }

    [ObservableProperty]
    public partial bool IsFrozen { get; set; }

    [ObservableProperty]
    public partial CadColor Color { get; set; }

    [ObservableProperty]
    public partial double LineWeight { get; set; }

    [ObservableProperty]
    public partial int Priority { get; private set; }

    [ObservableProperty]
    public partial int EntityCount { get; private set; }

    public void RefreshFromLayer(CadLayer layer, int priority, int entityCount)
    {
        _isRefreshing = true;
        try
        {
            Name = layer.Name;
            IsVisible = layer.IsVisible;
            IsLocked = layer.IsLocked;
            IsFrozen = layer.IsFrozen;
            Color = layer.Color;
            LineWeight = layer.LineWeight.IsByLayer ? CadLineWeight.Default.Value : layer.LineWeight.Value;
            Priority = priority;
            EntityCount = entityCount;
        }
        finally
        {
            _isRefreshing = false;
        }

        OnPropertyChanged(nameof(ToolTipText));
    }

    partial void OnNameChanged(string value)
    {
        if (_isRefreshing)
            return;

        _owner.RenameLayer(this, value);
    }

    partial void OnColorChanged(CadColor value) => CommitAppearance();

    partial void OnLineWeightChanged(double value) => CommitAppearance();

    partial void OnIsVisibleChanged(bool value) => CommitState();

    partial void OnIsLockedChanged(bool value) => CommitState();

    partial void OnIsFrozenChanged(bool value) => CommitState();

    private void CommitState()
    {
        if (_isRefreshing)
            return;

        _owner.SetLayerState(this);
    }

    private void CommitAppearance()
    {
        if (_isRefreshing)
            return;

        _owner.SetLayerAppearance(this);
    }
}
