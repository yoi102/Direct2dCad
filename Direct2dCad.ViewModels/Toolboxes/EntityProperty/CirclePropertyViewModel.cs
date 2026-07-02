using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public sealed record FillStyleOption(StyleId? Id, string Name)
{
    public override string ToString() => Name;
}

public partial class CirclePropertyViewModel : EntityPropertyViewModel
{
    private const double Epsilon = 1e-9;
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public CirclePropertyViewModel(CadDocumentViewModel documentViewModel, EntityId entityId)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        EntityId = entityId;
        RefreshFromEntity();
    }

    public EntityId EntityId { get; }
    public string EntityIdText => EntityId.ToString();
    public IReadOnlyList<FillStyleOption> FillStyleOptions { get; private set; } = [];
    public double Diameter => Radius * 2.0;

    [ObservableProperty]
    public partial double CenterX { get; set; }

    [ObservableProperty]
    public partial double CenterY { get; set; }

    [ObservableProperty]
    public partial double Radius { get; set; }

    [ObservableProperty]
    public partial FillStyleOption? SelectedFillStyleOption { get; set; }

    [ObservableProperty]
    public partial CadColor StrokeColor { get; set; }

    [ObservableProperty]
    public partial bool UseByLayerLineWeight { get; set; }

    [ObservableProperty]
    public partial double LineWeight { get; set; }

    [ObservableProperty]
    public partial int ZIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    public void RefreshFromEntity()
    {
        if (!TryGetCircle(out var circle))
            return;

        _isRefreshing = true;
        try
        {
            CenterX = circle.Center.X;
            CenterY = circle.Center.Y;
            Radius = circle.Radius;
            RefreshFillStyleOptions(circle.FillStyleId);
            StrokeColor = ResolveStrokeColor(circle);
            UseByLayerLineWeight = circle.LineWeight is null || circle.LineWeight.Value.IsByLayer;
            LineWeight = ResolveLineWeight(circle).Value;
            ZIndex = circle.ZIndex;
            IsVisible = circle.IsVisible;
        }
        finally
        {
            _isRefreshing = false;
        }

        OnPropertyChanged(nameof(Diameter));
    }

    partial void OnCenterXChanged(double value) => CommitGeometry();

    partial void OnCenterYChanged(double value) => CommitGeometry();

    partial void OnRadiusChanged(double value)
    {
        OnPropertyChanged(nameof(Diameter));
        CommitGeometry();
    }

    partial void OnSelectedFillStyleOptionChanged(FillStyleOption? value)
    {
        if (_isRefreshing || !TryGetCircle(out var circle))
            return;

        var fillStyleId = value?.Id;
        if (Nullable.Equals(circle.FillStyleId, fillStyleId))
            return;

        _documentViewModel.CadEditor.SetEntityFillStyle(EntityId, fillStyleId);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || !TryGetCircle(out var circle) || ResolveStrokeColor(circle) == value)
            return;

        _documentViewModel.CadEditor.SetEntityColor(EntityId, value);
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        if (_isRefreshing || !TryGetCircle(out _))
            return;

        _documentViewModel.CadEditor.SetEntityLineWeight(
            EntityId,
            value ? CadLineWeight.ByLayer : new CadLineWeight(ResolveLineWeightValue(LineWeight)));
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing || UseByLayerLineWeight || !TryGetCircle(out _))
            return;

        if (!IsFinitePositive(value))
        {
            RefreshFromEntity();
            return;
        }

        _documentViewModel.CadEditor.SetEntityLineWeight(EntityId, new CadLineWeight(value));
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing || !TryGetCircle(out var circle) || circle.ZIndex == value)
            return;

        _documentViewModel.CadEditor.SetEntityZIndex(EntityId, value);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing || !TryGetCircle(out var circle) || circle.IsVisible == value)
            return;

        _documentViewModel.CadEditor.SetEntityVisibility(EntityId, value);
    }

    private void CommitGeometry()
    {
        if (_isRefreshing || !TryGetCircle(out var circle))
            return;

        if (!TryCreateGeometry(out var center, out var radius))
        {
            RefreshFromEntity();
            return;
        }

        if (center.DistanceSquaredTo(circle.Center) <= Epsilon &&
            Math.Abs(radius - circle.Radius) <= Epsilon)
        {
            return;
        }

        _documentViewModel.CadEditor.SetCircleGeometry(EntityId, center, radius);
    }

    private bool TryCreateGeometry(out CadPointD center, out double radius)
    {
        center = new CadPointD(CenterX, CenterY);
        radius = Radius;

        return IsFinite(CenterX) &&
               IsFinite(CenterY) &&
               IsFinitePositive(radius);
    }

    private bool TryGetCircle(out CadCircle circle)
    {
        if (_documentViewModel.CadEditor.Document.TryGetEntity(EntityId, out var entity) &&
            entity is CadCircle currentCircle &&
            !currentCircle.IsErased)
        {
            circle = currentCircle;
            return true;
        }

        circle = null!;
        return false;
    }

    private void RefreshFillStyleOptions(StyleId? selectedStyleId)
    {
        FillStyleOptions = BuildFillStyleOptions(_documentViewModel.CadEditor.Document);
        OnPropertyChanged(nameof(FillStyleOptions));
        SelectedFillStyleOption = FindFillStyleOption(FillStyleOptions, selectedStyleId);
    }

    private CadColor ResolveStrokeColor(CadCircle circle)
    {
        var document = _documentViewModel.CadEditor.Document;
        var layer = document.GetLayer(circle.LayerId);
        var styleId = circle.GraphicStyleId ?? layer.DefaultGraphicStyleId;

        if (styleId is { } graphicStyleId &&
            document.TryGetStyle(graphicStyleId, out var style) &&
            style is CadGraphicStyle graphic)
        {
            return graphic.StrokeColor;
        }

        return layer.Color;
    }

    private CadLineWeight ResolveLineWeight(CadCircle circle)
    {
        var document = _documentViewModel.CadEditor.Document;
        var layer = document.GetLayer(circle.LayerId);
        var styleId = circle.GraphicStyleId ?? layer.DefaultGraphicStyleId;
        var styleWeight = styleId is { } graphicStyleId &&
                          document.TryGetStyle(graphicStyleId, out var style) &&
                          style is CadGraphicStyle graphic
            ? graphic.LineWeight
            : (CadLineWeight?)null;

        var weight = circle.LineWeight is { IsByLayer: false }
            ? circle.LineWeight.Value
            : styleWeight is { IsByLayer: false }
            ? styleWeight.Value
            : layer.LineWeight;

        return weight.IsByLayer || weight.Value <= 0
            ? CadLineWeight.Default
            : weight;
    }

    internal static IReadOnlyList<FillStyleOption> BuildFillStyleOptions(CadDocument document)
    {
        var options = new List<FillStyleOption>
        {
            new(null, "None")
        };

        options.AddRange(document.Styles.Values
            .OfType<CadFillStyle>()
            .OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase)
            .Select(style => new FillStyleOption(style.Id, style.Name)));

        return options;
    }

    internal static FillStyleOption? FindFillStyleOption(
        IReadOnlyList<FillStyleOption> options,
        StyleId? styleId)
    {
        return options.FirstOrDefault(option => Nullable.Equals(option.Id, styleId)) ??
               options.FirstOrDefault();
    }

    private static double ResolveLineWeightValue(double value)
    {
        return IsFinitePositive(value) ? value : CadLineWeight.Default.Value;
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && IsFinite(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public partial class TransientCirclePropertyViewModel : EntityPropertyViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public TransientCirclePropertyViewModel(CadDocumentViewModel documentViewModel)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        RefreshFromDocument();
    }

    public CadDocumentViewModel DocumentViewModel => _documentViewModel;
    public IReadOnlyList<FillStyleOption> FillStyleOptions { get; private set; } = [];

    [ObservableProperty]
    public partial FillStyleOption? SelectedFillStyleOption { get; set; }

    [ObservableProperty]
    public partial CadColor StrokeColor { get; set; }

    [ObservableProperty]
    public partial double LineWeight { get; set; }

    [ObservableProperty]
    public partial int ZIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    public void RefreshFromDocument()
    {
        _isRefreshing = true;
        try
        {
            RefreshFillStyleOptions(_documentViewModel.DrawingCircleFillStyleId);
            StrokeColor = _documentViewModel.DrawingCircleStrokeColor;
            LineWeight = _documentViewModel.DrawingCircleLineWeight;
            ZIndex = _documentViewModel.DrawingCircleZIndex;
            IsVisible = _documentViewModel.DrawingCircleIsVisible;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    partial void OnSelectedFillStyleOptionChanged(FillStyleOption? value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingCircleFillStyleId = value?.Id;
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingCircleStrokeColor = value;
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingCircleLineWeight = IsFinitePositive(value)
            ? value
            : CadLineWeight.Default.Value;
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingCircleZIndex = value;
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingCircleIsVisible = value;
    }

    private void RefreshFillStyleOptions(StyleId? selectedStyleId)
    {
        FillStyleOptions = CirclePropertyViewModel.BuildFillStyleOptions(_documentViewModel.CadEditor.Document);
        OnPropertyChanged(nameof(FillStyleOptions));
        SelectedFillStyleOption = CirclePropertyViewModel.FindFillStyleOption(FillStyleOptions, selectedStyleId);
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
