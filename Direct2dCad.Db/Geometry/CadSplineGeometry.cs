using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Db.Geometry;

/// <summary>Immutable spline definition; fit points are retained only for a fit-defined curve.</summary>
public sealed class CadSplineGeometry
{
    public CadNurbsCurve Curve { get; }
    public IReadOnlyList<CadPointD> FitPoints { get; }
    public bool Closed { get; }
    public bool IsFitDefined => FitPoints.Count > 0;
    public IReadOnlyList<CadPointD> EditPoints { get; }
    public CadSplineGeometry(CadNurbsCurve curve, bool closed = false, IEnumerable<CadPointD>? fitPoints = null)
    {
        Curve = curve ?? throw new ArgumentNullException(nameof(curve));
        Closed = closed;
        FitPoints = Array.AsReadOnly(fitPoints?.ToArray() ?? []);
        EditPoints = IsFitDefined ? FitPoints : Array.AsReadOnly(Curve.ControlPoints.Take(Curve.EditablePointCount).ToArray());
        if (FitPoints.Count == 1 || FitPoints.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) throw new ArgumentException("Invalid spline fit points.");
        if (IsFitDefined)
        {
            var segments = CadSpline.CreateBezierSegments(FitPoints, closed);
            var expected = segments.Count == 0 ? [] : new[] { segments[0].Start }.Concat(segments.SelectMany(s => new[] { s.Control1, s.Control2, s.End })).ToArray();
            var tolerance = Math.Max(1e-8, Math.Max(curve.Bounds.Width, curve.Bounds.Height) * 1e-10);
            if (curve.Degree != 3 || curve.IsRational || curve.IsPeriodic || expected.Length != curve.ControlPoints.Count ||
                expected.Where((p, i) => p.DistanceTo(curve.ControlPoints[i]) > tolerance).Any() ||
                curve.Knots.Where((k, i) => Math.Abs(k - (i < 4 ? 0 : i >= curve.Knots.Count - 4 ? 1 : (double)((i - 4) / 3 + 1) / segments.Count)) > 1e-12).Any())
                throw new ArgumentException("Fit points do not describe the supplied NURBS. Clear fit metadata before editing controls, weights or knots.");
        }
        if (curve.IsPeriodic && !closed) throw new ArgumentException("A periodic spline must be closed.");
        if (closed && curve.Evaluate(curve.ParameterStart).DistanceTo(curve.Evaluate(curve.ParameterEnd)) > 1e-7)
            throw new ArgumentException("A closed NURBS must have coincident endpoints.");
    }
    // Fit construction commutes with affine transforms. Reconstructing its controls avoids
    // independent rounding of fit/control coordinates breaking the metadata after a far move.
    public CadSplineGeometry Transform(Func<CadPointD, CadPointD> transform) => IsFitDefined
        ? FromFitPoints(FitPoints.Select(transform), Closed) : new(Curve.Transform(transform), Closed);
    public CadSplineGeometry WithClosed(bool closed)
    {
        if (Closed == closed) return this;
        if (IsFitDefined) return FromFitPoints(FitPoints, closed);
        return new(CadNurbsCurve.CreateUniform(EditPoints, Curve.Degree, Curve.Weights.Take(Curve.EditablePointCount), closed), closed);
    }
    public CadSplineGeometry WithEditPoints(IEnumerable<CadPointD> points)
    {
        var array = points.ToArray();
        if (IsFitDefined) return FromFitPoints(array, Closed);
        if (array.Length != Curve.EditablePointCount) throw new ArgumentException("Control count must be preserved when moving spline grips.");
        if (Closed && !Curve.IsPeriodic && Curve.ControlPoints[0] == Curve.ControlPoints[^1])
        {
            if (array[0] != EditPoints[0]) array[^1] = array[0];
            else if (array[^1] != EditPoints[^1]) array[0] = array[^1];
        }
        return new(new CadNurbsCurve(Curve.Degree, Curve.IsPeriodic ? array.Concat(array.Take(Curve.Degree)) : array,
            Curve.Knots, Curve.Weights, Curve.IsPeriodic), Closed);
    }
    public CadSplineGeometry WithControlPoint(int index, CadPointD point, double weight)
    {
        var curve = Curve.WithControlPoint(index, point, weight);
        if (Closed && !Curve.IsPeriodic && Curve.ControlPoints[0] == Curve.ControlPoints[^1])
        {
            if (index == 0) curve = curve.WithControlPoint(Curve.EditablePointCount - 1, point, weight);
            else if (index == Curve.EditablePointCount - 1) curve = curve.WithControlPoint(0, point, weight);
        }
        return new(curve, Closed);
    }
    public static CadSplineGeometry FromFitPoints(IEnumerable<CadPointD> points, bool closed = false)
    {
        var fit = points.ToArray();
        if (fit.Length < (closed ? 3 : 2)) throw new ArgumentException("Not enough spline fit points.");
        // Exact migration of the existing interpolant into a cubic B-spline with internal multiplicity three.
        var segments = CadSpline.CreateBezierSegments(fit, closed);
        var controls = new List<CadPointD> { segments[0].Start };
        foreach (var segment in segments) controls.AddRange([segment.Control1, segment.Control2, segment.End]);
        var knots = new List<double> { 0, 0, 0, 0 };
        for (var i = 1; i < segments.Count; i++) knots.AddRange(Enumerable.Repeat((double)i / segments.Count, 3));
        knots.AddRange([1, 1, 1, 1]);
        return new(new CadNurbsCurve(3, controls, knots), closed, fit);
    }
}
