using System.Numerics;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

internal static class Direct2DHatchRenderer
{
    private const double SolidLodPixelThreshold = 1.0;
    private const int MaxLineSetsPerFamily = 4096;

    public static void Draw(
        ID2D1DeviceContext deviceContext,
        ID2D1Geometry geometry,
        CadRectD geometryBounds,
        CadTransientHatchFill hatchFill,
        ID2D1Brush hatchBrush,
        CadViewport viewport)
    {
        if (hatchFill.Lines.Count == 0 || geometryBounds.IsEmpty)
            return;

        if (ShouldRenderAsSolidFill(hatchFill, viewport))
        {
            deviceContext.FillGeometry(geometry, hatchBrush);
            return;
        }

        var hatchBounds = ResolveRenderBounds(geometryBounds, viewport, hatchFill);
        if (hatchBounds.IsEmpty)
            return;

        var anchoredHatchFill = hatchFill with
        {
            HatchOrigin = ResolveOrigin(geometryBounds, hatchFill)
        };
        var layerParameters = new LayerParameters1
        {
            ContentBounds = ToRawRect(hatchBounds),
            GeometricMask = geometry,
            MaskAntialiasMode = AntialiasMode.PerPrimitive,
            MaskTransform = Matrix3x2.Identity,
            Opacity = 1.0f,
            OpacityBrush = null,
            LayerOptions = LayerOptions1.None
        };

        var previousPrimitiveBlend = deviceContext.PrimitiveBlend;
        var layerPushed = false;
        using var layer = deviceContext.CreateLayer(null);
        try
        {
            deviceContext.PushLayer(ref layerParameters, layer);
            layerPushed = true;
            deviceContext.PrimitiveBlend = PrimitiveBlend.Copy;

            var strokeWidth = 1.0f / Math.Max((float)viewport.Zoom, float.Epsilon);
            foreach (var line in anchoredHatchFill.Lines)
            {
                DrawLineSet(
                    deviceContext,
                    hatchBounds,
                    anchoredHatchFill,
                    line,
                    hatchBrush,
                    strokeWidth,
                    viewport.Zoom);
            }
        }
        finally
        {
            deviceContext.PrimitiveBlend = previousPrimitiveBlend;
            if (layerPushed)
            {
                deviceContext.PrimitiveBlend = PrimitiveBlend.SourceOver;
                deviceContext.PopLayer();
            }

            deviceContext.PrimitiveBlend = previousPrimitiveBlend;
        }
    }

    private static void DrawLineSet(
        ID2D1DeviceContext deviceContext,
        CadRectD bounds,
        CadTransientHatchFill hatchStyle,
        CadHatchLineDefinition line,
        ID2D1Brush brush,
        float strokeWidth,
        double zoom)
    {
        var hatchRotation = DegreesToRadians(hatchStyle.HatchAngle);
        var angleRadians = DegreesToRadians(line.Angle + hatchStyle.HatchAngle);
        var direction = new CadVectorD(Math.Cos(angleRadians), Math.Sin(angleRadians)).Normalize();
        if (direction.LengthSquared <= double.Epsilon)
            return;

        var normal = new CadVectorD(-direction.Y, direction.X);
        var offset = Rotate(line.Offset, hatchRotation) * hatchStyle.HatchScale;
        var normalStep = offset.Dot(normal);
        var spacing = Math.Abs(normalStep);
        if (spacing <= 1e-6)
        {
            spacing = Math.Max(offset.Length, 1e-6);
            normalStep = spacing;
        }

        var signedNormalStep = Math.Abs(normalStep) > 1e-6
            ? normalStep
            : Math.Max(offset.Length, 1e-6);
        var origin = hatchStyle.HatchOrigin +
                     Rotate(line.Origin - CadPointD.Origin, hatchRotation) * hatchStyle.HatchScale;
        var corners = GetBoundsCorners(bounds);
        var minNormal = double.PositiveInfinity;
        var maxNormal = double.NegativeInfinity;
        foreach (var corner in corners)
        {
            var distance = (corner - origin).Dot(normal);
            minNormal = Math.Min(minNormal, distance);
            maxNormal = Math.Max(maxNormal, distance);
        }

        var margin = Math.Max(spacing * 2.0, hatchStyle.HatchScale * 2.0);
        var firstIndex = (minNormal - margin) / signedNormalStep;
        var lastIndex = (maxNormal + margin) / signedNormalStep;
        var startIndex = Math.Floor(Math.Min(firstIndex, lastIndex)) - 1.0;
        var endIndex = Math.Ceiling(Math.Max(firstIndex, lastIndex)) + 1.0;
        var lineSetCount = Math.Max(1.0, endIndex - startIndex + 1.0);
        var indexStep = Math.Max(1.0, Math.Ceiling(lineSetCount / MaxLineSetsPerFamily));
        var drawAsSolidLine = line.IsSolidLine || IsDashCycleSubpixel(line, hatchStyle.HatchScale, zoom);

        for (var index = startIndex; index <= endIndex;)
        {
            var basePoint = origin + offset * index;
            var minAlong = double.PositiveInfinity;
            var maxAlong = double.NegativeInfinity;
            foreach (var corner in corners)
            {
                var distance = (corner - basePoint).Dot(direction);
                minAlong = Math.Min(minAlong, distance);
                maxAlong = Math.Max(maxAlong, distance);
            }

            var startDistance = minAlong - margin;
            var endDistance = maxAlong + margin;
            if (drawAsSolidLine)
            {
                deviceContext.DrawLine(
                    ToVector2(basePoint + direction * startDistance),
                    ToVector2(basePoint + direction * endDistance),
                    brush,
                    strokeWidth);
            }
            else
            {
                DrawDashedLine(
                    deviceContext,
                    basePoint,
                    direction,
                    startDistance,
                    endDistance,
                    line.DashPattern,
                    hatchStyle.HatchScale,
                    brush,
                    strokeWidth);
            }

            var nextIndex = index + indexStep;
            if (nextIndex <= index)
                break;

            index = nextIndex;
        }
    }

    private static void DrawDashedLine(
        ID2D1DeviceContext deviceContext,
        CadPointD basePoint,
        CadVectorD direction,
        double startDistance,
        double endDistance,
        IReadOnlyList<double> dashPattern,
        double scale,
        ID2D1Brush brush,
        float strokeWidth)
    {
        if (endDistance <= startDistance)
            return;

        var patternLength = dashPattern.Sum(value => Math.Abs(value) * scale);
        if (patternLength <= 1e-6)
            return;

        var position = startDistance;
        var cyclePosition = PositiveModulo(position, patternLength);
        var segmentIndex = 0;
        var segmentOffset = 0.0;
        var consumed = 0.0;
        for (var index = 0; index < dashPattern.Count; index++)
        {
            var segmentLength = Math.Abs(dashPattern[index]) * scale;
            if (segmentLength <= 1e-6)
            {
                if (Math.Abs(cyclePosition - consumed) <= 1e-9)
                {
                    segmentIndex = index;
                    segmentOffset = 0.0;
                    break;
                }

                continue;
            }

            if (cyclePosition < consumed + segmentLength || index == dashPattern.Count - 1)
            {
                segmentIndex = index;
                segmentOffset = Math.Max(0.0, cyclePosition - consumed);
                break;
            }

            consumed += segmentLength;
        }

        while (position < endDistance)
        {
            var dash = dashPattern[segmentIndex];
            var segmentLength = Math.Abs(dash) * scale;
            if (segmentLength <= 1e-6)
            {
                if (dash >= 0)
                {
                    var point = basePoint + direction * position;
                    deviceContext.DrawLine(
                        ToVector2(point),
                        ToVector2(point + direction * Math.Max(strokeWidth, 0.01f)),
                        brush,
                        strokeWidth);
                }

                segmentIndex = (segmentIndex + 1) % dashPattern.Count;
                segmentOffset = 0.0;
                continue;
            }

            var next = Math.Min(endDistance, position + segmentLength - segmentOffset);
            if (dash > 0 && next > position)
            {
                deviceContext.DrawLine(
                    ToVector2(basePoint + direction * position),
                    ToVector2(basePoint + direction * next),
                    brush,
                    strokeWidth);
            }

            position = next;
            segmentIndex = (segmentIndex + 1) % dashPattern.Count;
            segmentOffset = 0.0;
        }
    }

    private static bool ShouldRenderAsSolidFill(CadTransientHatchFill hatchFill, CadViewport viewport)
    {
        var zoom = Math.Max(viewport.Zoom, double.Epsilon);
        var scale = Math.Abs(hatchFill.HatchScale);
        if (!double.IsFinite(zoom) || !double.IsFinite(scale) || scale <= double.Epsilon)
            return false;

        var hatchRotation = DegreesToRadians(hatchFill.HatchAngle);
        foreach (var line in hatchFill.Lines)
        {
            var angleRadians = DegreesToRadians(line.Angle + hatchFill.HatchAngle);
            var direction = new CadVectorD(Math.Cos(angleRadians), Math.Sin(angleRadians)).Normalize();
            if (direction.LengthSquared <= double.Epsilon)
                continue;

            var normal = new CadVectorD(-direction.Y, direction.X);
            var offset = Rotate(line.Offset, hatchRotation) * scale;
            var normalSpacing = Math.Abs(offset.Dot(normal));
            if (normalSpacing <= 1e-6)
                normalSpacing = Math.Max(offset.Length, 1e-6);

            if (normalSpacing * zoom > SolidLodPixelThreshold)
                continue;

            if (line.IsSolidLine)
                return true;

            if (IsDashCycleSubpixel(line, scale, zoom))
                return true;
        }

        return false;
    }

    private static bool IsDashCycleSubpixel(
        CadHatchLineDefinition line,
        double scale,
        double zoom)
    {
        if (line.IsSolidLine)
            return false;

        var dashCycleScreenLength = line.DashPattern.Sum(value => Math.Abs(value)) *
                                    Math.Abs(scale) *
                                    Math.Max(zoom, double.Epsilon);
        return dashCycleScreenLength <= SolidLodPixelThreshold;
    }

    private static CadRectD ResolveRenderBounds(
        CadRectD geometryBounds,
        CadViewport viewport,
        CadTransientHatchFill hatchFill)
    {
        var renderBounds = viewport.VisibleWorldBounds.IsEmpty
            ? geometryBounds
            : geometryBounds.Intersection(viewport.VisibleWorldBounds);
        if (renderBounds.IsEmpty)
            return CadRectD.Empty;

        return renderBounds.Inflate(Math.Max(4.0, hatchFill.HatchScale * 4.0));
    }

    private static CadPointD ResolveOrigin(CadRectD entityBounds, CadTransientHatchFill hatchFill)
    {
        return new CadPointD(
            entityBounds.MinX + hatchFill.HatchOrigin.X,
            entityBounds.MaxY + hatchFill.HatchOrigin.Y);
    }

    private static CadPointD[] GetBoundsCorners(CadRectD bounds)
    {
        return
        [
            new CadPointD(bounds.MinX, bounds.MinY),
            new CadPointD(bounds.MaxX, bounds.MinY),
            new CadPointD(bounds.MaxX, bounds.MaxY),
            new CadPointD(bounds.MinX, bounds.MaxY)
        ];
    }

    private static CadVectorD Rotate(CadVectorD vector, double angleRadians)
    {
        var cos = Math.Cos(angleRadians);
        var sin = Math.Sin(angleRadians);
        return new CadVectorD(
            vector.X * cos - vector.Y * sin,
            vector.X * sin + vector.Y * cos);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double PositiveModulo(double value, double divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static RawRectF ToRawRect(CadRectD bounds)
    {
        return new RawRectF(
            (float)bounds.MinX,
            (float)bounds.MinY,
            (float)bounds.MaxX,
            (float)bounds.MaxY);
    }

    private static Vector2 ToVector2(CadPointD point) => new((float)point.X, (float)point.Y);
}
