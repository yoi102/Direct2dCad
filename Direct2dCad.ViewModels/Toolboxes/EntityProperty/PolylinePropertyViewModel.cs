using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class PolylineVertexPropertyViewModel : ObservableObject
{
    private bool _isRefreshing;

    public PolylineVertexPropertyViewModel(int index, CadPointD point)
    {
        Update(index, point);
    }

    public event EventHandler? Changed;

    [ObservableProperty]
    public partial int Index { get; private set; }

    [ObservableProperty]
    public partial double X { get; set; }

    [ObservableProperty]
    public partial double Y { get; set; }

    public CadPointD ToPoint() => new(X, Y);

    public void Update(int index, CadPointD point)
    {
        _isRefreshing = true;
        try
        {
            Index = index;
            X = point.X;
            Y = point.Y;
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    partial void OnXChanged(double value) => RaiseChanged();

    partial void OnYChanged(double value) => RaiseChanged();

    private void RaiseChanged()
    {
        if (!_isRefreshing)
            Changed?.Invoke(this, EventArgs.Empty);
    }
}

public partial class PolylinePropertyViewModel : EntityPropertyViewModel
{
    private const double Epsilon = 1e-9;
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public PolylinePropertyViewModel(CadDocumentViewModel documentViewModel, EntityId entityId)
    {
        _documentViewModel = documentViewModel ?? throw new ArgumentNullException(nameof(documentViewModel));
        EntityId = entityId;
        RefreshFromEntity();
    }

    public EntityId EntityId { get; }
    public string EntityIdText => EntityId.ToString();
    [ObservableProperty]
    public partial ObservableCollection<PolylineVertexPropertyViewModel> Vertices { get; private set; } = [];
    public IReadOnlyList<FillStyleOption> FillStyleOptions { get; private set; } = [];
    public int PointCount => Vertices.Count;
    public double Length => TryGetPolyline(out var polyline) ? polyline.Length : CalculateLength(Vertices.Select(x => x.ToPoint()), IsClosed);

    [ObservableProperty]
    public partial PolylineVertexPropertyViewModel? SelectedVertex { get; set; }

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

    public bool FillColorControlsEnabled => CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption);

    public bool LineWeightControlsEnabled => !UseByLayerLineWeight;

    public void RefreshFromEntity()
    {
        if (!TryGetPolyline(out var polyline))
            return;

        _isRefreshing = true;
        try
        {
            RefreshLayerOptions(_documentViewModel, polyline);
            RebuildVertices(polyline.Points);
            IsClosed = polyline.Closed;
            RefreshFillStyleOptions(polyline.FillStyleId);
            FillColor = CirclePropertyViewModel.ResolveFillColor(_documentViewModel.CadEditor.Document, polyline.FillStyleId);
            StrokeColor = ResolveStrokeColor(polyline);
            UseByLayerColor = polyline.UseLayerColor;
            UseByLayerLineWeight = polyline.UseLayerLineWeight;
            LineWeight = ResolveLineWeight(polyline).Value;
            ZIndex = polyline.ZIndex;
            IsVisible = polyline.IsVisible;
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

        if (value && Vertices.Count < 3)
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

        CommitGeometryFromVertices();
    }

    partial void OnSelectedFillStyleOptionChanged(FillStyleOption? value)
    {
        OnPropertyChanged(nameof(FillColorControlsEnabled));

        if (_isRefreshing || !TryGetPolyline(out var polyline))
            return;

        var fillStyleId = CirclePropertyViewModel.ResolveFillStyleId(_documentViewModel.CadEditor.Document, value, FillColor);
        if (Nullable.Equals(polyline.FillStyleId, fillStyleId))
            return;

        _documentViewModel.CadEditor.SetEntityFillStyle(EntityId, fillStyleId);
    }

    partial void OnFillColorChanged(CadColor value)
    {
        if (_isRefreshing || !CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption) || !TryGetPolyline(out var polyline))
            return;

        var fillStyleId = CirclePropertyViewModel.ResolveFillStyleId(_documentViewModel.CadEditor.Document, SelectedFillStyleOption, value);
        if (Nullable.Equals(polyline.FillStyleId, fillStyleId))
            return;

        _documentViewModel.CadEditor.SetEntityFillStyle(EntityId, fillStyleId);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing || UseByLayerColor || !TryGetPolyline(out var polyline) || ResolveStrokeColor(polyline) == value)
            return;

        _documentViewModel.CadEditor.SetEntityColor(EntityId, value);
    }

    partial void OnUseByLayerColorChanged(bool value)
    {
        OnPropertyChanged(nameof(ColorControlsEnabled));

        if (_isRefreshing || !TryGetPolyline(out _))
            return;

        _documentViewModel.CadEditor.SetEntityUseLayerColor(EntityId, value);
        RefreshFromEntity();
    }

    partial void OnUseByLayerLineWeightChanged(bool value)
    {
        OnPropertyChanged(nameof(LineWeightControlsEnabled));

        if (_isRefreshing || !TryGetPolyline(out _))
            return;

        _documentViewModel.CadEditor.SetEntityLineWeight(
            EntityId,
            value ? CadLineWeight.ByLayer : new CadLineWeight(ResolveLineWeightValue(LineWeight)));
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing || UseByLayerLineWeight || !TryGetPolyline(out _))
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
        if (_isRefreshing || !TryGetPolyline(out var polyline) || polyline.ZIndex == value)
            return;

        _documentViewModel.CadEditor.SetEntityZIndex(EntityId, value);
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing || !TryGetPolyline(out var polyline) || polyline.IsVisible == value)
            return;

        _documentViewModel.CadEditor.SetEntityVisibility(EntityId, value);
    }

    [RelayCommand]
    private void AddVertexAfterSelected()
    {
        if (Vertices.Count == 0)
            return;

        var insertAfter = SelectedVertex is not null
            ? Math.Max(0, Vertices.IndexOf(SelectedVertex))
            : Vertices.Count - 1;
        if (insertAfter < 0)
            insertAfter = Vertices.Count - 1;

        var point = CreateInsertedPoint(insertAfter);
        var vertex = CreateVertex(insertAfter + 1, point);
        Vertices.Insert(insertAfter + 1, vertex);
        ReindexVertices();
        SelectedVertex = vertex;
        CommitGeometryFromVertices();
    }

    [RelayCommand]
    private void RemoveSelectedVertex()
    {
        if (SelectedVertex is null)
            return;

        var minimumPointCount = IsClosed ? 3 : 2;
        if (Vertices.Count <= minimumPointCount)
            return;

        var index = Vertices.IndexOf(SelectedVertex);
        if (index < 0)
            return;

        SelectedVertex.Changed -= OnVertexChanged;
        Vertices.RemoveAt(index);
        ReindexVertices();
        SelectedVertex = Vertices.Count > 0
            ? Vertices[Math.Min(index, Vertices.Count - 1)]
            : null;
        CommitGeometryFromVertices();
    }

    private void OnVertexChanged(object? sender, EventArgs e)
    {
        if (_isRefreshing)
            return;

        CommitGeometryFromVertices();
    }

    private void CommitGeometryFromVertices()
    {
        if (_isRefreshing || !TryGetPolyline(out var polyline))
            return;

        var points = Vertices.Select(vertex => vertex.ToPoint()).ToArray();
        if (!TryValidateGeometry(points, IsClosed))
        {
            RefreshFromEntity();
            return;
        }

        if (GeometryMatches(polyline, points, IsClosed))
            return;

        _documentViewModel.CadEditor.SetPolylineGeometry(EntityId, points, IsClosed);
        RaiseGeometrySummaryChanged();
    }

    private void RebuildVertices(IReadOnlyList<CadPointD> points)
    {
        if (VertexCollectionMatches(Vertices, points))
            return;

        var selectedIndex = SelectedVertex is not null
            ? Vertices.IndexOf(SelectedVertex)
            : 0;

        foreach (var vertex in Vertices)
            vertex.Changed -= OnVertexChanged;

        var vertices = new ObservableCollection<PolylineVertexPropertyViewModel>();
        for (var i = 0; i < points.Count; i++)
            vertices.Add(CreateVertex(i, points[i]));

        Vertices = vertices;
        SelectedVertex = Vertices.Count > 0
            ? Vertices[Math.Clamp(Math.Max(selectedIndex, 0), 0, Vertices.Count - 1)]
            : null;
    }

    private static bool VertexCollectionMatches(
        IReadOnlyList<PolylineVertexPropertyViewModel> vertices,
        IReadOnlyList<CadPointD> points)
    {
        if (vertices.Count != points.Count)
            return false;

        for (var i = 0; i < points.Count; i++)
        {
            if (vertices[i].Index != i ||
                vertices[i].ToPoint().DistanceSquaredTo(points[i]) > Epsilon)
            {
                return false;
            }
        }

        return true;
    }

    private PolylineVertexPropertyViewModel CreateVertex(int index, CadPointD point)
    {
        var vertex = new PolylineVertexPropertyViewModel(index, point);
        vertex.Changed += OnVertexChanged;
        return vertex;
    }

    private void ReindexVertices()
    {
        for (var i = 0; i < Vertices.Count; i++)
            Vertices[i].Update(i, Vertices[i].ToPoint());

        RaiseGeometrySummaryChanged();
    }

    private CadPointD CreateInsertedPoint(int insertAfter)
    {
        var current = Vertices[insertAfter].ToPoint();

        if (insertAfter + 1 < Vertices.Count)
        {
            var next = Vertices[insertAfter + 1].ToPoint();
            return new CadPointD(
                (current.X + next.X) * 0.5,
                (current.Y + next.Y) * 0.5);
        }

        if (insertAfter > 0)
        {
            var previous = Vertices[insertAfter - 1].ToPoint();
            var direction = current - previous;
            if (direction.LengthSquared > Epsilon)
                return current + direction;
        }

        return current + CadVectorD.UnitX;
    }

    private bool TryGetPolyline(out CadPolyline polyline)
    {
        if (_documentViewModel.CadEditor.Document.TryGetEntity(EntityId, out var entity) &&
            entity is CadPolyline currentPolyline &&
            !currentPolyline.IsErased)
        {
            polyline = currentPolyline;
            return true;
        }

        polyline = null!;
        return false;
    }

    private void RefreshFillStyleOptions(StyleId? selectedStyleId)
    {
        FillStyleOptions = CirclePropertyViewModel.BuildFillStyleOptions(_documentViewModel.CadEditor.Document);
        OnPropertyChanged(nameof(FillStyleOptions));
        SelectedFillStyleOption = CirclePropertyViewModel.FindFillStyleOption(_documentViewModel.CadEditor.Document, FillStyleOptions, selectedStyleId);
        OnPropertyChanged(nameof(FillColorControlsEnabled));
    }

    private void RaiseGeometrySummaryChanged()
    {
        OnPropertyChanged(nameof(PointCount));
        OnPropertyChanged(nameof(Length));
    }

    private CadColor ResolveStrokeColor(CadPolyline polyline)
    {
        return ResolveStrokeColor(_documentViewModel.CadEditor.Document, polyline, polyline.GraphicStyleId);
    }

    private CadLineWeight ResolveLineWeight(CadPolyline polyline)
    {
        return ResolveEntityLineWeight(_documentViewModel.CadEditor.Document, polyline, polyline.GraphicStyleId);
    }

    private static bool GeometryMatches(CadPolyline polyline, IReadOnlyList<CadPointD> points, bool closed)
    {
        if (polyline.Closed != closed || polyline.Points.Count != points.Count)
            return false;

        for (var i = 0; i < points.Count; i++)
        {
            if (polyline.Points[i].DistanceSquaredTo(points[i]) > Epsilon)
                return false;
        }

        return true;
    }

    private static bool TryValidateGeometry(IReadOnlyList<CadPointD> points, bool closed)
    {
        if (points.Count < 2 || closed && points.Count < 3)
            return false;

        return points.All(point => IsFinite(point.X) && IsFinite(point.Y));
    }

    private static double CalculateLength(IEnumerable<CadPointD> points, bool closed)
    {
        var list = points.ToArray();
        if (list.Length < 2)
            return 0;

        var length = 0.0;
        for (var i = 1; i < list.Length; i++)
            length += list[i - 1].DistanceTo(list[i]);

        if (closed && list.Length >= 3)
            length += list[^1].DistanceTo(list[0]);

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

public partial class TransientPolylinePropertyViewModel : EntityPropertyViewModel
{
    private readonly CadDocumentViewModel _documentViewModel;
    private bool _isRefreshing;

    public TransientPolylinePropertyViewModel(CadDocumentViewModel documentViewModel)
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

    public bool FillColorControlsEnabled => CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption);

    public void RefreshFromDocument()
    {
        _isRefreshing = true;
        try
        {
            RefreshDrawingLayerOptions(_documentViewModel);
            IsClosed = _documentViewModel.DrawingPolylineClosed;
            RefreshFillStyleOptions(_documentViewModel.DrawingPolylineFillStyleId);
            FillColor = CirclePropertyViewModel.ResolveFillColor(_documentViewModel.CadEditor.Document, _documentViewModel.DrawingPolylineFillStyleId);
            StrokeColor = _documentViewModel.DrawingPolylineStrokeColor;
            LineWeight = _documentViewModel.DrawingPolylineLineWeight;
            ZIndex = _documentViewModel.DrawingPolylineZIndex;
            IsVisible = _documentViewModel.DrawingPolylineIsVisible;
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

        _documentViewModel.DrawingPolylineClosed = value;
    }

    partial void OnSelectedFillStyleOptionChanged(FillStyleOption? value)
    {
        OnPropertyChanged(nameof(FillColorControlsEnabled));

        if (_isRefreshing)
            return;

        _documentViewModel.DrawingPolylineFillStyleId = CirclePropertyViewModel.ResolveFillStyleId(_documentViewModel.CadEditor.Document, value, FillColor);
    }

    partial void OnFillColorChanged(CadColor value)
    {
        if (_isRefreshing || !CirclePropertyViewModel.SupportsFillColor(SelectedFillStyleOption))
            return;

        _documentViewModel.DrawingPolylineFillStyleId = CirclePropertyViewModel.ResolveFillStyleId(
            _documentViewModel.CadEditor.Document,
            SelectedFillStyleOption,
            value);
    }

    partial void OnStrokeColorChanged(CadColor value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingPolylineStrokeColor = value;
    }

    partial void OnLineWeightChanged(double value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingPolylineLineWeight = IsFinitePositive(value)
            ? value
            : CadLineWeight.Default.Value;
    }

    partial void OnZIndexChanged(int value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingPolylineZIndex = value;
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (_isRefreshing)
            return;

        _documentViewModel.DrawingPolylineIsVisible = value;
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
