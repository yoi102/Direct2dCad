using System.Text;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Text;

public static class CadStrokeFont
{
    public const double DefaultWidthFactor = 0.72;
    public const double DefaultCharacterSpacingFactor = 0.24;
    public const double DefaultObliqueAngleRadians = 0.0;
    private const double LineSpacingFactor = 1.4;

    public static IReadOnlyList<CadStrokeTextSegment> CreateSegments(
        string text,
        CadPointD position,
        double height,
        double widthFactor = DefaultWidthFactor,
        double characterSpacingFactor = DefaultCharacterSpacingFactor,
        double obliqueAngleRadians = DefaultObliqueAngleRadians,
        double rotationRadians = 0,
        CadShapeFontId shapeFontId = default)
    {
        GuardPositive(height, nameof(height));
        GuardPositive(widthFactor, nameof(widthFactor));
        GuardNonNegative(characterSpacingFactor, nameof(characterSpacingFactor));
        GuardFinite(obliqueAngleRadians, nameof(obliqueAngleRadians));
        GuardFinite(rotationRadians, nameof(rotationRadians));

        var shapeFont = CadShapeFontRegistry.GetOrDefault(shapeFontId);
        var content = string.IsNullOrEmpty(text) ? " " : text;
        var glyphWidth = height * widthFactor;
        var spacing = height * characterSpacingFactor;
        var lineAdvance = height * LineSpacingFactor;
        var oblique = Math.Tan(obliqueAngleRadians);
        var cos = Math.Cos(rotationRadians);
        var sin = Math.Sin(rotationRadians);
        var segments = new List<CadStrokeTextSegment>();
        var cursorX = 0.0;
        var cursorY = 0.0;

        foreach (var codePoint in content.EnumerateRunes())
        {
            if (codePoint.Value == '\r')
                continue;

            if (codePoint.Value == '\n')
            {
                cursorX = 0;
                cursorY += lineAdvance;
                continue;
            }

            var glyph = CadShapeGlyphProviderRegistry.ResolveGlyph(codePoint, shapeFont);
            if (glyph.Strokes.Count == 0)
            {
                cursorX += glyphWidth * glyph.Advance + spacing;
                continue;
            }

            foreach (var stroke in glyph.Strokes)
            {
                var start = Transform(stroke.Start, cursorX, cursorY, glyphWidth, height, oblique, cos, sin, position);
                var end = Transform(stroke.End, cursorX, cursorY, glyphWidth, height, oblique, cos, sin, position);
                segments.Add(new CadStrokeTextSegment(start, end));
            }

            cursorX += glyphWidth * glyph.Advance + spacing;
        }

        return segments;
    }

    public static CadRectD MeasureBounds(
        string text,
        CadPointD position,
        double height,
        double widthFactor = DefaultWidthFactor,
        double characterSpacingFactor = DefaultCharacterSpacingFactor,
        double obliqueAngleRadians = DefaultObliqueAngleRadians,
        double rotationRadians = 0,
        CadShapeFontId shapeFontId = default)
    {
        var bounds = CadRectD.Empty;
        foreach (var segment in CreateSegments(
                     text,
                     position,
                     height,
                     widthFactor,
                     characterSpacingFactor,
                     obliqueAngleRadians,
                     rotationRadians,
                     shapeFontId))
        {
            bounds = bounds
                .ExpandToInclude(segment.Start)
                .ExpandToInclude(segment.End);
        }

        return bounds.IsEmpty
            ? CadRectD.FromCenter(position, height, height)
            : bounds;
    }

    private static CadPointD Transform(
        CadShapeGlyphPoint point,
        double cursorX,
        double cursorY,
        double glyphWidth,
        double height,
        double oblique,
        double cos,
        double sin,
        CadPointD position)
    {
        var x = cursorX + point.X * glyphWidth;
        var y = cursorY + point.Y * height;
        x += y * oblique;

        return new CadPointD(
            position.X + x * cos - y * sin,
            position.Y + x * sin + y * cos);
    }

    private static void GuardPositive(double value, string paramName)
    {
        if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(paramName);
    }

    private static void GuardNonNegative(double value, string paramName)
    {
        if (value < 0 || double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(paramName);
    }

    private static void GuardFinite(double value, string paramName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(paramName);
    }

}
