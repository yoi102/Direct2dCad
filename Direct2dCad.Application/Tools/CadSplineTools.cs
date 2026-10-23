using System.Text.Json;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Application.Tools;

internal static class CadSplineTools
{
    internal static CadSplineGeometry Parse(JsonElement arguments)
    {
        var closed = arguments.TryGetProperty("closed", out var c) && c.GetBoolean();
        if (!arguments.TryGetProperty("control_points", out _))
        {
            if (new[] { "degree", "knots", "weights", "periodic" }.Any(k => arguments.TryGetProperty(k, out _)))
                throw new ArgumentException("NURBS parameters require control_points. Use either fit_points or control_points.");
            return CadSplineGeometry.FromFitPoints(CadDocumentToolExecutor.RequiredPoints(arguments, "fit_points", 2), closed);
        }
        var points = CadDocumentToolExecutor.RequiredPoints(arguments, "control_points", 2);
        var degree = arguments.TryGetProperty("degree", out var d) ? d.GetInt32() : Math.Min(3, points.Length - 1);
        double[]? Array(string name) => arguments.TryGetProperty(name, out var value) ? value.EnumerateArray().Select(x => x.GetDouble()).ToArray() : null;
        var weights = Array("weights"); var knots = Array("knots");
        var periodic = arguments.TryGetProperty("periodic", out var p) && p.GetBoolean();
        if (periodic && !closed) throw new ArgumentException("A periodic spline must be closed.");
        var curve = knots is null ? CadNurbsCurve.CreateUniform(points, degree, weights, closed)
            : new CadNurbsCurve(degree, points, knots, weights, periodic);
        var fit = arguments.TryGetProperty("fit_points", out var f) && f.GetArrayLength() > 0
            ? CadDocumentToolExecutor.RequiredPoints(arguments, "fit_points", 2) : null;
        return new(curve, closed, fit);
    }

    internal static Dictionary<string, object> Properties() => new()
    {
        ["control_points"] = new { type = "array", minItems = 2, maxItems = 100000, items = new { type = "object", properties = new { x = new { type = "number" }, y = new { type = "number" } }, required = new[] { "x", "y" }, additionalProperties = false } },
        ["degree"] = new { type = "integer", minimum = 1, maximum = 16 },
        ["knots"] = new { type = "array", items = new { type = "number" }, description = "Nondecreasing knot vector with control_count + degree + 1 entries. Omit to construct uniform knots." },
        ["weights"] = new { type = "array", items = new { type = "number", exclusiveMinimum = 0.0 }, description = "One positive weight per control; defaults to one." },
        ["periodic"] = new { type = "boolean", description = "For explicit knots, periodic controls/weights repeat the first degree entries at the end." }
    };
}
