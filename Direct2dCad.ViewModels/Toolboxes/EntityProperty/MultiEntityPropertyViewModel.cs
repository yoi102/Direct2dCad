using System.Collections.ObjectModel;
using System.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.ViewModels.Services.Interactions;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class MultiEntityPropertyViewModel : ObservableObject, IStrokeStylePropertySectionViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;
    private readonly EntityId[] _entityIds;
    private bool _isRefreshing;

    public MultiEntityPropertyViewModel(
        CadDocumentViewModel documentViewModel,
        IEnumerable<EntityId> entityIds)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        _entityIds = entityIds?.Distinct().OrderBy(id => id.Value).ToArray()
            ?? throw new ArgumentNullException(nameof(entityIds));
        if (_entityIds.Length < 2)
            throw new ArgumentException("Multi-entity properties require at least two entities.", nameof(entityIds));

        RefreshFromEntities();
    }

    public int SelectedCount => _entityIds.Length;
    public string SelectionCountText => $"{GetLocalizedText("SelectedEntities", "Selected entities")}: {SelectedCount}";

    [ObservableProperty]
    public partial bool IsEditable { get; private set; } = true;

    [ObservableProperty]
    public partial bool IsSameType { get; private set; }

    [ObservableProperty]
    public partial string EntityTypeName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<EntityLayerOption> LayerOptions { get; private set; } = [];

    [ObservableProperty]
    public partial EntityLayerOption? SelectedLayerOption { get; set; }

    [ObservableProperty]
    public partial bool HasMixedLayer { get; private set; }

    [ObservableProperty]
    public partial int? ZIndex { get; set; }

    [ObservableProperty]
    public partial bool HasMixedZIndex { get; private set; }

    [ObservableProperty]
    public partial bool? IsVisible { get; set; }

    [ObservableProperty]
    public partial bool HasMixedVisibility { get; private set; }

    [ObservableProperty]
    public partial bool SupportsStrokeAppearance { get; private set; }

    [ObservableProperty]
    public partial bool SupportsStrokeStyle { get; private set; }

    [ObservableProperty]
    public partial bool SupportsStartEndCaps { get; private set; }

    [ObservableProperty]
    public partial bool SupportsLineJoin { get; private set; }

    [ObservableProperty]
    public partial bool? UseByLayerColor { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<EntityColorSourceOption> ColorSourceOptions { get; private set; } = [];

    [ObservableProperty]
    public partial EntityColorSourceOption? SelectedColorSourceOption { get; set; }

    [ObservableProperty]
    public partial bool HasMixedColorSource { get; private set; }

    [ObservableProperty]
    public partial CadColor StrokeColor { get; set; } = CadColor.White;

    [ObservableProperty]
    public partial bool HasMixedStrokeColor { get; private set; }

    [ObservableProperty]
    public partial bool? UseByLayerLineWeight { get; set; }

    [ObservableProperty]
    public partial double? LineWeight { get; set; }

    [ObservableProperty]
    public partial bool SupportsOpacity { get; private set; }

    [ObservableProperty]
    public partial double? Opacity { get; set; }

    [ObservableProperty]
    public partial bool SupportsFill { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<FillStyleOption> FillStyleOptions { get; private set; } = [];

    [ObservableProperty]
    public partial FillStyleOption? SelectedFillStyleOption { get; set; }

    [ObservableProperty]
    public partial bool HasMixedFillStyle { get; private set; }

    [ObservableProperty]
    public partial CadColor FillColor { get; set; } = FillStyleCatalog.DefaultFillColor;

    [ObservableProperty]
    public partial bool HasMixedFillColor { get; private set; }

    public IReadOnlyList<StrokeCapOption> StrokeCapOptions { get; } = Enum.GetValues<CadStrokeCap>()
        .Select(value => new StrokeCapOption(value, value.ToString()))
        .ToArray();

    public IReadOnlyList<StrokeDashStyleOption> StrokeDashStyleOptions { get; } = Enum.GetValues<CadStrokeDashStyle>()
        .Select(value => new StrokeDashStyleOption(value, value.ToString()))
        .ToArray();

    public IReadOnlyList<StrokeLineJoinOption> StrokeLineJoinOptions { get; } = Enum.GetValues<CadStrokeLineJoin>()
        .Select(value => new StrokeLineJoinOption(value, value.ToString()))
        .ToArray();

    [ObservableProperty]
    public partial StrokeCapOption? SelectedStartCapOption { get; set; }

    [ObservableProperty]
    public partial StrokeCapOption? SelectedEndCapOption { get; set; }

    [ObservableProperty]
    public partial StrokeCapOption? SelectedDashCapOption { get; set; }

    [ObservableProperty]
    public partial StrokeDashStyleOption? SelectedDashStyleOption { get; set; }

    [ObservableProperty]
    public partial StrokeLineJoinOption? SelectedLineJoinOption { get; set; }

    public bool ColorControlsEnabled =>
        SupportsStrokeAppearance &&
        SelectedColorSourceOption?.Value == CadColorSource.Explicit;
    public bool LineWeightControlsEnabled => SupportsStrokeAppearance && UseByLayerLineWeight == false;
    public bool FillColorControlsEnabled =>
        SupportsFill && FillStyleCatalog.SupportsFillColor(SelectedFillStyleOption);

    public bool Matches(IEnumerable<EntityId> entityIds) =>
        _entityIds.SequenceEqual(entityIds.Distinct().OrderBy(id => id.Value));

    public void RefreshFromEntities()
    {
        var entities = ResolveEntities();
        if (entities.Count < 2)
            return;

        _isRefreshing = true;
        try
        {
            IsEditable = entities.All(entity =>
                CadEntityAccessPolicy.IsEditable(_documentViewModel.CadEditor.Document, entity));
            IsSameType = entities.Select(entity => entity.GetType()).Distinct().Count() == 1;
            EntityTypeName = IsSameType
                ? GetEntityTypeDisplayName(entities[0].GetType())
                : GetLocalizedText("MultipleEntityTypes", "Multiple entity types");

            RefreshLayerOptions(entities);
            ZIndex = GetCommonValue(entities, entity => entity.ZIndex);
            HasMixedZIndex = ZIndex is null;
            IsVisible = GetCommonValue(entities, entity => entity.IsVisible);
            HasMixedVisibility = IsVisible is null;

            SupportsStrokeAppearance = entities.All(CadEntityCapabilities.SupportsGraphicStyle);
            SupportsStrokeStyle = entities.All(CadEntityCapabilities.SupportsStrokeStyle);
            SupportsStartEndCaps = SupportsStrokeStyle && entities.All(CadEntityCapabilities.SupportsStartEndCaps);
            SupportsLineJoin = SupportsStrokeStyle && entities.All(CadEntityCapabilities.SupportsLineJoin);
            if (SupportsStrokeAppearance)
            {
                RefreshColorSourceProperties(entities);
                var colors = entities.Select(ResolveStrokeColor).ToArray();
                StrokeColor = colors[0];
                HasMixedStrokeColor = colors.Skip(1).Any(color => color != colors[0]);
                UseByLayerLineWeight = GetCommonValue(entities, entity => entity.UseLayerLineWeight);
                LineWeight = GetCommonValue(entities, entity => ResolveLineWeight(entity).Value);
            }
            else
            {
                UseByLayerColor = null;
                ColorSourceOptions = [];
                SelectedColorSourceOption = null;
                HasMixedColorSource = false;
                HasMixedStrokeColor = false;
                UseByLayerLineWeight = null;
                LineWeight = null;
            }

            SupportsOpacity = entities.All(CadEntityCapabilities.SupportsOpacity);
            Opacity = SupportsOpacity ? GetCommonValue(entities, ResolveOpacity) : null;

            RefreshFillProperties(entities);
            RefreshStrokeStyleProperties(entities);
        }
        finally
        {
            _isRefreshing = false;
        }

        OnPropertyChanged(nameof(SelectionCountText));
        OnPropertyChanged(nameof(ColorControlsEnabled));
        OnPropertyChanged(nameof(LineWeightControlsEnabled));
        OnPropertyChanged(nameof(FillColorControlsEnabled));
    }

    partial void OnSelectedLayerOptionChanged(EntityLayerOption? value)
    {
        if (_isRefreshing || value is null)
            return;

        var entities = ResolveEntities();
        if (entities.All(entity => entity.LayerId.Equals(value.LayerId)))
            return;

        _documentViewModel.CadEditor.ChangeEntitiesLayer(_entityIds, value.LayerId);
    }

    partial void OnZIndexChanged(int? value)
    {
        if (_isRefreshing || value is null)
            return;

        _documentViewModel.CadEditor.SetEntityZIndex(_entityIds, value.Value);
    }

    partial void OnIsVisibleChanged(bool? value)
    {
        if (_isRefreshing || value is null)
            return;

        _documentViewModel.CadEditor.SetEntityVisibility(_entityIds, value.Value);
    }

    partial void OnUseByLayerColorChanged(bool? value)
    {
        OnPropertyChanged(nameof(ColorControlsEnabled));
        if (_isRefreshing || value is null || !SupportsStrokeAppearance)
            return;

        _documentViewModel.CadEditor.SetEntityUseLayerColor(_entityIds, value.Value);
    }

    partial void OnSelectedColorSourceOptionChanged(EntityColorSourceOption? value)
    {
        OnPropertyChanged(nameof(ColorControlsEnabled));
        if (_isRefreshing || value is null || !SupportsStrokeAppearance)
            return;

        var entities = ResolveEntities();
        if (entities.All(entity => entity.ColorSource == value.Value))
            return;

        _documentViewModel.CadEditor.SetEntityColorSource(_entityIds, value.Value);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || !SupportsStrokeAppearance || UseByLayerColor != false)
            return;

        _documentViewModel.CadEditor.SetEntityColor(_entityIds, value);
    }

    partial void OnUseByLayerLineWeightChanged(bool? value)
    {
        OnPropertyChanged(nameof(LineWeightControlsEnabled));
        if (_isRefreshing || value is null || !SupportsStrokeAppearance)
            return;

        _documentViewModel.CadEditor.SetEntityLineWeight(
            _entityIds,
            value.Value
                ? CadLineWeight.ByLayer
                : new CadLineWeight(LineWeight is > 0 ? LineWeight.Value : CadLineWeight.Default.Value));
    }

    partial void OnLineWeightChanged(double? value)
    {
        if (_isRefreshing || !SupportsStrokeAppearance || UseByLayerLineWeight != false || value is not > 0)
            return;

        _documentViewModel.CadEditor.SetEntityLineWeight(_entityIds, new CadLineWeight(value.Value));
    }

    partial void OnOpacityChanged(double? value)
    {
        if (_isRefreshing || !SupportsOpacity || value is null)
            return;

        _documentViewModel.CadEditor.SetEntityOpacity(_entityIds, Math.Clamp(value.Value, 0, 1));
    }

    partial void OnSelectedFillStyleOptionChanged(FillStyleOption? value)
    {
        OnPropertyChanged(nameof(FillColorControlsEnabled));
        if (_isRefreshing || !SupportsFill || value is null)
            return;

        var document = _documentViewModel.CadEditor.Document;
        var fillStyleId = FillStyleCatalog.ResolveFillStyleId(document, value, FillColor);
        _documentViewModel.CadEditor.SetEntityFillStyle(_entityIds, fillStyleId);
    }

    partial void OnFillColorChanged(CadColor value)
    {
        if (_isRefreshing || !FillColorControlsEnabled || SelectedFillStyleOption is null)
            return;

        var document = _documentViewModel.CadEditor.Document;
        var fillStyleId = FillStyleCatalog.ResolveFillStyleId(document, SelectedFillStyleOption, value);
        _documentViewModel.CadEditor.SetEntityFillStyle(_entityIds, fillStyleId);
    }

    partial void OnSelectedStartCapOptionChanged(StrokeCapOption? value) =>
        UpdateStrokeStyles(value is null ? null : value.Value, null, null, null, null);

    partial void OnSelectedEndCapOptionChanged(StrokeCapOption? value) =>
        UpdateStrokeStyles(null, value is null ? null : value.Value, null, null, null);

    partial void OnSelectedDashCapOptionChanged(StrokeCapOption? value) =>
        UpdateStrokeStyles(null, null, value is null ? null : value.Value, null, null);

    partial void OnSelectedDashStyleOptionChanged(StrokeDashStyleOption? value) =>
        UpdateStrokeStyles(null, null, null, value is null ? null : value.Value, null);

    partial void OnSelectedLineJoinOptionChanged(StrokeLineJoinOption? value) =>
        UpdateStrokeStyles(null, null, null, null, value is null ? null : value.Value);

    private IReadOnlyList<CadEntity> ResolveEntities()
    {
        return _entityIds
            .Select(entityId => _documentViewModel.CadEditor.Document.TryGetEntity(entityId, out var entity) ? entity : null)
            .Where(entity => entity is { IsErased: false })
            .Cast<CadEntity>()
            .ToArray();
    }

    private void RefreshLayerOptions(IReadOnlyList<CadEntity> entities)
    {
        var document = _documentViewModel.CadEditor.Document;
        LayerOptions = document.Layers.Values
            .Where(layer => entities.All(entity => entity.LayerId.Equals(layer.Id)) ||
                            CadEntityAccessPolicy.CanAddToLayer(document, layer.Id))
            .OrderBy(layer => document.DocumentSettings.LayerDrawingPriority.GetPriority(layer.Id))
            .ThenBy(layer => layer.Id.Value)
            .Select(layer => new EntityLayerOption(layer.Id, layer.Name, layer.Color))
            .ToArray();

        var commonLayerId = GetCommonValue(entities, entity => entity.LayerId);
        HasMixedLayer = commonLayerId is null;
        SelectedLayerOption = commonLayerId is { } layerId
            ? LayerOptions.FirstOrDefault(option => option.LayerId.Equals(layerId))
            : null;
    }

    private void RefreshFillProperties(IReadOnlyList<CadEntity> entities)
    {
        SupportsFill = entities.All(CadEntityCapabilities.SupportsFill);
        if (!SupportsFill)
        {
            FillStyleOptions = [];
            SelectedFillStyleOption = null;
            HasMixedFillStyle = false;
            HasMixedFillColor = false;
            return;
        }

        var document = _documentViewModel.CadEditor.Document;
        FillStyleOptions = FillStyleCatalog.BuildFillStyleOptions(document);
        var options = entities
            .Select(entity => FillStyleCatalog.FindFillStyleOption(document, FillStyleOptions, GetFillStyleId(entity)))
            .ToArray();
        SelectedFillStyleOption = options.Skip(1).All(option => Equals(option, options[0]))
            ? options[0]
            : null;
        HasMixedFillStyle = SelectedFillStyleOption is null;

        var colors = entities
            .Select(entity => FillStyleCatalog.ResolveFillColor(document, GetFillStyleId(entity), FillStyleCatalog.DefaultFillColor))
            .ToArray();
        FillColor = colors[0];
        HasMixedFillColor = colors.Skip(1).Any(color => color != colors[0]);
    }

    private void RefreshColorSourceProperties(IReadOnlyList<CadEntity> entities)
    {
        var document = _documentViewModel.CadEditor.Document;
        var supportsByBlock = entities.All(entity =>
            document.TryGetBlock(entity.OwnerBlockId, out var ownerBlock) &&
            ownerBlock is { IsSystem: false });
        var options = new List<EntityColorSourceOption>
        {
            new EntityColorSourceOption(
                CadColorSource.ByLayer,
                GetLocalizedText("ByLayer", "By layer")),
            new EntityColorSourceOption(
                CadColorSource.Explicit,
                GetLocalizedText("Explicit", "Explicit"))
        };
        if (supportsByBlock || entities.Any(entity => entity.ColorSource == CadColorSource.ByBlock))
        {
            options.Add(new EntityColorSourceOption(
                CadColorSource.ByBlock,
                GetLocalizedText("ByBlock", "By block")));
        }

        ColorSourceOptions = options;
        var firstSource = entities[0].ColorSource;
        CadColorSource? commonSource = entities.Skip(1).All(entity => entity.ColorSource == firstSource)
            ? firstSource
            : null;
        SelectedColorSourceOption = commonSource is { } source
            ? ColorSourceOptions.FirstOrDefault(option => option.Value == source)
            : null;
        HasMixedColorSource = commonSource is null;
        UseByLayerColor = commonSource is null
            ? null
            : commonSource == CadColorSource.ByLayer;
    }

    private void RefreshStrokeStyleProperties(IReadOnlyList<CadEntity> entities)
    {
        if (!SupportsStrokeStyle)
        {
            SelectedStartCapOption = null;
            SelectedEndCapOption = null;
            SelectedDashCapOption = null;
            SelectedDashStyleOption = null;
            SelectedLineJoinOption = null;
            return;
        }

        SelectedStartCapOption = FindCommonOption(
            entities,
            entity => entity.StrokeStyle.StartCap,
            StrokeCapOptions,
            option => option.Value);
        SelectedEndCapOption = FindCommonOption(
            entities,
            entity => entity.StrokeStyle.EndCap,
            StrokeCapOptions,
            option => option.Value);
        SelectedDashCapOption = FindCommonOption(
            entities,
            entity => entity.StrokeStyle.DashCap,
            StrokeCapOptions,
            option => option.Value);
        SelectedDashStyleOption = FindCommonOption(
            entities,
            entity => entity.StrokeStyle.DashStyle,
            StrokeDashStyleOptions,
            option => option.Value);
        SelectedLineJoinOption = FindCommonOption(
            entities,
            entity => entity.StrokeStyle.LineJoin,
            StrokeLineJoinOptions,
            option => option.Value);
    }

    private void UpdateStrokeStyles(
        CadStrokeCap? startCap,
        CadStrokeCap? endCap,
        CadStrokeCap? dashCap,
        CadStrokeDashStyle? dashStyle,
        CadStrokeLineJoin? lineJoin)
    {
        if (_isRefreshing || !SupportsStrokeStyle ||
            startCap is null && endCap is null && dashCap is null && dashStyle is null && lineJoin is null)
        {
            return;
        }

        _documentViewModel.CadEditor.UpdateEntityStrokeStyles(
            _entityIds,
            startCap,
            endCap,
            dashCap,
            dashStyle,
            lineJoin);
    }

    private CadColor ResolveStrokeColor(CadEntity entity)
    {
        var document = _documentViewModel.CadEditor.Document;
        var layer = document.GetLayer(entity.LayerId);
        if (entity.ColorSource != CadColorSource.Explicit)
            return ResolveGraphicStyle(document, layer.DefaultGraphicStyleId)?.StrokeColor ?? layer.Color;

        return ResolveGraphicStyle(document, GetGraphicStyleId(entity))?.StrokeColor ??
               ResolveGraphicStyle(document, layer.DefaultGraphicStyleId)?.StrokeColor ??
               layer.Color;
    }

    private CadLineWeight ResolveLineWeight(CadEntity entity)
    {
        if (entity.LineWeight is { IsByLayer: false } lineWeight && lineWeight.Value > 0)
            return lineWeight;

        var document = _documentViewModel.CadEditor.Document;
        var layer = document.GetLayer(entity.LayerId);
        var style = ResolveGraphicStyle(document, GetGraphicStyleId(entity) ?? layer.DefaultGraphicStyleId);
        return style?.LineWeight is { IsByLayer: false } styleWeight && styleWeight.Value > 0
            ? styleWeight
            : CadLineWeight.Default;
    }

    private static CadGraphicStyle? ResolveGraphicStyle(CadDocument document, StyleId? styleId) =>
        styleId is { } id && document.TryGetStyle(id, out var style) && style is CadGraphicStyle graphic
            ? graphic
            : null;

    private static StyleId? GetGraphicStyleId(CadEntity entity) => entity switch
    {
        CadLine value => value.GraphicStyleId,
        CadCircle value => value.GraphicStyleId,
        CadEllipse value => value.GraphicStyleId,
        CadEllipseArc value => value.GraphicStyleId,
        CadRectangle value => value.GraphicStyleId,
        CadArc value => value.GraphicStyleId,
        CadPolyline value => value.GraphicStyleId,
        CadSpline value => value.GraphicStyleId,
        CadCompositePath value => value.GraphicStyleId,
        CadText value => value.GraphicStyleId,
        CadShapeText value => value.GraphicStyleId,
        CadBlockReference value => value.GraphicStyleId,
        _ => null
    };

    private static double ResolveOpacity(CadEntity entity) => entity switch
    {
        CadImage image => image.Opacity,
        CadOleObject oleObject => oleObject.Opacity,
        _ => 1
    };

    private static StyleId? GetFillStyleId(CadEntity entity) => entity switch
    {
        CadCircle circle => circle.FillStyleId,
        CadEllipse ellipse => ellipse.FillStyleId,
        CadRectangle rectangle => rectangle.FillStyleId,
        CadPolyline polyline => polyline.FillStyleId,
        CadSpline spline => spline.FillStyleId,
        CadCompositePath path => path.FillStyleId,
        _ => null
    };

    private static TOption? FindCommonOption<TValue, TOption>(
        IReadOnlyList<CadEntity> entities,
        Func<CadEntity, TValue> selector,
        IReadOnlyList<TOption> options,
        Func<TOption, TValue> optionSelector)
        where TOption : class
    {
        var value = selector(entities[0]);
        if (entities.Skip(1).Any(entity => !EqualityComparer<TValue>.Default.Equals(selector(entity), value)))
            return null;

        return options.FirstOrDefault(option =>
            EqualityComparer<TValue>.Default.Equals(optionSelector(option), value));
    }

    private static T? GetCommonValue<T>(IReadOnlyList<CadEntity> entities, Func<CadEntity, T> selector)
        where T : struct, IEquatable<T>
    {
        var first = selector(entities[0]);
        return entities.Skip(1).All(entity => selector(entity).Equals(first)) ? first : null;
    }

    private static string GetEntityTypeDisplayName(Type entityType)
    {
        var descriptor = CadSelectionEntityTypeCatalog.All.FirstOrDefault(item => item.EntityType == entityType);
        return descriptor is null
            ? entityType.Name.RemovePrefix("Cad")
            : GetLocalizedText(descriptor.ResourceKey, descriptor.FallbackName);
    }

    private static string GetLocalizedText(string resourceKey, string fallback)
    {
        var resourceManager = new ResourceManager(typeof(Lang.Strings.Strings));
        return resourceManager.GetString(resourceKey) ?? fallback;
    }
}

internal static class MultiEntityPropertyStringExtensions
{
    public static string RemovePrefix(this string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
}
