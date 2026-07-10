using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public abstract class EntityPropertyViewModel : ObservableObject
{
    private bool _isRefreshingLayerOptions;
    private bool _isRefreshingEntityName;
    private bool _isDrawingLayerSelection;
    private bool _isPasteLayerSelection;
    private CadDocumentViewModel? _layerDocumentViewModel;
    private EntityId? _layerEntityId;
    private EntityLayerOption? _selectedLayerOption;
    private string _entityName = string.Empty;

    public IReadOnlyList<EntityLayerOption> LayerOptions { get; private set; } = [];

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

    protected void RefreshLayerOptions(CadDocumentViewModel documentViewModel, CadEntity entity)
    {
        ArgumentNullException.ThrowIfNull(documentViewModel);
        ArgumentNullException.ThrowIfNull(entity);

        _layerDocumentViewModel = documentViewModel;
        _layerEntityId = entity.Id;
        _isDrawingLayerSelection = false;
        _isPasteLayerSelection = false;
        RefreshEntityName(entity);

        RefreshLayerOptionsCore(documentViewModel, entity.LayerId);
    }

    protected void RefreshDrawingLayerOptions(CadDocumentViewModel documentViewModel)
    {
        ArgumentNullException.ThrowIfNull(documentViewModel);

        _layerDocumentViewModel = documentViewModel;
        _layerEntityId = null;
        _isDrawingLayerSelection = true;
        _isPasteLayerSelection = false;

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

    private void RefreshEntityName(CadEntity entity)
    {
        _isRefreshingEntityName = true;
        try
        {
            EntityName = entity.Name;
        }
        finally
        {
            _isRefreshingEntityName = false;
        }
    }

    private void OnEntityNameChanged(string value)
    {
        if (_isRefreshingEntityName ||
            _layerDocumentViewModel is not { } documentViewModel ||
            _layerEntityId is not { } entityId ||
            !documentViewModel.CadEditor.Document.TryGetEntity(entityId, out var entity) ||
            entity is null ||
            entity.IsErased)
        {
            return;
        }

        var normalizedName = value ?? string.Empty;
        if (string.Equals(entity.Name, normalizedName, StringComparison.Ordinal))
            return;

        documentViewModel.CadEditor.RenameEntity(entityId, normalizedName);
    }
}

public sealed record EntityLayerOption(LayerId LayerId, string Name, CadColor Color)
{
    public string LayerIdText => LayerId.Value.ToString();
}
