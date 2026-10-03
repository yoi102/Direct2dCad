using System.Numerics;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Resources;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Entities;

/// <summary>
/// Omits whole offscreen edges from long solid strokes. Vertices are never clipped or
/// simplified: artificial figure ends remain outside the stroke's visible extent.
/// Fills and dashed paths must retain their complete topology and dash phase.
/// </summary>
internal static class Direct2DVisiblePolylineStroke
{
    internal const int MinimumPointCount = 512;

    public static bool TryDraw(
        ID2D1DeviceContext context, ID2D1Factory? factory, CadEntity entity,
        ID2D1Geometry geometry, Direct2DResourceCache.EntityResourceBucket resources,
        CadViewport viewport, ID2D1Brush brush, float strokeWidth, ID2D1StrokeStyle? strokeStyle)
    {
        if (entity is not CadPolyline { Points.Count: >= MinimumPointCount } polyline ||
            !ReferenceEquals(geometry, resources.Geometry) ||
            polyline.StrokeStyle.DashStyle != CadStrokeDashStyle.Solid ||
            resources.GraphicLineTypeStrokeStyle is not null || factory is null)
            return false;

        // Retained command lists and transformed block definitions need the full path.
        var viewportTransform = Matrix3x2.CreateScale((float)viewport.Zoom, (float)-viewport.Zoom) *
            Matrix3x2.CreateTranslation((float)viewport.Offset.X, (float)viewport.Offset.Y);
        if (context.Transform != viewportTransform ||
            !TryCreate(factory, polyline, viewport.VisibleWorldBounds, strokeWidth, viewport.Zoom, out var visibleStroke))
            return false;

        using (visibleStroke)
            if (visibleStroke is not null)
                context.DrawGeometry(visibleStroke, brush, strokeWidth, strokeStyle);
        return true;
    }

    public static bool TryCreate(
        ID2D1Factory factory, CadPolyline polyline, CadRectD visibleBounds,
        float strokeWidth, double zoom, out ID2D1PathGeometry? geometry)
    {
        geometry = null;
        var coordinateMagnitude = Math.Max(Math.Max(Math.Abs(polyline.Bounds.MinX), Math.Abs(polyline.Bounds.MaxX)),
            Math.Max(Math.Abs(polyline.Bounds.MinY), Math.Abs(polyline.Bounds.MaxY)));
        // Preserve miter/cap extents, antialias coverage and float-coordinate rounding.
        var bounds = visibleBounds.Inflate(64.0 / zoom + strokeWidth * 10.0 + coordinateMagnitude * 1e-6);
        if (bounds.Contains(polyline.Bounds))
            return false;

        var points = polyline.Points;
        var edgeCount = polyline.Closed ? points.Count : points.Count - 1;
        var firstExcluded = -1;
        for (var i = 0; i < edgeCount; i++)
        {
            if (!Intersects(i))
            {
                firstExcluded = i;
                break;
            }
        }
        if (firstExcluded < 0)
            return false;

        ID2D1PathGeometry? result = null;
        ID2D1GeometrySink? sink = null;
        try
        {
            var figureOpen = false;
            // Start just after an omitted edge so a closed path's original seam
            // stays within one continuous run, preserving its join.
            for (var step = 0; step < edgeCount; step++)
            {
                var i = polyline.Closed ? (firstExcluded + 1 + step) % edgeCount : step;
                if (Intersects(i))
                {
                    if (sink is null)
                    {
                        result = factory.CreatePathGeometry();
                        sink = result.Open();
                    }
                    if (!figureOpen)
                    {
                        sink.BeginFigure(ToVector(points[i]), FigureBegin.Hollow);
                        figureOpen = true;
                    }
                    sink.AddLine(ToVector(points[(i + 1) % points.Count]));
                }
                else if (figureOpen)
                {
                    sink!.EndFigure(FigureEnd.Open);
                    figureOpen = false;
                }
            }
            if (figureOpen)
                sink!.EndFigure(FigureEnd.Open);
            sink?.Close();
            geometry = result;
            return true;
        }
        catch
        {
            result?.Dispose();
            throw;
        }
        finally
        {
            sink?.Dispose();
        }

        bool Intersects(int i)
        {
            var a = ToVector(points[i]);
            var b = ToVector(points[(i + 1) % points.Count]);
            return Math.Min(a.X, b.X) <= bounds.MaxX && Math.Max(a.X, b.X) >= bounds.MinX &&
                   Math.Min(a.Y, b.Y) <= bounds.MaxY && Math.Max(a.Y, b.Y) >= bounds.MinY;
        }
    }

    private static Vector2 ToVector(CadPointD point) => new((float)point.X, (float)point.Y);
}
