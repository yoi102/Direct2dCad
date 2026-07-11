using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class EllipsePropertyViewModel : EntityPropertyViewModel
{
    private const double Epsilon = 1e-9;
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public EllipsePropertyViewModel(CadDocumentViewModel documentViewModel, EntityId entityId)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        EntityId = entityId;
        RefreshFromEntity();
    }

    public EntityId EntityId { get; }
    public string EntityIdText => EntityId.ToString();
    public IReadOnlyList<FillStyleOption> FillStyleOptions { get; private set; } = [];
    public double DiameterX => RadiusX * 2.0;
    public double DiameterY => RadiusY * 2.0;

    [ObservableProperty]
    public partial double CenterX { get; set; }

    [ObservableProperty]
    public partial double CenterY { get; set; }

    [ObservableProperty]
    public partial double RadiusX { get; set; }

    [ObservableProperty]
    public partial double RadiusY { get; set; }

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

    public bool FillColorControlsEnabled => CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption);

    public bool LineWeightControlsEnabled => !UseByLayerLineWeight;

    public void RefreshFromEntity()
    {
        if (!TryGetEllipse(out var ellipse))
            return;

        _isRefreshing = true;
        try
        {
            RefreshLayerOptions(_documentViewModel, ellipse);
            CenterX = ellipse.Center.X;
            CenterY = ellipse.Center.Y;
            RadiusX = ellipse.RadiusX;
            RadiusY = ellipse.RadiusY;
            RefreshFillStyleOptions(ellipse.FillStyleId);
            FillColor = CirclePropertyViewModel.ResolveFillColor(_documentViewModel.CadEditor.Document, ellipse.FillStyleId);
            StrokeColor = ResolveStrokeColor(ellipse);
            UseByLayerColor = ellipse.UseLayerColor;
            UseByLayerLineWeight = ellipse.UseLayerLineWeight;
            LineWeight = ResolveLineWeight(ellipse).Value;
            ZIndex = ellipse.ZIndex;
            IsVisible = ellipse.IsVisible;
        }
        finally
        {
            _isRefreshing = false;
        }

        OnPropertyChanged(nameof(DiameterX));
        OnPropertyChanged(nameof(DiameterY));
    }

    partial void OnCenterXChanged(double value) => CommitGeometry();

    partial void OnCenterYChanged(double value) => CommitGeometry();

    partial void OnRadiusXChanged(double value)
    {
        OnPropertyChanged(nameof(DiameterX));
        CommitGeometry();
    }

    partial void OnRadiusYChanged(double value)
    {
        OnPropertyChanged(nameof(DiameterY));
        CommitGeometry();
    }

    partial void OnSelectedFillStyleOptionChanged(FillStyleOption? value)
    {
        OnPropertyChanged(nameof(FillColorControlsEnabled));

        if (_isRefreshing || !TryGetEllipse(out var ellipse))
            return;

        var fillStyleId = CirclePropertyViewModel.ResolveFillStyleId(_documentViewModel.CadEditor.Document, value, FillColor);
        if (Nullable.Equals(ellipse.FillStyleId, fillStyleId))
            return;

        _documentViewModel.CadEditor.SetEntityFillStyle(EntityId, fillStyleId);
    }

    partial void OnFillColorChanged(CadColor value)
    {
        if (_isRefreshing || !CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption) || !TryGetEllipse(out var ellipse))
            return;

        var fillStyleId = CirclePropertyViewModel.ResolveFillStyleId(_documentViewModel.CadEditor.Document, SelectedFillStyleOption, value);
        if (Nullable.Equals(ellipse.FillStyleId, fillStyleId))
            return;

        _documentViewModel.CadEditor.SetEntityFillStyle(EntityId, fillStyleId);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || UseByLayerColor || !TryGetEllipse(out var ellipse) || ResolveStrokeColor(ellipse) == value)
            return;

        _documentViewModel.CadEditor.SetEntityColor(EntityId, value);
    }

    partial void OnUseByLayerColorChanged(bool value)
    {
        OnPropertyChanged(nameof(ColorControlsEnabled));

        if (_isRefreshing || !TryGetEllipse(out _))
            return;

        _documentViewModel.CadEditor.SetEntityUseLayerColor(EntityId, value);
        RefreshFromEntity();
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        OnPropertyChanged(nameof(LineWeightControlsEnabled));

        if (_isRefreshing || !TryGetEllipse(out _))
            return;

        _documentViewModel.CadEditor.SetEntityLineWeight(
            EntityId,
            value ? CadLineWeight.ByLayer : new CadLineWeight(ResolveLineWeightValue(LineWeight)));
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing || UseByLayerLineWeight || !TryGetEllipse(out _))
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
        if (_isRefreshing || !TryGetEllipse(out var ellipse) || ellipse.ZIndex == value)
            return;

        _documentViewModel.CadEditor.SetEntityZIndex(EntityId, value);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing || !TryGetEllipse(out var ellipse) || ellipse.IsVisible == value)
            return;

        _documentViewModel.CadEditor.SetEntityVisibility(EntityId, value);
    }

    private void CommitGeometry()
    {
        if (_isRefreshing || !TryGetEllipse(out var ellipse))
            return;

        if (!TryCreateGeometry(out var center, out var radiusX, out var radiusY))
        {
            RefreshFromEntity();
            return;
        }

        if (center.DistanceSquaredTo(ellipse.Center) <= Epsilon &&
            Math.Abs(radiusX - ellipse.RadiusX) <= Epsilon &&
            Math.Abs(radiusY - ellipse.RadiusY) <= Epsilon)
        {
            return;
        }

        _documentViewModel.CadEditor.SetEllipseGeometry(EntityId, center, radiusX, radiusY);
    }

    private bool TryCreateGeometry(out CadPointD center, out double radiusX, out double radiusY)
    {
        center = new CadPointD(CenterX, CenterY);
        radiusX = RadiusX;
        radiusY = RadiusY;

        return IsFinite(CenterX) &&
               IsFinite(CenterY) &&
               IsFinitePositive(radiusX) &&
               IsFinitePositive(radiusY);
    }

    private bool TryGetEllipse(out CadEllipse ellipse)
    {
        if (_documentViewModel.CadEditor.Document.TryGetEntity(EntityId, out var entity) &&
            entity is CadEllipse currentEllipse &&
            !currentEllipse.IsErased)
        {
            ellipse = currentEllipse;
            return true;
        }

        ellipse = null!;
        return false;
    }

    private void RefreshFillStyleOptions(StyleId? selectedStyleId)
    {
        FillStyleOptions = CirclePropertyViewModel.BuildFillStyleOptions(_documentViewModel.CadEditor.Document);
        OnPropertyChanged(nameof(FillStyleOptions));
        SelectedFillStyleOption = CirclePropertyViewModel.FindFillStyleOption(_documentViewModel.CadEditor.Document, FillStyleOptions, selectedStyleId);
        OnPropertyChanged(nameof(FillColorControlsEnabled));
    }

    private CadColor ResolveStrokeColor(CadEllipse ellipse)
    {
        return ResolveStrokeColor(_documentViewModel.CadEditor.Document, ellipse, ellipse.GraphicStyleId);
    }

    private CadLineWeight ResolveLineWeight(CadEllipse ellipse)
    {
        return ResolveEntityLineWeight(_documentViewModel.CadEditor.Document, ellipse, ellipse.GraphicStyleId);
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

public partial class TransientEllipsePropertyViewModel : EntityPropertyViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public TransientEllipsePropertyViewModel(CadDocumentViewModel documentViewModel)
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
            RefreshDrawingLayerOptions(_documentViewModel);
            RefreshFillStyleOptions(_documentViewModel.DrawingDefaults.EllipseFillStyleId);
            FillColor = CirclePropertyViewModel.ResolveFillColor(_documentViewModel.CadEditor.Document, _documentViewModel.DrawingDefaults.EllipseFillStyleId);
            StrokeColor = _documentViewModel.DrawingDefaults.EllipseStrokeColor;
            LineWeight = _documentViewModel.DrawingDefaults.EllipseLineWeight;
            ZIndex = _documentViewModel.DrawingDefaults.EllipseZIndex;
            IsVisible = _documentViewModel.DrawingDefaults.EllipseIsVisible;
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

        _documentViewModel.DrawingDefaults.EllipseFillStyleId = CirclePropertyViewModel.ResolveFillStyleId(_documentViewModel.CadEditor.Document, value, FillColor);
    }

    partial void OnFillColorChanged(CadColor value)
    {
        if (_isRefreshing || !CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption))
            return;

        _documentViewModel.DrawingDefaults.EllipseFillStyleId = CirclePropertyViewModel.ResolveFillStyleId(
            _documentViewModel.CadEditor.Document,
            SelectedFillStyleOption,
            value);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.EllipseStrokeColor = value;
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.EllipseLineWeight = IsFinitePositive(value)
            ? value
            : CadLineWeight.Default.Value;
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.EllipseZIndex = value;
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.EllipseIsVisible = value;
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
