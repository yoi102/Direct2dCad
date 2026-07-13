using System.Collections.ObjectModel;
using AvalonDock.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using MessagePipe;

namespace Direct2dCad.ViewModels.Toolboxes;

public partial class EntitySearchToolboxViewModel : CadToolboxViewModelBase, IDisposable
{
    private readonly IDisposable _interactionStateChangedSubscription;
    private CadDocumentViewModel? _documentViewModel;
    private bool _isRefreshing;

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

    public ObservableCollection<EntitySearchLayerFilterOption> LayerFilters { get; } = [];

    public ObservableCollection<EntitySearchTypeFilterOption> TypeFilters { get; } = [];

    public ObservableCollection<EntitySearchResultItemViewModel> Results { get; } = [];

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
            Refresh();
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
    }

    private void OnInteractionStateChanged(CadDocumentInteractionStateChangedMessage message)
    {
        if (!ReferenceEquals(message.DocumentViewModel, _documentViewModel))
            return;

        Refresh();
    }

    private void RefreshLayerFilters(IReadOnlyCollection<CadEntity> scopedEntities)
    {
        var selectedLayerId = SelectedLayerFilter?.LayerId;
        LayerFilters.Clear();
        var allLayers = new EntitySearchLayerFilterOption(
            null,
            GetResourceText("EntitySearchAllLayers", "All layers"),
            scopedEntities.Count);
        LayerFilters.Add(allLayers);

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
                LayerFilters.Add(new EntitySearchLayerFilterOption(
                    layer.Id,
                    layer.Name,
                    entityCountsByLayer.GetValueOrDefault(layer.Id)));
            }
        }

        SelectedLayerFilter = selectedLayerId is { } layerId
            ? LayerFilters.FirstOrDefault(x => x.LayerId.Equals(layerId)) ?? allLayers
            : allLayers;
    }

    private void RefreshTypeFilters(IReadOnlyCollection<CadEntity> scopedEntities)
    {
        var selectedTypeKey = SelectedTypeFilter?.TypeKey;
        TypeFilters.Clear();
        var allTypes = new EntitySearchTypeFilterOption(
            null,
            GetResourceText("EntitySearchAllTypes", "All types"));
        TypeFilters.Add(allTypes);

        foreach (var type in scopedEntities
                     .Select(GetEntityTypeName)
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(x => x, StringComparer.Ordinal))
        {
            TypeFilters.Add(new EntitySearchTypeFilterOption(type, type));
        }

        SelectedTypeFilter = selectedTypeKey is { } typeKey
            ? TypeFilters.FirstOrDefault(x => string.Equals(x.TypeKey, typeKey, StringComparison.Ordinal)) ?? allTypes
            : allTypes;
    }

    private void RefreshResults()
        => RefreshResults(GetScopedEntities());

    private void RefreshResults(IEnumerable<CadEntity> scopedEntities)
    {
        var selectedEntityId = SelectedResult?.EntityId;
        Results.Clear();

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
                Results.Add(CreateResultItem(document, entity));
            }
        }

        _isRefreshing = true;
        try
        {
            SelectedResult = selectedEntityId is { } entityId
                ? Results.FirstOrDefault(x => x.EntityId.Equals(entityId))
                : null;
        }
        finally
        {
            _isRefreshing = false;
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

public sealed class EntitySearchResultItemViewModel(
    EntityId entityId,
    string name,
    string entityType,
    string layerName,
    int zIndex,
    CadRectD bounds,
    bool isVisible,
    bool isLocked)
{
    public EntityId EntityId { get; } = entityId;

    public string Name { get; } = name;

    public string EntityType { get; } = entityType;

    public string LayerName { get; } = layerName;

    public int ZIndex { get; } = zIndex;

    public CadRectD Bounds { get; } = bounds;

    public bool IsVisible { get; } = isVisible;

    public bool IsLocked { get; } = isLocked;

    public string EntityIdText => EntityId.Value.ToString();

    public string BoundsText => Bounds.IsEmpty
        ? "Empty"
        : $"{Bounds.MinX:0.###}, {Bounds.MinY:0.###} - {Bounds.MaxX:0.###}, {Bounds.MaxY:0.###}";

    public string StatusText => IsVisible
        ? IsLocked ? "Visible, Locked" : "Visible"
        : IsLocked ? "Hidden, Locked" : "Hidden";

    public string ToolTipText => $"Id: {EntityIdText}, Layer: {LayerName}, Z: {ZIndex}, Bounds: {BoundsText}";
}
