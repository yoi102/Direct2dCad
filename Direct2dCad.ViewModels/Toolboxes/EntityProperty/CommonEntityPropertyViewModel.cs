using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.ViewModels.Services.Interactions;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class CommonEntityPropertyViewModel : EntityPropertyViewModel,
    IEntityHeaderPropertySectionViewModel,
    IEntitySettingsPropertySectionViewModel,
    IStrokeAppearancePropertySectionViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public CommonEntityPropertyViewModel(CadDocumentViewModel documentViewModel, EntityId entityId)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        EntityId = entityId;
        RefreshFromEntity();
    }

    public EntityId EntityId { get; }
    public string EntityIdText => EntityId.ToString();

    [ObservableProperty]
    public partial string Title { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool SupportsStrokeAppearance { get; private set; }

    [ObservableProperty]
    public partial bool SupportsStrokeStyle { get; private set; }

    [ObservableProperty]
    public partial int ZIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial CadColor StrokeColor { get; set; } = CadColor.White;

    [ObservableProperty]
    public partial bool UseByLayerColor { get; set; }

    [ObservableProperty]
    public partial double LineWeight { get; set; } = CadLineWeight.Default.Value;

    [ObservableProperty]
    public partial bool UseByLayerLineWeight { get; set; }

    public bool ColorControlsEnabled => SupportsStrokeAppearance && !UseByLayerColor;
    public bool LineWeightControlsEnabled => SupportsStrokeAppearance && !UseByLayerLineWeight;

    public void RefreshFromEntity()
    {
        if (!TryGetEntity(out var entity))
            return;

        _isRefreshing = true;
        try
        {
            RefreshLayerOptions(_documentViewModel, entity);
            Title = GetEntityTypeDisplayName(entity.GetType());
            SupportsStrokeAppearance = SupportsGraphicStyle(entity);
            SupportsStrokeStyle = entity is CadEllipseArc or CadCompositePath;
            ZIndex = entity.ZIndex;
            IsVisible = entity.IsVisible;
            UseByLayerColor = entity.UseLayerColor;
            UseByLayerLineWeight = entity.UseLayerLineWeight;

            if (SupportsStrokeAppearance)
            {
                var graphicStyleId = GetGraphicStyleId(entity);
                StrokeColor = ResolveStrokeColor(_documentViewModel.CadEditor.Document, entity, graphicStyleId);
                LineWeight = ResolveEntityLineWeight(_documentViewModel.CadEditor.Document, entity, graphicStyleId).Value;
            }
        }
        finally
        {
            _isRefreshing = false;
        }

        OnPropertyChanged(nameof(ColorControlsEnabled));
        OnPropertyChanged(nameof(LineWeightControlsEnabled));
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing || !TryGetEntity(out var entity) || entity.ZIndex == value)
            return;

        _documentViewModel.CadEditor.SetEntityZIndex(EntityId, value);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing || !TryGetEntity(out var entity) || entity.IsVisible == value)
            return;

        _documentViewModel.CadEditor.SetEntityVisibility(EntityId, value);
    }

    partial void OnUseByLayerColorChanged(bool value)
    {
        OnPropertyChanged(nameof(ColorControlsEnabled));
        if (_isRefreshing || !SupportsStrokeAppearance || !TryGetEntity(out var entity) || entity.UseLayerColor == value)
            return;

        _documentViewModel.CadEditor.SetEntityUseLayerColor(EntityId, value);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || !ColorControlsEnabled || !TryGetEntity(out _))
            return;

        _documentViewModel.CadEditor.SetEntityColor(EntityId, value);
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        OnPropertyChanged(nameof(LineWeightControlsEnabled));
        if (_isRefreshing || !SupportsStrokeAppearance || !TryGetEntity(out _))
            return;

        _documentViewModel.CadEditor.SetEntityLineWeight(
            EntityId,
            value ? CadLineWeight.ByLayer : new CadLineWeight(Math.Max(LineWeight, 0.01)));
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing || !LineWeightControlsEnabled || !TryGetEntity(out _))
            return;

        if (!double.IsFinite(value) || value <= 0)
        {
            RefreshFromEntity();
            return;
        }

        _documentViewModel.CadEditor.SetEntityLineWeight(EntityId, new CadLineWeight(value));
    }

    private bool TryGetEntity(out CadEntity entity)
    {
        if (_documentViewModel.CadEditor.Document.TryGetEntity(EntityId, out var current) &&
            current is { IsErased: false })
        {
            entity = current;
            return true;
        }

        entity = null!;
        return false;
    }

    private static bool SupportsGraphicStyle(CadEntity entity) =>
        entity is CadEllipseArc or CadShapeText or CadBlockReference or CadCompositePath;

    private static StyleId? GetGraphicStyleId(CadEntity entity) => entity switch
    {
        CadEllipseArc ellipseArc => ellipseArc.GraphicStyleId,
        CadShapeText shapeText => shapeText.GraphicStyleId,
        CadBlockReference blockReference => blockReference.GraphicStyleId,
        CadCompositePath path => path.GraphicStyleId,
        _ => null
    };

    private static string GetEntityTypeDisplayName(Type entityType)
    {
        var descriptor = CadSelectionEntityTypeCatalog.All.FirstOrDefault(item => item.EntityType == entityType);
        return descriptor is null
            ? entityType.Name
            : Direct2dCad.Lang.Strings.Strings.ResourceManager.GetString(
                  descriptor.ResourceKey,
                  CultureInfo.CurrentUICulture) ?? descriptor.FallbackName;
    }
}
