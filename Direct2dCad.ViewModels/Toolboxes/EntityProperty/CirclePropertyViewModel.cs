using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

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
    public partial CadColor FillColor { get; set; }

    [ObservableProperty]
    public partial CadColor StrokeColor { get; set; }

    [ObservableProperty]
    public partial bool UseByLayerColor { get; set; }

    [ObservableProperty]
    public partial bool UseByLayerLineWeight { get; set; }

    [ObservableProperty]
    public partial double LineWeight { get; set; }

    [ObservableProperty]
    public partial int ZIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    public bool ColorControlsEnabled => !UseByLayerColor;

    public bool FillColorControlsEnabled => SupportsFillColor(SelectedFillStyleOption);

    public bool LineWeightControlsEnabled => !UseByLayerLineWeight;

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
            FillColor = ResolveFillColor(_documentViewModel.CadEditor.Document, circle.FillStyleId);
            StrokeColor = ResolveStrokeColor(circle);
            UseByLayerColor = circle.UseLayerColor;
            UseByLayerLineWeight = circle.UseLayerLineWeight;
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
        OnPropertyChanged(nameof(FillColorControlsEnabled));

        if (_isRefreshing || !TryGetCircle(out var circle))
            return;

        var fillStyleId = ResolveFillStyleId(_documentViewModel.CadEditor.Document, value, FillColor);
        if (Nullable.Equals(circle.FillStyleId, fillStyleId))
            return;

        _documentViewModel.CadEditor.SetEntityFillStyle(EntityId, fillStyleId);
    }

    partial void OnFillColorChanged(CadColor value)
    {
        if (_isRefreshing || !SupportsFillColor(SelectedFillStyleOption) || !TryGetCircle(out var circle))
            return;

        var fillStyleId = ResolveFillStyleId(_documentViewModel.CadEditor.Document, SelectedFillStyleOption, value);
        if (Nullable.Equals(circle.FillStyleId, fillStyleId))
            return;

        _documentViewModel.CadEditor.SetEntityFillStyle(EntityId, fillStyleId);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || UseByLayerColor || !TryGetCircle(out var circle) || ResolveStrokeColor(circle) == value)
            return;

        _documentViewModel.CadEditor.SetEntityColor(EntityId, value);
    }

    partial void OnUseByLayerColorChanged(bool value)
    {
        OnPropertyChanged(nameof(ColorControlsEnabled));

        if (_isRefreshing || !TryGetCircle(out _))
            return;

        _documentViewModel.CadEditor.SetEntityUseLayerColor(EntityId, value);
        RefreshFromEntity();
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        OnPropertyChanged(nameof(LineWeightControlsEnabled));

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
        SelectedFillStyleOption = FindFillStyleOption(_documentViewModel.CadEditor.Document, FillStyleOptions, selectedStyleId);
        OnPropertyChanged(nameof(FillColorControlsEnabled));
    }

    private CadColor ResolveStrokeColor(CadCircle circle)
    {
        return ResolveStrokeColor(_documentViewModel.CadEditor.Document, circle, circle.GraphicStyleId);
    }

    private CadLineWeight ResolveLineWeight(CadCircle circle)
    {
        return ResolveEntityLineWeight(_documentViewModel.CadEditor.Document, circle, circle.GraphicStyleId);
    }

    internal static IReadOnlyList<FillStyleOption> BuildFillStyleOptions(CadDocument document)
        => FillStyleCatalog.BuildFillStyleOptions(document);

    internal static FillStyleOption? FindFillStyleOption(
        CadDocument document,
        IReadOnlyList<FillStyleOption> options,
        StyleId? styleId)
        => FillStyleCatalog.FindFillStyleOption(document, options, styleId);

    internal static StyleId? ResolveFillStyleId(CadDocument document, FillStyleOption? option)
        => FillStyleCatalog.ResolveFillStyleId(document, option);

    internal static StyleId? ResolveFillStyleId(CadDocument document, FillStyleOption? option, CadColor fillColor)
        => FillStyleCatalog.ResolveFillStyleId(document, option, fillColor);

    internal static CadColor ResolveFillColor(CadDocument document, StyleId? styleId)
        => FillStyleCatalog.ResolveFillColor(document, styleId, FillStyleCatalog.DefaultFillColor);

    internal static bool SupportsFillColor(FillStyleOption? option)
        => FillStyleCatalog.SupportsFillColor(option);

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
    public partial CadColor FillColor { get; set; }

    [ObservableProperty]
    public partial CadColor StrokeColor { get; set; }

    [ObservableProperty]
    public partial double LineWeight { get; set; }

    [ObservableProperty]
    public partial int ZIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    public bool FillColorControlsEnabled => CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption);

    public void RefreshFromDocument()
    {
        _isRefreshing = true;
        try
        {
            RefreshFillStyleOptions(_documentViewModel.DrawingCircleFillStyleId);
            FillColor = CirclePropertyViewModel.ResolveFillColor(_documentViewModel.CadEditor.Document, _documentViewModel.DrawingCircleFillStyleId);
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
        OnPropertyChanged(nameof(FillColorControlsEnabled));

        if (_isRefreshing)
            return;

        _documentViewModel.DrawingCircleFillStyleId = CirclePropertyViewModel.ResolveFillStyleId(_documentViewModel.CadEditor.Document, value, FillColor);
    }

    partial void OnFillColorChanged(CadColor value)
    {
        if (_isRefreshing || !CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption))
            return;

        _documentViewModel.DrawingCircleFillStyleId = CirclePropertyViewModel.ResolveFillStyleId(
            _documentViewModel.CadEditor.Document,
            SelectedFillStyleOption,
            value);
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
        SelectedFillStyleOption = CirclePropertyViewModel.FindFillStyleOption(_documentViewModel.CadEditor.Document, FillStyleOptions, selectedStyleId);
        OnPropertyChanged(nameof(FillColorControlsEnabled));
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
