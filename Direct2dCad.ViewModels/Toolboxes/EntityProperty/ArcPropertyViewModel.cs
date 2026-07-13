using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class ArcPropertyViewModel : EntityPropertyViewModel
{
    private const double Epsilon = 1e-9;
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public ArcPropertyViewModel(CadDocumentViewModel documentViewModel, EntityId entityId)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        EntityId = entityId;
        RefreshFromEntity();
    }

    public EntityId EntityId { get; }
    public string EntityIdText => EntityId.ToString();
    public double EndAngleDegrees => StartAngleDegrees + SweepAngleDegrees;

    [ObservableProperty]
    public partial double CenterX { get; set; }

    [ObservableProperty]
    public partial double CenterY { get; set; }

    [ObservableProperty]
    public partial double Radius { get; set; }

    [ObservableProperty]
    public partial double StartAngleDegrees { get; set; }

    [ObservableProperty]
    public partial double SweepAngleDegrees { get; set; }

    [ObservableProperty]
    public partial bool IsCounterClockwise { get; set; }

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

    public bool LineWeightControlsEnabled => !UseByLayerLineWeight;

    public void RefreshFromEntity()
    {
        if (!TryGetArc(out var arc))
            return;

        _isRefreshing = true;
        try
        {
            RefreshLayerOptions(_documentViewModel, arc);
            CenterX = arc.Center.X;
            CenterY = arc.Center.Y;
            Radius = arc.Radius;
            StartAngleDegrees = CadArc.RadiansToDegrees(arc.StartAngleRadians);
            SweepAngleDegrees = CadArc.RadiansToDegrees(arc.SweepAngleRadians);
            IsCounterClockwise = arc.IsCounterClockwise;
            StrokeColor = ResolveStrokeColor(arc);
            UseByLayerColor = arc.UseLayerColor;
            UseByLayerLineWeight = arc.UseLayerLineWeight;
            LineWeight = ResolveLineWeight(arc).Value;
            ZIndex = arc.ZIndex;
            IsVisible = arc.IsVisible;
        }
        finally
        {
            _isRefreshing = false;
        }

        OnPropertyChanged(nameof(EndAngleDegrees));
    }

    partial void OnCenterXChanged(double value) => CommitGeometry();

    partial void OnCenterYChanged(double value) => CommitGeometry();

    partial void OnRadiusChanged(double value) => CommitGeometry();

    partial void OnStartAngleDegreesChanged(double value)
    {
        OnPropertyChanged(nameof(EndAngleDegrees));
        CommitGeometry();
    }

    partial void OnSweepAngleDegreesChanged(double value)
    {
        OnPropertyChanged(nameof(EndAngleDegrees));
        CommitGeometry();
    }

    partial void OnIsCounterClockwiseChanged(bool value)
    {
        if (_isRefreshing)
            return;

        var magnitude = Math.Abs(SweepAngleDegrees);
        if (!IsFinitePositive(magnitude))
            magnitude = 90.0;

        SweepAngleDegrees = value ? magnitude : -magnitude;
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || UseByLayerColor || !TryGetArc(out var arc) || ResolveStrokeColor(arc) == value)
            return;

        _documentViewModel.CadEditor.SetEntityColor(EntityId, value);
    }

    partial void OnUseByLayerColorChanged(bool value)
    {
        OnPropertyChanged(nameof(ColorControlsEnabled));

        if (_isRefreshing || !TryGetArc(out _))
            return;

        _documentViewModel.CadEditor.SetEntityUseLayerColor(EntityId, value);
        RefreshFromEntity();
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        OnPropertyChanged(nameof(LineWeightControlsEnabled));

        if (_isRefreshing || !TryGetArc(out _))
            return;

        _documentViewModel.CadEditor.SetEntityLineWeight(
            EntityId,
            value ? CadLineWeight.ByLayer : new CadLineWeight(ResolveLineWeightValue(LineWeight)));
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing || UseByLayerLineWeight || !TryGetArc(out _))
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
        if (_isRefreshing || !TryGetArc(out var arc) || arc.ZIndex == value)
            return;

        _documentViewModel.CadEditor.SetEntityZIndex(EntityId, value);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing || !TryGetArc(out var arc) || arc.IsVisible == value)
            return;

        _documentViewModel.CadEditor.SetEntityVisibility(EntityId, value);
    }

    private void CommitGeometry()
    {
        if (_isRefreshing || !TryGetArc(out var arc))
            return;

        if (!TryCreateGeometry(out var center, out var radius, out var startAngleRadians, out var sweepAngleRadians))
        {
            RefreshFromEntity();
            return;
        }

        if (center.DistanceSquaredTo(arc.Center) <= Epsilon &&
            Math.Abs(radius - arc.Radius) <= Epsilon &&
            Math.Abs(startAngleRadians - arc.StartAngleRadians) <= Epsilon &&
            Math.Abs(sweepAngleRadians - arc.SweepAngleRadians) <= Epsilon)
        {
            return;
        }

        _documentViewModel.CadEditor.SetArcGeometry(EntityId, center, radius, startAngleRadians, sweepAngleRadians);
    }

    private bool TryCreateGeometry(
        out CadPointD center,
        out double radius,
        out double startAngleRadians,
        out double sweepAngleRadians)
    {
        center = new CadPointD(CenterX, CenterY);
        radius = Radius;
        startAngleRadians = CadArc.DegreesToRadians(StartAngleDegrees);
        sweepAngleRadians = CadArc.DegreesToRadians(SweepAngleDegrees);

        return IsFinite(CenterX) &&
               IsFinite(CenterY) &&
               IsFinitePositive(radius) &&
               IsFinite(StartAngleDegrees) &&
               IsFinite(SweepAngleDegrees) &&
               Math.Abs(sweepAngleRadians) > 1e-12 &&
               Math.Abs(sweepAngleRadians) <= Math.PI * 2.0 + 1e-12;
    }

    private bool TryGetArc(out CadArc arc)
    {
        if (_documentViewModel.CadEditor.Document.TryGetEntity(EntityId, out var entity) &&
            entity is CadArc currentArc &&
            !currentArc.IsErased)
        {
            arc = currentArc;
            return true;
        }

        arc = null!;
        return false;
    }

    private CadColor ResolveStrokeColor(CadArc arc)
    {
        return ResolveStrokeColor(_documentViewModel.CadEditor.Document, arc, arc.GraphicStyleId);
    }

    private CadLineWeight ResolveLineWeight(CadArc arc)
    {
        return ResolveEntityLineWeight(_documentViewModel.CadEditor.Document, arc, arc.GraphicStyleId);
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

public partial class TransientArcPropertyViewModel : EntityPropertyViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public TransientArcPropertyViewModel(CadDocumentViewModel documentViewModel)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        RefreshFromDocument();
    }

    public CadDocumentViewModel DocumentViewModel => _documentViewModel;

    [ObservableProperty]
    public partial CadColor StrokeColor { get; set; }

    [ObservableProperty]
    public partial bool UseByLayerColor { get; set; }

    [ObservableProperty]
    public partial double LineWeight { get; set; }

    [ObservableProperty]
    public partial bool UseByLayerLineWeight { get; set; }

    [ObservableProperty]
    public partial int ZIndex { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    public bool ColorControlsEnabled => !UseByLayerColor;
    public bool LineWeightControlsEnabled => !UseByLayerLineWeight;

    public void RefreshFromDocument()
    {
        _isRefreshing = true;
        try
        {
            RefreshDrawingLayerOptions(_documentViewModel);
            StrokeColor = _documentViewModel.DrawingDefaults.ArcStrokeColor;
            UseByLayerColor = _documentViewModel.DrawingDefaults.ArcUseLayerColor;
            LineWeight = _documentViewModel.DrawingDefaults.ArcLineWeight;
            UseByLayerLineWeight = _documentViewModel.DrawingDefaults.ArcUseLayerLineWeight;
            ZIndex = _documentViewModel.DrawingDefaults.ArcZIndex;
            IsVisible = _documentViewModel.DrawingDefaults.ArcIsVisible;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || UseByLayerColor)
            return;

        _documentViewModel.DrawingDefaults.ArcStrokeColor = value;
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing || UseByLayerLineWeight)
            return;

        _documentViewModel.DrawingDefaults.ArcLineWeight = IsFinitePositive(value)
            ? value
            : CadLineWeight.Default.Value;
    }

    partial void OnUseByLayerColorChanged(bool value)
    {
        OnPropertyChanged(nameof(ColorControlsEnabled));
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.ArcUseLayerColor = value;
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        OnPropertyChanged(nameof(LineWeightControlsEnabled));
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.ArcUseLayerLineWeight = value;
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.ArcZIndex = value;
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.ArcIsVisible = value;
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
