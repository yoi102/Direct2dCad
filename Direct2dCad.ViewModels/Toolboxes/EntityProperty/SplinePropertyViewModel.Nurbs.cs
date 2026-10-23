using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public partial class SplinePropertyViewModel
{
    public ObservableCollection<NurbsControlPointViewModel> ControlPoints { get; } = [];
    [ObservableProperty] public partial bool IsFitDefined { get; private set; }
    public bool IsControlDefined => !IsFitDefined;
    public int EditPointCount => IsFitDefined ? FitPoints.Count : ControlPoints.Count;
    [ObservableProperty] public partial int Degree { get; set; }
    [ObservableProperty] public partial string KnotVector { get; set; } = "";
    [ObservableProperty] public partial string GeometryError { get; private set; } = "";
    [ObservableProperty] public partial NurbsControlPointViewModel? SelectedControlPoint { get; set; }

    private void RefreshNurbs(CadSpline spline)
    {
        IsFitDefined = spline.Geometry.IsFitDefined;
        OnPropertyChanged(nameof(IsControlDefined));
        Degree = spline.Degree;
        KnotVector = string.Join(" ", spline.Knots.Select(k => k.ToString("G17", CultureInfo.InvariantCulture)));
        while (ControlPoints.Count > spline.Nurbs.EditablePointCount) ControlPoints.RemoveAt(ControlPoints.Count - 1);
        for (var i = 0; i < spline.Nurbs.EditablePointCount; i++)
        {
            var point = ToDisplayPoint(spline.ControlPoints[i]);
            if (i == ControlPoints.Count)
            {
                var row = new NurbsControlPointViewModel(i);
                row.PropertyChanged += (_, e) =>
                {
                    if (!_isRefreshing && e.PropertyName is nameof(row.X) or nameof(row.Y) or nameof(row.Weight)) CommitControlPoint(row);
                };
                ControlPoints.Add(row);
            }
            ControlPoints[i].X = point.X;
            ControlPoints[i].Y = point.Y;
            ControlPoints[i].Weight = spline.Weights[i];
        }
        if (SelectedControlPoint is null || !ControlPoints.Contains(SelectedControlPoint)) SelectedControlPoint = ControlPoints.FirstOrDefault();
        OnPropertyChanged(nameof(EditPointCount));
    }

    private void CommitControlPoint(NurbsControlPointViewModel row)
    {
        if (!IsControlDefined || !TryGetSpline(out var spline)) return;
        ApplyGeometry(() => spline.Geometry.WithControlPoint(row.Index, ToModelPoint(new(row.X, row.Y)), row.Weight));
    }

    // Explicit application lets users finish a degree/knot edit before validation and undo recording.
    [RelayCommand]
    private void ApplyKnotVector()
    {
        if (!IsControlDefined || !TryGetSpline(out var spline)) return;
        ApplyGeometry(() => new CadSplineGeometry(new CadNurbsCurve(Degree, spline.ControlPoints,
            KnotVector.Split([' ', ',', ';', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(s => double.Parse(s, CultureInfo.InvariantCulture)), spline.Weights, spline.Nurbs.IsPeriodic), spline.Closed));
    }

    [RelayCommand]
    private void ApplyUniformKnots()
    {
        if (!IsControlDefined || !TryGetSpline(out var spline)) return;
        ApplyGeometry(() => new CadSplineGeometry(CadNurbsCurve.CreateUniform(spline.Geometry.EditPoints, Degree,
            spline.Weights.Take(spline.Nurbs.EditablePointCount), spline.Closed), spline.Closed));
    }

    [RelayCommand]
    private void AddControlPoint()
    {
        if (!IsControlDefined || !TryGetSpline(out var spline)) return;
        var points = spline.Geometry.EditPoints.ToList();
        var weights = spline.Weights.Take(points.Count).ToList();
        var index = SelectedControlPoint?.Index ?? points.Count - 1;
        var next = index + 1 < points.Count ? points[index + 1] : points[index] + (points[index] - points[Math.Max(0, index - 1)]);
        points.Insert(index + 1, new((points[index].X + next.X) / 2, (points[index].Y + next.Y) / 2));
        weights.Insert(index + 1, weights[index]);
        ApplyGeometry(() => new CadSplineGeometry(CadNurbsCurve.CreateUniform(points, spline.Degree, weights, spline.Closed), spline.Closed));
    }

    [RelayCommand]
    private void RemoveControlPoint()
    {
        if (!IsControlDefined || SelectedControlPoint is null || !TryGetSpline(out var spline)) return;
        var points = spline.Geometry.EditPoints.ToList();
        if (points.Count <= Math.Max(spline.Degree + 1, spline.Closed ? 3 : 2)) return;
        var weights = spline.Weights.Take(points.Count).ToList();
        points.RemoveAt(SelectedControlPoint.Index); weights.RemoveAt(SelectedControlPoint.Index);
        ApplyGeometry(() => new CadSplineGeometry(CadNurbsCurve.CreateUniform(points, spline.Degree, weights, spline.Closed), spline.Closed));
    }

    private void ApplyGeometry(Func<CadSplineGeometry> create)
    {
        try
        {
            var geometry = create();
            _documentViewModel.CadEditor.SetSplineGeometry(EntityId, geometry);
            GeometryError = "";
            RefreshFromEntity();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            GeometryError = ex.Message;
        }
    }
}

public partial class NurbsControlPointViewModel(int index) : ObservableObject
{
    public int Index { get; } = index;
    [ObservableProperty] public partial double X { get; set; }
    [ObservableProperty] public partial double Y { get; set; }
    [ObservableProperty] public partial double Weight { get; set; } = 1;
}
