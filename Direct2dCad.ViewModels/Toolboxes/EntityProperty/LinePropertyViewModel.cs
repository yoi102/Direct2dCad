using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class LinePropertyViewModel : EntityPropertyViewModel
{
    private const double Epsilon = 1e-9;
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;
    private bool _isUpdatingGeometryProperties;

    public LinePropertyViewModel(CadDocumentViewModel documentViewModel, EntityId entityId)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        EntityId = entityId;
        RefreshFromEntity();
    }

    public EntityId EntityId { get; }
    public string EntityIdText => EntityId.ToString();

    [ObservableProperty]
    public partial double StartX { get; set; }

    [ObservableProperty]
    public partial double StartY { get; set; }

    [ObservableProperty]
    public partial double EndX { get; set; }

    [ObservableProperty]
    public partial double EndY { get; set; }

    [ObservableProperty]
    public partial double Length { get; set; }

    [ObservableProperty]
    public partial double AngleDegrees { get; set; }

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
        if (!TryGetLine(out var line))
            return;

        _isRefreshing = true;
        try
        {
            RefreshLayerOptions(_documentViewModel, line);
            StartX = line.Start.X;
            StartY = line.Start.Y;
            EndX = line.End.X;
            EndY = line.End.Y;
            RefreshDerivedGeometry(line.Start, line.End);
            StrokeColor = ResolveStrokeColor(line);
            UseByLayerColor = line.UseLayerColor;
            UseByLayerLineWeight = line.UseLayerLineWeight;
            LineWeight = ResolveLineWeight(line).Value;
            ZIndex = line.ZIndex;
            IsVisible = line.IsVisible;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    partial void OnStartXChanged(double value) => CommitPointGeometryChange();

    partial void OnStartYChanged(double value) => CommitPointGeometryChange();

    partial void OnEndXChanged(double value) => CommitPointGeometryChange();

    partial void OnEndYChanged(double value) => CommitPointGeometryChange();

    partial void OnLengthChanged(double value) => CommitPolarGeometryChange();

    partial void OnAngleDegreesChanged(double value) => CommitPolarGeometryChange();

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || UseByLayerColor || !TryGetLine(out var line) || ResolveStrokeColor(line) == value)
            return;

        _documentViewModel.CadEditor.SetEntityColor(EntityId, value);
    }

    partial void OnUseByLayerColorChanged(bool value)
    {
        OnPropertyChanged(nameof(ColorControlsEnabled));

        if (_isRefreshing || !TryGetLine(out _))
            return;

        _documentViewModel.CadEditor.SetEntityUseLayerColor(EntityId, value);
        RefreshFromEntity();
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        OnPropertyChanged(nameof(LineWeightControlsEnabled));

        if (_isRefreshing || !TryGetLine(out _))
            return;

        _documentViewModel.CadEditor.SetEntityLineWeight(
            EntityId,
            value ? CadLineWeight.ByLayer : new CadLineWeight(ResolveLineWeightValue(LineWeight)));
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing || UseByLayerLineWeight || !TryGetLine(out _))
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
        if (_isRefreshing || !TryGetLine(out var line) || line.ZIndex == value)
            return;

        _documentViewModel.CadEditor.SetEntityZIndex(EntityId, value);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing || !TryGetLine(out var line) || line.IsVisible == value)
            return;

        _documentViewModel.CadEditor.SetEntityVisibility(EntityId, value);
    }

    private void CommitPointGeometryChange()
    {
        if (_isRefreshing || _isUpdatingGeometryProperties || !TryGetLine(out _))
            return;

        if (!TryCreatePointGeometry(out var start, out var end))
        {
            RefreshFromEntity();
            return;
        }

        _isUpdatingGeometryProperties = true;
        try
        {
            RefreshDerivedGeometry(start, end);
        }
        finally
        {
            _isUpdatingGeometryProperties = false;
        }

        CommitGeometry(start, end);
    }

    private void CommitPolarGeometryChange()
    {
        if (_isRefreshing || _isUpdatingGeometryProperties || !TryGetLine(out _))
            return;

        if (!TryCreatePolarGeometry(out var start, out var end))
        {
            RefreshFromEntity();
            return;
        }

        _isUpdatingGeometryProperties = true;
        try
        {
            EndX = end.X;
            EndY = end.Y;
        }
        finally
        {
            _isUpdatingGeometryProperties = false;
        }

        CommitGeometry(start, end);
    }

    private void CommitGeometry(CadPointD start, CadPointD end)
    {
        if (!TryGetLine(out var line))
            return;

        if (start.DistanceSquaredTo(line.Start) <= Epsilon &&
            end.DistanceSquaredTo(line.End) <= Epsilon)
        {
            return;
        }

        _documentViewModel.CadEditor.SetLineGeometry(EntityId, start, end);
    }

    private bool TryCreatePointGeometry(out CadPointD start, out CadPointD end)
    {
        start = new CadPointD(StartX, StartY);
        end = new CadPointD(EndX, EndY);

        return IsFinite(StartX) &&
               IsFinite(StartY) &&
               IsFinite(EndX) &&
               IsFinite(EndY) &&
               start.DistanceSquaredTo(end) > Epsilon;
    }

    private bool TryCreatePolarGeometry(out CadPointD start, out CadPointD end)
    {
        start = new CadPointD(StartX, StartY);
        var angleRadians = DegreesToRadians(AngleDegrees);
        end = new CadPointD(
            start.X + Math.Cos(angleRadians) * Length,
            start.Y + Math.Sin(angleRadians) * Length);

        return IsFinite(StartX) &&
               IsFinite(StartY) &&
               IsFinitePositive(Length) &&
               IsFinite(AngleDegrees);
    }

    private void RefreshDerivedGeometry(CadPointD start, CadPointD end)
    {
        var delta = end - start;
        Length = delta.Length;
        AngleDegrees = RadiansToDegrees(Math.Atan2(delta.Y, delta.X));
    }

    private bool TryGetLine(out CadLine line)
    {
        if (_documentViewModel.CadEditor.Document.TryGetEntity(EntityId, out var entity) &&
            entity is CadLine currentLine &&
            !currentLine.IsErased)
        {
            line = currentLine;
            return true;
        }

        line = null!;
        return false;
    }

    private CadColor ResolveStrokeColor(CadLine line)
    {
        return ResolveStrokeColor(_documentViewModel.CadEditor.Document, line, line.GraphicStyleId);
    }

    private CadLineWeight ResolveLineWeight(CadLine line)
    {
        return ResolveEntityLineWeight(_documentViewModel.CadEditor.Document, line, line.GraphicStyleId);
    }

    private static double ResolveLineWeightValue(double value)
    {
        return IsFinitePositive(value) ? value : CadLineWeight.Default.Value;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && IsFinite(value);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

public partial class TransientLinePropertyViewModel : EntityPropertyViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public TransientLinePropertyViewModel(CadDocumentViewModel documentViewModel)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        RefreshFromDocument();
    }

    public CadDocumentViewModel DocumentViewModel => _documentViewModel;

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
            RefreshDrawingLayerOptions(_documentViewModel);
            StrokeColor = _documentViewModel.DrawingDefaults.LineStrokeColor;
            LineWeight = _documentViewModel.DrawingDefaults.LineLineWeight;
            ZIndex = _documentViewModel.DrawingDefaults.LineZIndex;
            IsVisible = _documentViewModel.DrawingDefaults.LineIsVisible;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.LineStrokeColor = value;
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.LineLineWeight = IsFinitePositive(value)
            ? value
            : CadLineWeight.Default.Value;
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.LineZIndex = value;
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.LineIsVisible = value;
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
