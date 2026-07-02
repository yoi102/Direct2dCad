using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class RectanglePropertyViewModel : EntityPropertyViewModel
{
    private const double Epsilon = 1e-9;
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;
    private bool _isUpdatingGeometryProperties;

    public RectanglePropertyViewModel(CadDocumentViewModel documentViewModel, EntityId entityId)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        EntityId = entityId;
        RefreshFromEntity();
    }

    public EntityId EntityId { get; }
    public string EntityIdText => EntityId.ToString();
    public IReadOnlyList<FillStyleOption> FillStyleOptions { get; private set; } = [];

    [ObservableProperty]
    public partial double Left { get; set; }

    [ObservableProperty]
    public partial double Bottom { get; set; }

    [ObservableProperty]
    public partial double Right { get; set; }

    [ObservableProperty]
    public partial double Top { get; set; }

    [ObservableProperty]
    public partial double CenterX { get; set; }

    [ObservableProperty]
    public partial double CenterY { get; set; }

    [ObservableProperty]
    public partial double Width { get; set; }

    [ObservableProperty]
    public partial double Height { get; set; }

    [ObservableProperty]
    public partial double CornerRadiusX { get; set; }

    [ObservableProperty]
    public partial double CornerRadiusY { get; set; }

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
        if (!TryGetRectangle(out var rectangle))
            return;

        _isRefreshing = true;
        try
        {
            RefreshGeometryProperties(rectangle.Bounds);
            CornerRadiusX = rectangle.CornerRadiusX;
            CornerRadiusY = rectangle.CornerRadiusY;
            RefreshFillStyleOptions(rectangle.FillStyleId);
            StrokeColor = ResolveStrokeColor(rectangle);
            UseByLayerLineWeight = rectangle.LineWeight is null || rectangle.LineWeight.Value.IsByLayer;
            LineWeight = ResolveLineWeight(rectangle).Value;
            ZIndex = rectangle.ZIndex;
            IsVisible = rectangle.IsVisible;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    partial void OnLeftChanged(double value) => CommitEdgeGeometryChange();

    partial void OnBottomChanged(double value) => CommitEdgeGeometryChange();

    partial void OnRightChanged(double value) => CommitEdgeGeometryChange();

    partial void OnTopChanged(double value) => CommitEdgeGeometryChange();

    partial void OnCenterXChanged(double value) => CommitCenterSizeGeometryChange();

    partial void OnCenterYChanged(double value) => CommitCenterSizeGeometryChange();

    partial void OnWidthChanged(double value) => CommitCenterSizeGeometryChange();

    partial void OnHeightChanged(double value) => CommitCenterSizeGeometryChange();

    partial void OnCornerRadiusXChanged(double value) => CommitCornerRadius();

    partial void OnCornerRadiusYChanged(double value) => CommitCornerRadius();

    partial void OnSelectedFillStyleOptionChanged(FillStyleOption? value)
    {
        if (_isRefreshing || !TryGetRectangle(out var rectangle))
            return;

        var fillStyleId = value?.Id;
        if (Nullable.Equals(rectangle.FillStyleId, fillStyleId))
            return;

        _documentViewModel.CadEditor.SetEntityFillStyle(EntityId, fillStyleId);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || !TryGetRectangle(out var rectangle) || ResolveStrokeColor(rectangle) == value)
            return;

        _documentViewModel.CadEditor.SetEntityColor(EntityId, value);
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        if (_isRefreshing || !TryGetRectangle(out _))
            return;

        _documentViewModel.CadEditor.SetEntityLineWeight(
            EntityId,
            value ? CadLineWeight.ByLayer : new CadLineWeight(ResolveLineWeightValue(LineWeight)));
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing || UseByLayerLineWeight || !TryGetRectangle(out _))
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
        if (_isRefreshing || !TryGetRectangle(out var rectangle) || rectangle.ZIndex == value)
            return;

        _documentViewModel.CadEditor.SetEntityZIndex(EntityId, value);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing || !TryGetRectangle(out var rectangle) || rectangle.IsVisible == value)
            return;

        _documentViewModel.CadEditor.SetEntityVisibility(EntityId, value);
    }

    private void CommitEdgeGeometryChange()
    {
        if (_isRefreshing || _isUpdatingGeometryProperties || !TryGetRectangle(out _))
            return;

        if (!TryCreateBoundsFromEdges(out var bounds))
        {
            RefreshFromEntity();
            return;
        }

        _isUpdatingGeometryProperties = true;
        try
        {
            UpdateCenterSizeProperties(bounds);
        }
        finally
        {
            _isUpdatingGeometryProperties = false;
        }

        CommitGeometry(bounds);
    }

    private void CommitCenterSizeGeometryChange()
    {
        if (_isRefreshing || _isUpdatingGeometryProperties || !TryGetRectangle(out _))
            return;

        if (!TryCreateBoundsFromCenterSize(out var bounds))
        {
            RefreshFromEntity();
            return;
        }

        _isUpdatingGeometryProperties = true;
        try
        {
            UpdateEdgeProperties(bounds);
        }
        finally
        {
            _isUpdatingGeometryProperties = false;
        }

        CommitGeometry(bounds);
    }

    private void CommitGeometry(CadRectD bounds)
    {
        if (!TryGetRectangle(out var rectangle) || rectangle.Bounds.NearEquals(bounds, Epsilon))
            return;

        _documentViewModel.CadEditor.SetRectangleGeometry(EntityId, bounds);
    }

    private void CommitCornerRadius()
    {
        if (_isRefreshing || !TryGetRectangle(out var rectangle))
            return;

        if (!TryCreateCornerRadius(out var radiusX, out var radiusY))
        {
            RefreshFromEntity();
            return;
        }

        if (Math.Abs(radiusX - rectangle.CornerRadiusX) <= Epsilon &&
            Math.Abs(radiusY - rectangle.CornerRadiusY) <= Epsilon)
        {
            return;
        }

        _documentViewModel.CadEditor.SetRectangleCornerRadius(EntityId, radiusX, radiusY);
    }

    private bool TryCreateBoundsFromEdges(out CadRectD bounds)
    {
        bounds = new CadRectD(Left, Bottom, Right, Top);
        return IsFinite(Left) &&
               IsFinite(Bottom) &&
               IsFinite(Right) &&
               IsFinite(Top) &&
               bounds.Width > Epsilon &&
               bounds.Height > Epsilon;
    }

    private bool TryCreateBoundsFromCenterSize(out CadRectD bounds)
    {
        bounds = CadRectD.FromCenter(new CadPointD(CenterX, CenterY), Width, Height);
        return IsFinite(CenterX) &&
               IsFinite(CenterY) &&
               IsFinitePositive(Width) &&
               IsFinitePositive(Height);
    }

    private bool TryCreateCornerRadius(out double radiusX, out double radiusY)
    {
        radiusX = CornerRadiusX;
        radiusY = CornerRadiusY;

        return IsFinite(radiusX) &&
               IsFinite(radiusY) &&
               radiusX >= 0 &&
               radiusY >= 0;
    }

    private void RefreshGeometryProperties(CadRectD bounds)
    {
        UpdateEdgeProperties(bounds);
        UpdateCenterSizeProperties(bounds);
    }

    private void UpdateEdgeProperties(CadRectD bounds)
    {
        Left = bounds.Left;
        Bottom = bounds.Bottom;
        Right = bounds.Right;
        Top = bounds.Top;
    }

    private void UpdateCenterSizeProperties(CadRectD bounds)
    {
        CenterX = bounds.Center.X;
        CenterY = bounds.Center.Y;
        Width = bounds.Width;
        Height = bounds.Height;
    }

    private bool TryGetRectangle(out CadRectangle rectangle)
    {
        if (_documentViewModel.CadEditor.Document.TryGetEntity(EntityId, out var entity) &&
            entity is CadRectangle currentRectangle &&
            !currentRectangle.IsErased)
        {
            rectangle = currentRectangle;
            return true;
        }

        rectangle = null!;
        return false;
    }

    private void RefreshFillStyleOptions(StyleId? selectedStyleId)
    {
        FillStyleOptions = CirclePropertyViewModel.BuildFillStyleOptions(_documentViewModel.CadEditor.Document);
        OnPropertyChanged(nameof(FillStyleOptions));
        SelectedFillStyleOption = CirclePropertyViewModel.FindFillStyleOption(FillStyleOptions, selectedStyleId);
    }

    private CadColor ResolveStrokeColor(CadRectangle rectangle)
    {
        var document = _documentViewModel.CadEditor.Document;
        var layer = document.GetLayer(rectangle.LayerId);
        var styleId = rectangle.GraphicStyleId ?? layer.DefaultGraphicStyleId;

        if (styleId is { } graphicStyleId &&
            document.TryGetStyle(graphicStyleId, out var style) &&
            style is CadGraphicStyle graphic)
        {
            return graphic.StrokeColor;
        }

        return layer.Color;
    }

    private CadLineWeight ResolveLineWeight(CadRectangle rectangle)
    {
        var document = _documentViewModel.CadEditor.Document;
        var layer = document.GetLayer(rectangle.LayerId);
        var styleId = rectangle.GraphicStyleId ?? layer.DefaultGraphicStyleId;
        var styleWeight = styleId is { } graphicStyleId &&
                          document.TryGetStyle(graphicStyleId, out var style) &&
                          style is CadGraphicStyle graphic
            ? graphic.LineWeight
            : (CadLineWeight?)null;

        var weight = rectangle.LineWeight is { IsByLayer: false }
            ? rectangle.LineWeight.Value
            : styleWeight is { IsByLayer: false }
            ? styleWeight.Value
            : layer.LineWeight;

        return weight.IsByLayer || weight.Value <= 0
            ? CadLineWeight.Default
            : weight;
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

public partial class TransientRectanglePropertyViewModel : EntityPropertyViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public TransientRectanglePropertyViewModel(CadDocumentViewModel documentViewModel)
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
    public partial double CornerRadiusX { get; set; }

    [ObservableProperty]
    public partial double CornerRadiusY { get; set; }

    [ObservableProperty]
    public partial int ZIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    public void RefreshFromDocument()
    {
        _isRefreshing = true;
        try
        {
            RefreshFillStyleOptions(_documentViewModel.DrawingRectangleFillStyleId);
            StrokeColor = _documentViewModel.DrawingRectangleStrokeColor;
            LineWeight = _documentViewModel.DrawingRectangleLineWeight;
            CornerRadiusX = _documentViewModel.DrawingRectangleCornerRadiusX;
            CornerRadiusY = _documentViewModel.DrawingRectangleCornerRadiusY;
            ZIndex = _documentViewModel.DrawingRectangleZIndex;
            IsVisible = _documentViewModel.DrawingRectangleIsVisible;
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

        _documentViewModel.DrawingRectangleFillStyleId = value?.Id;
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingRectangleStrokeColor = value;
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingRectangleLineWeight = IsFinitePositive(value)
            ? value
            : CadLineWeight.Default.Value;
    }

    partial void OnCornerRadiusXChanged(double value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingRectangleCornerRadiusX = IsFiniteNonNegative(value)
            ? value
            : 0;
    }

    partial void OnCornerRadiusYChanged(double value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingRectangleCornerRadiusY = IsFiniteNonNegative(value)
            ? value
            : 0;
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingRectangleZIndex = value;
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingRectangleIsVisible = value;
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

    private static bool IsFiniteNonNegative(double value)
    {
        return value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
