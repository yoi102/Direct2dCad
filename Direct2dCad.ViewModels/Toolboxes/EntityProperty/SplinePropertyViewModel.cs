using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class SplinePropertyViewModel : EntityPropertyViewModel
{
    private const double Epsilon = 1e-9;
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public SplinePropertyViewModel(CadDocumentViewModel documentViewModel, EntityId entityId)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        EntityId = entityId;
        RefreshFromEntity();
    }

    public EntityId EntityId { get; }
    public string EntityIdText => EntityId.ToString();
    [ObservableProperty]
    public partial ObservableCollection<PolylineVertexPropertyViewModel> FitPoints { get; private set; } = [];
    public IReadOnlyList<FillStyleOption> FillStyleOptions { get; private set; } = [];
    public int FitPointCount => FitPoints.Count;
    public double Length => TryGetSpline(out var spline) ? spline.Length : CalculateLength(FitPoints.Select(x => x.ToPoint()), IsClosed);

    [ObservableProperty]
    public partial PolylineVertexPropertyViewModel? SelectedFitPoint { get; set; }

    [ObservableProperty]
    public partial bool IsClosed { get; set; }

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

    public bool FillControlsEnabled => IsClosed;

    public bool FillColorControlsEnabled => IsClosed && CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption);

    public bool LineWeightControlsEnabled => !UseByLayerLineWeight;

    public void RefreshFromEntity()
    {
        if (!TryGetSpline(out var spline))
            return;

        _isRefreshing = true;
        try
        {
            RefreshLayerOptions(_documentViewModel, spline);
            RebuildFitPoints(spline.FitPoints);
            IsClosed = spline.Closed;
            RefreshFillStyleOptions(spline.FillStyleId);
            FillColor = CirclePropertyViewModel.ResolveFillColor(_documentViewModel.CadEditor.Document, spline.FillStyleId);
            StrokeColor = ResolveStrokeColor(spline);
            UseByLayerColor = spline.UseLayerColor;
            UseByLayerLineWeight = spline.UseLayerLineWeight;
            LineWeight = ResolveLineWeight(spline).Value;
            ZIndex = spline.ZIndex;
            IsVisible = spline.IsVisible;
        }
        finally
        {
            _isRefreshing = false;
        }

        RaiseGeometrySummaryChanged();
    }

    partial void OnIsClosedChanged(bool value)
    {
        if (_isRefreshing)
            return;

        if (value && FitPoints.Count < 3)
        {
            _isRefreshing = true;
            try
            {
                IsClosed = false;
            }
            finally
            {
                _isRefreshing = false;
            }
            return;
        }

        CommitGeometryFromFitPoints();
        OnPropertyChanged(nameof(FillControlsEnabled));
        OnPropertyChanged(nameof(FillColorControlsEnabled));
    }

    partial void OnSelectedFillStyleOptionChanged(FillStyleOption? value)
    {
        OnPropertyChanged(nameof(FillColorControlsEnabled));

        if (_isRefreshing || !IsClosed || !TryGetSpline(out var spline))
            return;

        var fillStyleId = CirclePropertyViewModel.ResolveFillStyleId(_documentViewModel.CadEditor.Document, value, FillColor);
        if (Nullable.Equals(spline.FillStyleId, fillStyleId))
            return;

        _documentViewModel.CadEditor.SetEntityFillStyle(EntityId, fillStyleId);
    }

    partial void OnFillColorChanged(CadColor value)
    {
        if (_isRefreshing ||
            !IsClosed ||
            !CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption) ||
            !TryGetSpline(out var spline))
        {
            return;
        }

        var fillStyleId = CirclePropertyViewModel.ResolveFillStyleId(_documentViewModel.CadEditor.Document, SelectedFillStyleOption, value);
        if (Nullable.Equals(spline.FillStyleId, fillStyleId))
            return;

        _documentViewModel.CadEditor.SetEntityFillStyle(EntityId, fillStyleId);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || UseByLayerColor || !TryGetSpline(out var spline) || ResolveStrokeColor(spline) == value)
            return;

        _documentViewModel.CadEditor.SetEntityColor(EntityId, value);
    }

    partial void OnUseByLayerColorChanged(bool value)
    {
        OnPropertyChanged(nameof(ColorControlsEnabled));

        if (_isRefreshing || !TryGetSpline(out _))
            return;

        _documentViewModel.CadEditor.SetEntityUseLayerColor(EntityId, value);
        RefreshFromEntity();
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        OnPropertyChanged(nameof(LineWeightControlsEnabled));

        if (_isRefreshing || !TryGetSpline(out _))
            return;

        _documentViewModel.CadEditor.SetEntityLineWeight(
            EntityId,
            value ? CadLineWeight.ByLayer : new CadLineWeight(ResolveLineWeightValue(LineWeight)));
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing || UseByLayerLineWeight || !TryGetSpline(out _))
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
        if (_isRefreshing || !TryGetSpline(out var spline) || spline.ZIndex == value)
            return;

        _documentViewModel.CadEditor.SetEntityZIndex(EntityId, value);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing || !TryGetSpline(out var spline) || spline.IsVisible == value)
            return;

        _documentViewModel.CadEditor.SetEntityVisibility(EntityId, value);
    }

    [RelayCommand]
    private void AddFitPointAfterSelected()
    {
        if (FitPoints.Count == 0)
            return;

        var insertAfter = SelectedFitPoint is not null
            ? Math.Max(0, FitPoints.IndexOf(SelectedFitPoint))
            : FitPoints.Count - 1;
        if (insertAfter < 0)
            insertAfter = FitPoints.Count - 1;

        var point = CreateInsertedPoint(insertAfter);
        var fitPoint = CreateFitPoint(insertAfter + 1, point);
        FitPoints.Insert(insertAfter + 1, fitPoint);
        ReindexFitPoints();
        SelectedFitPoint = fitPoint;
        CommitGeometryFromFitPoints();
    }

    [RelayCommand]
    private void RemoveSelectedFitPoint()
    {
        if (SelectedFitPoint is null)
            return;

        var minimumFitPointCount = IsClosed ? 3 : 2;
        if (FitPoints.Count <= minimumFitPointCount)
            return;

        var index = FitPoints.IndexOf(SelectedFitPoint);
        if (index < 0)
            return;

        SelectedFitPoint.Changed -= OnFitPointChanged;
        FitPoints.RemoveAt(index);
        ReindexFitPoints();
        SelectedFitPoint = FitPoints.Count > 0
            ? FitPoints[Math.Min(index, FitPoints.Count - 1)]
            : null;
        CommitGeometryFromFitPoints();
    }

    private void OnFitPointChanged(object? sender, EventArgs e)
    {
        if (_isRefreshing)
            return;

        CommitGeometryFromFitPoints();
    }

    private void CommitGeometryFromFitPoints()
    {
        if (_isRefreshing || !TryGetSpline(out var spline))
            return;

        var fitPoints = FitPoints.Select(point => point.ToPoint()).ToArray();
        if (!TryValidateGeometry(fitPoints, IsClosed))
        {
            RefreshFromEntity();
            return;
        }

        if (GeometryMatches(spline, fitPoints, IsClosed))
            return;

        _documentViewModel.CadEditor.SetSplineGeometry(EntityId, fitPoints, IsClosed);
        RaiseGeometrySummaryChanged();
    }

    private void RebuildFitPoints(IReadOnlyList<CadPointD> fitPoints)
    {
        if (FitPointCollectionMatches(FitPoints, fitPoints))
            return;

        var selectedIndex = SelectedFitPoint is not null
            ? FitPoints.IndexOf(SelectedFitPoint)
            : 0;

        foreach (var fitPoint in FitPoints)
            fitPoint.Changed -= OnFitPointChanged;

        var newFitPoints = new ObservableCollection<PolylineVertexPropertyViewModel>();
        for (var i = 0; i < fitPoints.Count; i++)
            newFitPoints.Add(CreateFitPoint(i, fitPoints[i]));

        FitPoints = newFitPoints;
        SelectedFitPoint = FitPoints.Count > 0
            ? FitPoints[Math.Clamp(Math.Max(selectedIndex, 0), 0, FitPoints.Count - 1)]
            : null;
    }

    private static bool FitPointCollectionMatches(
        IReadOnlyList<PolylineVertexPropertyViewModel> fitPoints,
        IReadOnlyList<CadPointD> points)
    {
        if (fitPoints.Count != points.Count)
            return false;

        for (var i = 0; i < points.Count; i++)
        {
            if (fitPoints[i].Index != i ||
                fitPoints[i].ToPoint().DistanceSquaredTo(points[i]) > Epsilon)
            {
                return false;
            }
        }

        return true;
    }

    private PolylineVertexPropertyViewModel CreateFitPoint(int index, CadPointD point)
    {
        var fitPoint = new PolylineVertexPropertyViewModel(index, point);
        fitPoint.Changed += OnFitPointChanged;
        return fitPoint;
    }

    private void ReindexFitPoints()
    {
        for (var i = 0; i < FitPoints.Count; i++)
            FitPoints[i].Update(i, FitPoints[i].ToPoint());

        RaiseGeometrySummaryChanged();
    }

    private CadPointD CreateInsertedPoint(int insertAfter)
    {
        var current = FitPoints[insertAfter].ToPoint();

        if (insertAfter + 1 < FitPoints.Count)
        {
            var next = FitPoints[insertAfter + 1].ToPoint();
            return new CadPointD(
                (current.X + next.X) * 0.5,
                (current.Y + next.Y) * 0.5);
        }

        if (insertAfter > 0)
        {
            var previous = FitPoints[insertAfter - 1].ToPoint();
            var direction = current - previous;
            if (direction.LengthSquared > Epsilon)
                return current + direction;
        }

        return current + CadVectorD.UnitX;
    }

    private bool TryGetSpline(out CadSpline spline)
    {
        if (_documentViewModel.CadEditor.Document.TryGetEntity(EntityId, out var entity) &&
            entity is CadSpline currentSpline &&
            !currentSpline.IsErased)
        {
            spline = currentSpline;
            return true;
        }

        spline = null!;
        return false;
    }

    private void RaiseGeometrySummaryChanged()
    {
        OnPropertyChanged(nameof(FitPointCount));
        OnPropertyChanged(nameof(Length));
    }

    private CadColor ResolveStrokeColor(CadSpline spline)
    {
        return ResolveStrokeColor(_documentViewModel.CadEditor.Document, spline, spline.GraphicStyleId);
    }

    private CadLineWeight ResolveLineWeight(CadSpline spline)
    {
        return ResolveEntityLineWeight(_documentViewModel.CadEditor.Document, spline, spline.GraphicStyleId);
    }

    private void RefreshFillStyleOptions(StyleId? selectedStyleId)
    {
        FillStyleOptions = CirclePropertyViewModel.BuildFillStyleOptions(_documentViewModel.CadEditor.Document);
        OnPropertyChanged(nameof(FillStyleOptions));
        SelectedFillStyleOption = CirclePropertyViewModel.FindFillStyleOption(_documentViewModel.CadEditor.Document, FillStyleOptions, selectedStyleId);
        OnPropertyChanged(nameof(FillControlsEnabled));
        OnPropertyChanged(nameof(FillColorControlsEnabled));
    }

    private static bool GeometryMatches(CadSpline spline, IReadOnlyList<CadPointD> fitPoints, bool closed)
    {
        if (spline.Closed != closed || spline.FitPoints.Count != fitPoints.Count)
            return false;

        for (var i = 0; i < fitPoints.Count; i++)
        {
            if (spline.FitPoints[i].DistanceSquaredTo(fitPoints[i]) > Epsilon)
                return false;
        }

        return true;
    }

    private static bool TryValidateGeometry(IReadOnlyList<CadPointD> fitPoints, bool closed)
    {
        if (fitPoints.Count < 2 || closed && fitPoints.Count < 3)
            return false;

        return fitPoints.All(point => IsFinite(point.X) && IsFinite(point.Y));
    }

    private static double CalculateLength(IEnumerable<CadPointD> fitPoints, bool closed)
    {
        var segments = CadSpline.CreateBezierSegments(fitPoints.ToArray(), closed);
        if (segments.Count == 0)
            return 0;

        var length = 0.0;
        var previous = segments[0].Start;
        foreach (var segment in segments)
        {
            for (var step = 1; step <= 20; step++)
            {
                var point = segment.Evaluate(step / 20.0);
                length += previous.DistanceTo(point);
                previous = point;
            }
        }

        return length;
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

public partial class TransientSplinePropertyViewModel : EntityPropertyViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public TransientSplinePropertyViewModel(CadDocumentViewModel documentViewModel)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        RefreshFromDocument();
    }

    public CadDocumentViewModel DocumentViewModel => _documentViewModel;
    public IReadOnlyList<FillStyleOption> FillStyleOptions { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsClosed { get; set; }

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

    public bool FillControlsEnabled => IsClosed;

    public bool FillColorControlsEnabled => IsClosed && CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption);

    public void RefreshFromDocument()
    {
        _isRefreshing = true;
        try
        {
            RefreshDrawingLayerOptions(_documentViewModel);
            IsClosed = _documentViewModel.DrawingDefaults.SplineClosed;
            RefreshFillStyleOptions(_documentViewModel.DrawingDefaults.SplineFillStyleId);
            FillColor = CirclePropertyViewModel.ResolveFillColor(_documentViewModel.CadEditor.Document, _documentViewModel.DrawingDefaults.SplineFillStyleId);
            StrokeColor = _documentViewModel.DrawingDefaults.SplineStrokeColor;
            LineWeight = _documentViewModel.DrawingDefaults.SplineLineWeight;
            ZIndex = _documentViewModel.DrawingDefaults.SplineZIndex;
            IsVisible = _documentViewModel.DrawingDefaults.SplineIsVisible;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    partial void OnIsClosedChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.SplineClosed = value;
        OnPropertyChanged(nameof(FillControlsEnabled));
        OnPropertyChanged(nameof(FillColorControlsEnabled));
    }

    partial void OnSelectedFillStyleOptionChanged(FillStyleOption? value)
    {
        OnPropertyChanged(nameof(FillColorControlsEnabled));

        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.SplineFillStyleId = CirclePropertyViewModel.ResolveFillStyleId(_documentViewModel.CadEditor.Document, value, FillColor);
    }

    partial void OnFillColorChanged(CadColor value)
    {
        if (_isRefreshing || !CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption))
            return;

        _documentViewModel.DrawingDefaults.SplineFillStyleId = CirclePropertyViewModel.ResolveFillStyleId(
            _documentViewModel.CadEditor.Document,
            SelectedFillStyleOption,
            value);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.SplineStrokeColor = value;
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.SplineLineWeight = IsFinitePositive(value)
            ? value
            : CadLineWeight.Default.Value;
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.SplineZIndex = value;
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingDefaults.SplineIsVisible = value;
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private void RefreshFillStyleOptions(StyleId? selectedStyleId)
    {
        FillStyleOptions = CirclePropertyViewModel.BuildFillStyleOptions(_documentViewModel.CadEditor.Document);
        OnPropertyChanged(nameof(FillStyleOptions));
        SelectedFillStyleOption = CirclePropertyViewModel.FindFillStyleOption(_documentViewModel.CadEditor.Document, FillStyleOptions, selectedStyleId);
        OnPropertyChanged(nameof(FillControlsEnabled));
        OnPropertyChanged(nameof(FillColorControlsEnabled));
    }
}
