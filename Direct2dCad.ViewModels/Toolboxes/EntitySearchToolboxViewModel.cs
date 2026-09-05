using AvalonDock.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db;
using Direct2dCad.ChangeTracking;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Collections;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using MessagePipe;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class EntitySearchToolboxViewModel : CadToolboxViewModelBase, IDisposable
{
    private readonly IDisposable _interactionStateChangedSubscription;
    private CadDocumentViewModel? _documentViewModel;
    private bool _isRefreshing;
    private readonly Dictionary<EntityId, int> _resultIndices = [];
    private (CadEditor? Editor, long Version, BlockId? Owner)? _lastRefreshState;

    public EntitySearchToolboxViewModel(
        IToolboxLayoutSettingsStore toolboxLayoutSettingsStore,
        IToolboxIconProvider toolboxIconProvider,
        ISubscriber<CadDocumentInteractionStateChangedMessage> interactionStateChangedSubscriber)
        : base(toolboxLayoutSettingsStore, "toolbox.entity-search", DockZone.RightTop, isOpenByDefault: false)
    {
        Title = Strings.EntitySearch;
        _interactionStateChangedSubscription = interactionStateChangedSubscriber.Subscribe(OnInteractionStateChanged);
        Icon = toolboxIconProvider.Search;
        Shortcut = "Ctrl+Shift+T";
        CanClose = false;
    }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial CadEntitySearchScope SearchScope { get; set; } = CadEntitySearchScope.CurrentSpace;

    [ObservableProperty]
    public partial EntitySearchLayerFilterOption? SelectedLayerFilter { get; set; }

    [ObservableProperty]
    public partial EntitySearchTypeFilterOption? SelectedTypeFilter { get; set; }

    [ObservableProperty]
    public partial EntitySearchResultItemViewModel? SelectedResult { get; set; }

    public ObservableRangeCollection<EntitySearchLayerFilterOption> LayerFilters { get; } = [];

    public ObservableRangeCollection<EntitySearchTypeFilterOption> TypeFilters { get; } = [];

    public ObservableRangeCollection<EntitySearchResultItemViewModel> Results { get; } = [];

    public bool HasDocument => _documentViewModel is not null;

    public bool HasResults => Results.Count > 0;

    public string ScopeText => GetResourceText("EntitySearchScope", "Scope");

    public string CurrentSpaceText => GetResourceText("EntitySearchCurrentSpace", "Current space");

    public string EntireDocumentText => GetResourceText("EntitySearchEntireDocument", "Entire document");

    public string SearchHintText => GetResourceText("EntitySearchHint", "Entity name, ID, layer, or type");

    public string LayerHintText => GetResourceText("EntitySearchLayerHint", "Layer");

    public string TypeHintText => GetResourceText("EntitySearchTypeHint", "Type");

    public string ResultSummary => _documentViewModel is null
        ? GetResourceText("EntitySearchNoDocument", "No document")
        : string.Format(
            GetResourceText("EntitySearchResultCountFormat", "{0} entities"),
            Results.Count);

    public void Attach(CadDocumentViewModel? documentViewModel)
    {
        if (ReferenceEquals(_documentViewModel, documentViewModel))
        {
            RefreshIfChanged();
            return;
        }

        _documentViewModel = documentViewModel;
        OnPropertyChanged(nameof(HasDocument));
        Refresh();
    }

    public void Dispose()
    {
        _interactionStateChangedSubscription.Dispose();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_isRefreshing)
            RefreshResults();
    }

    partial void OnSearchScopeChanged(CadEntitySearchScope value)
    {
        if (!_isRefreshing)
            Refresh();
    }

    partial void OnSelectedLayerFilterChanged(EntitySearchLayerFilterOption? value)
    {
        if (!_isRefreshing)
            RefreshResults();
    }

    partial void OnSelectedTypeFilterChanged(EntitySearchTypeFilterOption? value)
    {
        if (!_isRefreshing)
            RefreshResults();
    }

    partial void OnSelectedResultChanged(EntitySearchResultItemViewModel? value)
    {
        if (_isRefreshing || value is null || _documentViewModel is null)
            return;

        var editor = _documentViewModel.CadEditor;
        if (editor.Document.TryGetEntity(value.EntityId, out var entity) &&
            entity is { IsErased: false } &&
            entity.OwnerBlockId.Equals(editor.ActiveOwnerBlockId))
        {
            _documentViewModel.SelectEntities([value.EntityId]);
        }
    }

    [RelayCommand]
    private void PanToResult()
    {
        if (SelectedResult is null || _documentViewModel is null)
            return;

        if (_documentViewModel.PanToEntity(SelectedResult.EntityId))
            _documentViewModel.SelectEntities([SelectedResult.EntityId]);
    }

    [RelayCommand]
    private void Refresh()
    {
        var refreshState = GetRefreshState();
        var scopedEntities = GetScopedEntities().ToArray();
        _isRefreshing = true;
        try
        {
            RefreshLayerFilters(scopedEntities);
            RefreshTypeFilters(scopedEntities);
        }
        finally
        {
            _isRefreshing = false;
        }

        RefreshResults(scopedEntities);
        _lastRefreshState = refreshState;
    }

    private void OnInteractionStateChanged(CadDocumentInteractionStateChangedMessage message)
    {
        if (!ReferenceEquals(message.DocumentViewModel, _documentViewModel))
            return;

        RefreshIfChanged();
    }

    private void RefreshIfChanged()
    {
        if (_isRefreshing || _lastRefreshState == GetRefreshState())
            return;
        if (!TryRefreshChangedResults())
            Refresh();
    }

    private bool TryRefreshChangedResults()
    {
        var state = GetRefreshState();
        if (_lastRefreshState is not { } previous || state.Editor is not { } editor ||
            !ReferenceEquals(previous.Editor, editor) || previous.Owner != state.Owner ||
            unchecked(previous.Version + 1) != state.Version)
            return false;

        var changes = editor.LastDocumentChanges;
        const CadEntityChangeKind membershipOrOrder = CadEntityChangeKind.Created |
            CadEntityChangeKind.Deleted | CadEntityChangeKind.Layer |
            CadEntityChangeKind.DrawOrder | CadEntityChangeKind.Visibility;
        if (changes.AffectsDocumentStructure || changes.TableChanges != CadDocumentTableChangeKind.None || changes.AffectsLayouts || changes.AffectsLayoutStructure ||
            changes.EntityChanges.Any(change => (change.Kind & membershipOrOrder) != 0))
            return false;

        foreach (var change in changes.EntityChanges)
        {
            if (!editor.Document.TryGetEntity(change.EntityId, out var entity) || entity is null ||
                (change.Kind.HasFlag(CadEntityChangeKind.Metadata) && !string.IsNullOrWhiteSpace(SearchText)) ||
                (editor.Document.TryGetBlock(entity.OwnerBlockId, out var owner) && owner is { IsSystem: false }))
                return false;
        }

        _isRefreshing = true;
        try
        {
            var selectedId = SelectedResult?.EntityId;
            var replacements = new Dictionary<int, EntitySearchResultItemViewModel>();
            foreach (var change in changes.EntityChanges)
            {
                if (!_resultIndices.TryGetValue(change.EntityId, out var index))
                    continue;
                var item = CreateResultItem(editor.Document, editor.Document.GetEntity(change.EntityId));
                if (item == Results[index])
                    continue;
                replacements[index] = item;
            }
            Results.ReplaceItems(replacements);
            if (selectedId is { } id && _resultIndices.TryGetValue(id, out var selectedIndex))
                SelectedResult = Results[selectedIndex];
            _lastRefreshState = state;
            return true;
        }
        finally { _isRefreshing = false; }
    }

    private (CadEditor? Editor, long Version, BlockId? Owner) GetRefreshState()
    {
        var editor = _documentViewModel?.CadEditor;
        return (editor, editor?.DocumentChangeVersion ?? 0,
            SearchScope == CadEntitySearchScope.CurrentSpace ? editor?.ActiveOwnerBlockId : null);
    }

    private void RefreshLayerFilters(IReadOnlyCollection<CadEntity> scopedEntities)
    {
        var selectedLayerId = SelectedLayerFilter?.LayerId;
        var filters = new List<EntitySearchLayerFilterOption>();
        var allLayers = new EntitySearchLayerFilterOption(
            null,
            GetResourceText("EntitySearchAllLayers", "All layers"),
            scopedEntities.Count);
        filters.Add(allLayers);

        if (_documentViewModel is not null)
        {
            var document = _documentViewModel.CadEditor.Document;
            var entityCountsByLayer = scopedEntities
                .GroupBy(entity => entity.LayerId)
                .ToDictionary(group => group.Key, group => group.Count());
            foreach (var layer in document.Layers.Values
                         .OrderBy(x => document.DocumentSettings.LayerDrawingPriority.GetPriority(x.Id))
                         .ThenBy(x => x.Id.Value))
            {
                filters.Add(new EntitySearchLayerFilterOption(
                    layer.Id,
                    layer.Name,
                    entityCountsByLayer.GetValueOrDefault(layer.Id)));
            }
        }

        LayerFilters.ReplaceRange(filters);
        SelectedLayerFilter = selectedLayerId is { } layerId
            ? LayerFilters.FirstOrDefault(x => x.LayerId.Equals(layerId)) ?? allLayers
            : allLayers;
    }

    private void RefreshTypeFilters(IReadOnlyCollection<CadEntity> scopedEntities)
    {
        var selectedTypeKey = SelectedTypeFilter?.TypeKey;
        var filters = new List<EntitySearchTypeFilterOption>();
        var allTypes = new EntitySearchTypeFilterOption(
            null,
            GetResourceText("EntitySearchAllTypes", "All types"));
        filters.Add(allTypes);

        foreach (var type in scopedEntities
                     .Select(GetEntityTypeName)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            filters.Add(new EntitySearchTypeFilterOption(type, type));
        }

        TypeFilters.ReplaceRange(filters);
        SelectedTypeFilter = selectedTypeKey is { } typeKey
            ? TypeFilters.FirstOrDefault(x => string.Equals(x.TypeKey, typeKey, StringComparison.Ordinal)) ?? allTypes
            : allTypes;
    }

    private void RefreshResults()
        => RefreshResults(GetScopedEntities());

    private void RefreshResults(IEnumerable<CadEntity> scopedEntities)
    {
        var selectedEntityId = SelectedResult?.EntityId;
        var results = new List<EntitySearchResultItemViewModel>();

        if (_documentViewModel is not null)
        {
            var document = _documentViewModel.CadEditor.Document;
            var query = SearchText?.Trim();
            var layerId = SelectedLayerFilter?.LayerId;
            var typeKey = SelectedTypeFilter?.TypeKey;

            foreach (var entity in scopedEntities
                         .Where(x => layerId is null || x.LayerId.Equals(layerId.Value))
                         .Where(x => typeKey is null || string.Equals(GetEntityTypeName(x), typeKey, StringComparison.Ordinal))
                         .Where(x => MatchesSearch(document, x, query))
                         .OrderBy(x => document.DocumentSettings.LayerDrawingPriority.GetPriority(x.LayerId))
                         .ThenBy(x => x.ZIndex)
                         .ThenBy(x => x.Id.Value))
            {
                var item = CreateResultItem(document, entity);
                results.Add(_resultIndices.TryGetValue(entity.Id, out var index) && Results[index] == item
                    ? Results[index] : item);
            }
        }

        var wasRefreshing = _isRefreshing;
        _isRefreshing = true;
        try
        {
            Results.ReplaceRange(results);
            _resultIndices.Clear();
            for (var index = 0; index < Results.Count; index++)
                _resultIndices.Add(Results[index].EntityId, index);
            SelectedResult = selectedEntityId is { } entityId
                ? Results.FirstOrDefault(x => x.EntityId.Equals(entityId))
                : null;
        }
        finally
        {
            _isRefreshing = wasRefreshing;
        }

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(ResultSummary));
    }

    private IEnumerable<CadEntity> GetScopedEntities()
    {
        if (_documentViewModel is null)
            return [];

        var editor = _documentViewModel.CadEditor;
        var document = editor.Document;
        var entities = document.Entities.Values.Where(entity => !entity.IsErased);
        if (SearchScope == CadEntitySearchScope.CurrentSpace)
        {
            return entities.Where(entity => entity.OwnerBlockId.Equals(editor.ActiveOwnerBlockId));
        }

        var paperSpaceBlockIds = document.Layouts.Values
            .Select(layout => layout.PaperSpaceBlockId)
            .ToHashSet();
        return entities.Where(entity =>
            entity.OwnerBlockId.Equals(BlockId.ModelSpace) ||
            paperSpaceBlockIds.Contains(entity.OwnerBlockId));
    }

    private static string GetResourceText(string key, string fallback)
        => Strings.ResourceManager.GetString(key, Strings.Culture) ?? fallback;

    private static bool MatchesSearch(CadDocument document, CadEntity entity, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return true;

        var layerName = document.TryGetLayer(entity.LayerId, out var layer) && layer is not null
            ? layer.Name
            : string.Empty;
        var type = GetEntityTypeName(entity);
        var id = entity.Id.Value.ToString();

        return Contains(entity.Name, query) ||
               Contains(layerName, query) ||
               Contains(type, query) ||
               Contains(id, query);
    }

    private static bool Contains(string? value, string query)
    {
        return !string.IsNullOrEmpty(value) &&
               value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static EntitySearchResultItemViewModel CreateResultItem(CadDocument document, CadEntity entity)
    {
        var layerName = document.TryGetLayer(entity.LayerId, out var layer) && layer is not null
            ? layer.Name
            : entity.LayerId.Value.ToString();
        var name = string.IsNullOrWhiteSpace(entity.Name)
            ? $"({GetEntityTypeName(entity)})"
            : entity.Name.Trim();

        return new EntitySearchResultItemViewModel(
            entity.Id,
            name,
            GetEntityTypeName(entity),
            layerName,
            entity.ZIndex,
            entity.Bounds,
            entity.IsVisible,
            entity.IsLocked);
    }

    private static string GetEntityTypeName(CadEntity entity)
        => entity switch
        {
            CadLine => "Line",
            CadCircle => "Circle",
            CadArc => "Arc",
            CadEllipse => "Ellipse",
            CadEllipseArc => "EllipseArc",
            CadRectangle => "Rectangle",
            CadPolyline polyline when polyline.Closed => "Polyline",
            CadPolyline => "Polyline",
            CadSpline => "Spline",
            CadCompositePath => "Composite Path",
            CadText => "Text",
            CadShapeText => "ShapeText",
            CadImage => "Image",
            CadOleObject => "OLE Object",
            CadBlockReference => "BlockReference",
            _ => entity.GetType().Name
        };
}

public sealed record EntitySearchLayerFilterOption(
    LayerId? LayerId,
    string Name,
    int EntityCount)
{
    public string DisplayText => LayerId is null
        ? Name
        : $"{Name} ({EntityCount})";
}

public sealed record EntitySearchTypeFilterOption(
    string? TypeKey,
    string DisplayText);

public enum CadEntitySearchScope
{
    CurrentSpace,
    EntireDocument
}

public sealed record EntitySearchResultItemViewModel(
    EntityId EntityId,
    string Name,
    string EntityType,
    string LayerName,
    int ZIndex,
    CadRectD Bounds,
    bool IsVisible,
    bool IsLocked)
{
    public string EntityIdText => EntityId.Value.ToString();

    public string BoundsText => Bounds.IsEmpty
        ? "Empty"
        : $"{Bounds.MinX:0.###}, {Bounds.MinY:0.###} - {Bounds.MaxX:0.###}, {Bounds.MaxY:0.###}";

    public string StatusText => IsVisible
        ? IsLocked ? "Visible, Locked" : "Visible"
        : IsLocked ? "Hidden, Locked" : "Hidden";

    public string ToolTipText => $"Id: {EntityIdText}, Layer: {LayerName}, Z: {ZIndex}, Bounds: {BoundsText}";
}
