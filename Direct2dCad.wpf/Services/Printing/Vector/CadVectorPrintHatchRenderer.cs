using System.Windows.Media;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.wpf.Services.Printing.Vector;

internal static class CadVectorPrintHatchRenderer
{
    public static void Draw(
        DrawingContext context,
        Geometry clipGeometry,
        CadRectD entityBounds,
        CadMatrixD ownerToPaper,
        CadHatchFillStyle style,
        CadHatchPatternDefinition pattern,
        double paperScale)
    {
        if (clipGeometry.IsEmpty() || entityBounds.IsEmpty || pattern.Lines.Count == 0)
            return;

        var pen = new Pen(
            CadVectorPrintStyleResolver.CreateBrush(style.ForegroundColor),
            1.0 / Math.Max(paperScale, double.Epsilon));
        context.PushClip(clipGeometry);
        try
        {
            foreach (var line in pattern.Lines)
            {
                DrawLineFamily(
                    context,
                    entityBounds,
                    ownerToPaper,
                    style,
                    line,
                    pen);
            }
        }
        finally
        {
            context.Pop();
        }
    }

    private static void DrawLineFamily(
        DrawingContext context,
        CadRectD bounds,
        CadMatrixD ownerToPaper,
        CadHatchFillStyle style,
        CadHatchLineDefinition line,
        Pen pen)
    {
        var hatchRotation = DegreesToRadians(style.HatchAngle);
        var angle = DegreesToRadians(line.Angle + style.HatchAngle);
        var direction = new CadVectorD(Math.Cos(angle), Math.Sin(angle)).Normalize();
        if (direction.LengthSquared <= double.Epsilon)
            return;

        var normal = new CadVectorD(-direction.Y, direction.X);
        var offset = Rotate(line.Offset, hatchRotation) * style.HatchScale;
        var normalStep = offset.Dot(normal);
        if (Math.Abs(normalStep) <= 1e-9)
            normalStep = Math.Max(offset.Length, 1e-9);

        var origin = new CadPointD(
                         bounds.MinX + style.HatchOrigin.X,
                         bounds.MaxY + style.HatchOrigin.Y) +
                     Rotate(line.Origin - CadPointD.Origin, hatchRotation) * style.HatchScale;

        Span<CadPointD> corners =
        [
            new(bounds.MinX, bounds.MinY),
            new(bounds.MaxX, bounds.MinY),
            new(bounds.MaxX, bounds.MaxY),
            new(bounds.MinX, bounds.MaxY)
        ];
        var minNormal = double.PositiveInfinity;
        var maxNormal = double.NegativeInfinity;
        var minAlong = double.PositiveInfinity;
        var maxAlong = double.NegativeInfinity;
        foreach (var corner in corners)
        {
            var relative = corner - origin;
            minNormal = Math.Min(minNormal, relative.Dot(normal));
            maxNormal = Math.Max(maxNormal, relative.Dot(normal));
            minAlong = Math.Min(minAlong, relative.Dot(direction));
            maxAlong = Math.Max(maxAlong, relative.Dot(direction));
        }

        var spacing = Math.Abs(normalStep);
        var margin = Math.Max(spacing * 2.0, style.HatchScale * 2.0);
        var firstIndex = (minNormal - margin) / normalStep;
        var lastIndex = (maxNormal + margin) / normalStep;
        var startIndex = (long)Math.Floor(Math.Min(firstIndex, lastIndex)) - 1;
        var endIndex = (long)Math.Ceiling(Math.Max(firstIndex, lastIndex)) + 1;
        var alongStep = offset.Dot(direction);
        var dashLength = ResolveDashLength(line.DashPattern, style.HatchScale);

        for (var index = startIndex; index <= endIndex; index++)
        {
            var basePoint = origin + offset * index;
            var alongOffset = alongStep * index;
            var startDistance = minAlong - alongOffset - margin;
            var endDistance = maxAlong - alongOffset + margin;
            if (line.IsSolidLine || dashLength <= 1e-9)
            {
                DrawSegment(
                    context,
                    ownerToPaper,
                    basePoint + direction * startDistance,
                    basePoint + direction * endDistance,
                    pen);
                continue;
            }

            DrawDashedLine(
                context,
                ownerToPaper,
                basePoint,
                direction,
                startDistance,
                endDistance,
                line.DashPattern,
                style.HatchScale,
                dashLength,
                pen);
        }
    }

    private static void DrawDashedLine(
        DrawingContext context,
        CadMatrixD ownerToPaper,
        CadPointD basePoint,
        CadVectorD direction,
        double startDistance,
        double endDistance,
        IReadOnlyList<double> pattern,
        double scale,
        double patternLength,
        Pen pen)
    {
        var absoluteScale = Math.Abs(scale);
        var position = startDistance;
        var cyclePosition = PositiveModulo(position, patternLength);
        var segmentIndex = 0;
        var segmentOffset = 0.0;
        var consumed = 0.0;
        for (var index = 0; index < pattern.Count; index++)
        {
            var segmentLength = Math.Abs(pattern[index]) * absoluteScale;
            if (segmentLength <= 1e-9)
                continue;
            if (cyclePosition < consumed + segmentLength || index == pattern.Count - 1)
            {
                segmentIndex = index;
                segmentOffset = Math.Max(0.0, cyclePosition - consumed);
                break;
            }
            consumed += segmentLength;
        }

        while (position < endDistance)
        {
            var dash = pattern[segmentIndex];
            var segmentLength = Math.Abs(dash) * absoluteScale;
            if (segmentLength <= 1e-9)
            {
                if (dash >= 0)
                {
                    var point = basePoint + direction * position;
                    DrawSegment(
                        context,
                        ownerToPaper,
                        point,
                        point + direction * Math.Max(1e-6, pen.Thickness),
                        pen);
                }
                segmentIndex = (segmentIndex + 1) % pattern.Count;
                segmentOffset = 0;
                continue;
            }

            var next = Math.Min(endDistance, position + segmentLength - segmentOffset);
            if (dash > 0 && next > position)
            {
                DrawSegment(
                    context,
                    ownerToPaper,
                    basePoint + direction * position,
                    basePoint + direction * next,
                    pen);
            }
            position = next;
            segmentIndex = (segmentIndex + 1) % pattern.Count;
            segmentOffset = 0;
        }
    }

    private static void DrawSegment(
        DrawingContext context,
        CadMatrixD transform,
        CadPointD start,
        CadPointD end,
        Pen pen)
    {
        context.DrawLine(
            pen,
            CadVectorPrintGeometryFactory.ToPoint(transform.TransformPoint(start)),
            CadVectorPrintGeometryFactory.ToPoint(transform.TransformPoint(end)));
    }

    private static double ResolveDashLength(IReadOnlyList<double> pattern, double scale)
    {
        var length = 0.0;
        foreach (var item in pattern)
            length += Math.Abs(item) * Math.Abs(scale);
        return length;
    }

    private static CadVectorD Rotate(CadVectorD vector, double radians)
    {
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);
        return new CadVectorD(
            vector.X * cosine - vector.Y * sine,
            vector.X * sine + vector.Y * cosine);
    }

    private static double PositiveModulo(double value, double divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}
