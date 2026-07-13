using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public abstract class EntityPropertyViewModel : ObservableObject,
    IDrawingLayerPropertySectionViewModel,
    IStrokeStylePropertySectionViewModel
{
    private bool _isRefreshingLayerOptions;
    private bool _isRefreshingEntityName;
    private bool _isRefreshingStrokeStyle;
    private bool _isDrawingLayerSelection;
    private bool _isPasteLayerSelection;
    private CadDocumentViewModel? _layerDocumentViewModel;
    private EntityId? _layerEntityId;
    private EntityLayerOption? _selectedLayerOption;
    private StrokeCapOption? _selectedStartCapOption;
    private StrokeCapOption? _selectedEndCapOption;
    private StrokeCapOption? _selectedDashCapOption;
    private StrokeDashStyleOption? _selectedDashStyleOption;
    private StrokeLineJoinOption? _selectedLineJoinOption;
    private string _entityName = string.Empty;
    private bool _supportsStartEndCaps;
    private bool _supportsLineJoin;

    public bool IsEditable { get; private set; } = true;
    public bool SupportsStartEndCaps
    {
        get => _supportsStartEndCaps;
        private set => SetProperty(ref _supportsStartEndCaps, value);
    }

    public bool SupportsLineJoin
    {
        get => _supportsLineJoin;
        private set => SetProperty(ref _supportsLineJoin, value);
    }

    public IReadOnlyList<EntityLayerOption> LayerOptions { get; private set; } = [];
    public IReadOnlyList<StrokeCapOption> StrokeCapOptions { get; } = Enum.GetValues<CadStrokeCap>()
        .Select(value => new StrokeCapOption(value, value.ToString()))
        .ToArray();
    public IReadOnlyList<StrokeDashStyleOption> StrokeDashStyleOptions { get; } = Enum.GetValues<CadStrokeDashStyle>()
        .Select(value => new StrokeDashStyleOption(value, value.ToString()))
        .ToArray();
    public IReadOnlyList<StrokeLineJoinOption> StrokeLineJoinOptions { get; } = Enum.GetValues<CadStrokeLineJoin>()
        .Select(value => new StrokeLineJoinOption(value, value.ToString()))
        .ToArray();

    public string EntityName
    {
        get => _entityName;
        set
        {
            if (SetProperty(ref _entityName, value ?? string.Empty))
                OnEntityNameChanged(_entityName);
        }
    }

    public EntityLayerOption? SelectedLayerOption
    {
        get => _selectedLayerOption;
        set
        {
            if (SetProperty(ref _selectedLayerOption, value))
                OnSelectedLayerOptionChanged(value);
        }
    }

    public StrokeCapOption? SelectedStartCapOption
    {
        get => _selectedStartCapOption;
        set
        {
            if (SetProperty(ref _selectedStartCapOption, value))
                CommitStrokeStyleChange();
        }
    }

    public StrokeCapOption? SelectedEndCapOption
    {
        get => _selectedEndCapOption;
        set
        {
            if (SetProperty(ref _selectedEndCapOption, value))
                CommitStrokeStyleChange();
        }
    }

    public StrokeCapOption? SelectedDashCapOption
    {
        get => _selectedDashCapOption;
        set
        {
            if (SetProperty(ref _selectedDashCapOption, value))
                CommitStrokeStyleChange();
        }
    }

    public StrokeDashStyleOption? SelectedDashStyleOption
    {
        get => _selectedDashStyleOption;
        set
        {
            if (SetProperty(ref _selectedDashStyleOption, value))
                CommitStrokeStyleChange();
        }
    }

    public StrokeLineJoinOption? SelectedLineJoinOption
    {
        get => _selectedLineJoinOption;
        set
        {
            if (SetProperty(ref _selectedLineJoinOption, value))
                CommitStrokeStyleChange();
        }
    }

    protected void RefreshLayerOptions(CadDocumentViewModel documentViewModel, CadEntity entity)
    {
        ArgumentNullException.ThrowIfNull(documentViewModel);
        ArgumentNullException.ThrowIfNull(entity);

        _layerDocumentViewModel = documentViewModel;
        _layerEntityId = entity.Id;
        _isDrawingLayerSelection = false;
        _isPasteLayerSelection = false;
        IsEditable = CadEntityAccessPolicy.IsEditable(documentViewModel.CadEditor.Document, entity);
        OnPropertyChanged(nameof(IsEditable));
        RefreshEntityName(entity.Name);
        RefreshStrokeStyleCapabilities(entity);
        RefreshStrokeStyle(entity.StrokeStyle);

        RefreshLayerOptionsCore(documentViewModel, entity.LayerId);
    }

    protected void RefreshDrawingLayerOptions(CadDocumentViewModel documentViewModel)
    {
        ArgumentNullException.ThrowIfNull(documentViewModel);

        _layerDocumentViewModel = documentViewModel;
        _layerEntityId = null;
        _isDrawingLayerSelection = true;
        _isPasteLayerSelection = false;
        RefreshEntityName(documentViewModel.DrawingDefaults.EntityName);

        RefreshLayerOptionsCore(documentViewModel, documentViewModel.DrawingLayerId);
    }

    protected void RefreshPasteLayerOptions(CadDocumentViewModel documentViewModel)
    {
        ArgumentNullException.ThrowIfNull(documentViewModel);

        _layerDocumentViewModel = documentViewModel;
        _layerEntityId = null;
        _isDrawingLayerSelection = false;
        _isPasteLayerSelection = true;

        RefreshLayerOptionsCore(documentViewModel, documentViewModel.PasteTargetLayerId);
    }

    private void RefreshLayerOptionsCore(CadDocumentViewModel documentViewModel, LayerId selectedLayerId)
    {
        var document = documentViewModel.CadEditor.Document;
        LayerOptions = document.Layers.Values
            .Where(layer => layer.Id.Equals(selectedLayerId) ||
                            CadEntityAccessPolicy.CanAddToLayer(document, layer.Id))
            .OrderBy(layer => document.DocumentSettings.LayerDrawingPriority.GetPriority(layer.Id))
            .ThenBy(layer => layer.Id.Value)
            .Select(layer => new EntityLayerOption(layer.Id, layer.Name, layer.Color))
            .ToArray();
        OnPropertyChanged(nameof(LayerOptions));

        _isRefreshingLayerOptions = true;
        try
        {
            SelectedLayerOption =
                LayerOptions.FirstOrDefault(option => option.LayerId.Equals(selectedLayerId)) ??
                LayerOptions.FirstOrDefault();
        }
        finally
        {
            _isRefreshingLayerOptions = false;
        }
    }

    protected static CadColor ResolveStrokeColor(
        CadDocument document,
        CadEntity entity,
        StyleId? graphicStyleId)
    {
        var layer = document.GetLayer(entity.LayerId);
        return entity.UseLayerColor
            ? ResolveLayerStrokeColor(document, layer)
            : ResolveGraphicStrokeColor(document, graphicStyleId ?? layer.DefaultGraphicStyleId) ??
              ResolveLayerStrokeColor(document, layer);
    }

    protected static CadLineWeight ResolveEntityLineWeight(
        CadDocument document,
        CadEntity entity,
        StyleId? graphicStyleId)
    {
        if (entity.LineWeight is { IsByLayer: false } explicitWeight)
            return NormalizeLineWeight(explicitWeight);

        var layer = document.GetLayer(entity.LayerId);
        var styleWeight = ResolveGraphicLineWeight(document, graphicStyleId ?? layer.DefaultGraphicStyleId);
        return styleWeight is { IsByLayer: false }
            ? NormalizeLineWeight(styleWeight.Value)
            : CadLineWeight.Default;
    }

    protected static CadColor ResolveLayerStrokeColor(CadDocument document, CadLayer layer)
    {
        return ResolveGraphicStrokeColor(document, layer.DefaultGraphicStyleId) ?? layer.Color;
    }

    private static CadColor? ResolveGraphicStrokeColor(CadDocument document, StyleId? styleId)
    {
        return styleId is { } graphicStyleId &&
               document.TryGetStyle(graphicStyleId, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic.StrokeColor
            : null;
    }

    private static CadLineWeight? ResolveGraphicLineWeight(CadDocument document, StyleId? styleId)
    {
        return styleId is { } graphicStyleId &&
               document.TryGetStyle(graphicStyleId, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic.LineWeight
            : null;
    }

    private static CadLineWeight NormalizeLineWeight(CadLineWeight lineWeight)
    {
        return lineWeight.IsByLayer || lineWeight.Value <= 0
            ? CadLineWeight.Default
            : lineWeight;
    }

    private void OnSelectedLayerOptionChanged(EntityLayerOption? option)
    {
        if (_isRefreshingLayerOptions ||
            option is null ||
            _layerDocumentViewModel is not { } documentViewModel ||
            (!_isDrawingLayerSelection && !_isPasteLayerSelection && _layerEntityId is null))
        {
            return;
        }

        if (_isDrawingLayerSelection)
        {
            if (!documentViewModel.DrawingLayerId.Equals(option.LayerId))
                documentViewModel.DrawingLayerId = option.LayerId;

            return;
        }

        if (_isPasteLayerSelection)
        {
            documentViewModel.PasteTargetLayerId = option.LayerId;
            return;
        }

        if (_layerEntityId is not { } entityId ||
            !documentViewModel.CadEditor.Document.TryGetEntity(entityId, out var entity) ||
            entity is null ||
            entity.IsErased ||
            entity.LayerId.Equals(option.LayerId))
        {
            return;
        }

        documentViewModel.CadEditor.ChangeEntityLayer(entityId, option.LayerId);
    }

    private void RefreshEntityName(string name)
    {
        _isRefreshingEntityName = true;
        try
        {
            EntityName = name;
        }
        finally
        {
            _isRefreshingEntityName = false;
        }
    }

    private void OnEntityNameChanged(string value)
    {
        if (_isRefreshingEntityName ||
            _layerDocumentViewModel is not { } documentViewModel)
        {
            return;
        }

        var normalizedName = value ?? string.Empty;
        if (_isDrawingLayerSelection)
        {
            if (!string.Equals(documentViewModel.DrawingDefaults.EntityName, normalizedName, StringComparison.Ordinal))
                documentViewModel.DrawingDefaults.EntityName = normalizedName;
            return;
        }

        if (
            _layerEntityId is not { } entityId ||
            !documentViewModel.CadEditor.Document.TryGetEntity(entityId, out var entity) ||
            entity is null ||
            entity.IsErased)
        {
            return;
        }

        if (string.Equals(entity.Name, normalizedName, StringComparison.Ordinal))
            return;

        documentViewModel.CadEditor.RenameEntity(entityId, normalizedName);
    }

    private void RefreshStrokeStyle(CadStrokeStyle strokeStyle)
    {
        _isRefreshingStrokeStyle = true;
        try
        {
            SelectedStartCapOption = FindStrokeCapOption(strokeStyle.StartCap);
            SelectedEndCapOption = FindStrokeCapOption(strokeStyle.EndCap);
            SelectedDashCapOption = FindStrokeCapOption(strokeStyle.DashCap);
            SelectedDashStyleOption = FindDashStyleOption(strokeStyle.DashStyle);
            SelectedLineJoinOption = FindLineJoinOption(strokeStyle.LineJoin);
        }
        finally
        {
            _isRefreshingStrokeStyle = false;
        }
    }

    private void RefreshStrokeStyleCapabilities(CadEntity entity)
    {
        SupportsStartEndCaps = entity switch
        {
            CadLine => true,
            CadArc arc => !arc.IsFullCircle,
            CadEllipseArc => true,
            CadPolyline polyline => !polyline.Closed,
            CadSpline spline => !spline.Closed,
            _ => false
        };
        SupportsLineJoin = entity is CadRectangle or CadPolyline or CadSpline;
    }

    private void CommitStrokeStyleChange()
    {
        if (_isRefreshingStrokeStyle ||
            _layerDocumentViewModel is not { } documentViewModel ||
            _layerEntityId is not { } entityId ||
            !documentViewModel.CadEditor.Document.TryGetEntity(entityId, out var entity) ||
            entity is null ||
            entity.IsErased)
        {
            return;
        }

        var strokeStyle = new CadStrokeStyle(
            SelectedStartCapOption?.Value ?? CadStrokeStyle.Default.StartCap,
            SelectedEndCapOption?.Value ?? CadStrokeStyle.Default.EndCap,
            SelectedDashCapOption?.Value ?? CadStrokeStyle.Default.DashCap,
            SelectedDashStyleOption?.Value ?? CadStrokeStyle.Default.DashStyle,
            SelectedLineJoinOption?.Value ?? CadStrokeStyle.Default.LineJoin);

        if (entity.StrokeStyle == strokeStyle)
            return;

        documentViewModel.CadEditor.SetEntityStrokeStyle(entityId, strokeStyle);
    }

    private StrokeCapOption FindStrokeCapOption(CadStrokeCap value)
    {
        return StrokeCapOptions.FirstOrDefault(option => option.Value == value) ??
               StrokeCapOptions.First(option => option.Value == CadStrokeStyle.Default.StartCap);
    }

    private StrokeDashStyleOption FindDashStyleOption(CadStrokeDashStyle value)
    {
        return StrokeDashStyleOptions.FirstOrDefault(option => option.Value == value) ??
               StrokeDashStyleOptions.First(option => option.Value == CadStrokeStyle.Default.DashStyle);
    }

    private StrokeLineJoinOption FindLineJoinOption(CadStrokeLineJoin value)
    {
        return StrokeLineJoinOptions.FirstOrDefault(option => option.Value == value) ??
               StrokeLineJoinOptions.First(option => option.Value == CadStrokeStyle.Default.LineJoin);
    }
}

public sealed record EntityLayerOption(LayerId LayerId, string Name, CadColor Color)
{
    public string LayerIdText => LayerId.Value.ToString();
}

public sealed record StrokeCapOption(CadStrokeCap Value, string Name);

public sealed record StrokeDashStyleOption(CadStrokeDashStyle Value, string Name);

public sealed record StrokeLineJoinOption(CadStrokeLineJoin Value, string Name);
