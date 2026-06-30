using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Text;

public static class CadStrokeFont
{
    public const double DefaultWidthFactor = 0.72;
    public const double DefaultCharacterSpacingFactor = 0.24;
    public const double DefaultObliqueAngleRadians = 0.0;
    private const double LineSpacingFactor = 1.4;

    private static readonly IReadOnlyDictionary<char, GlyphDefinition> Glyphs = CreateGlyphs();
    private static readonly GlyphDefinition FallbackGlyph = Glyph(1.0, "LT-RT", "RT-RB", "RB-LB", "LB-LT", "LT-RB");
    private static readonly GlyphDefinition CjkFallbackGlyph = Glyph(1.0, "LT-RT", "RT-RB", "RB-LB", "LB-LT", "TC-BC", "LM-RM", "UL-BR", "TR-LB");
    private static readonly GlyphDefinition KanaFallbackGlyph = Glyph(1.0, "LT-RT", "RT-RB", "RB-LB", "LB-LT", "TC-RM", "RM-BC", "LM-RM");

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

        foreach (var sourceChar in content)
        {
            if (sourceChar == '\r')
                continue;

            if (sourceChar == '\n')
            {
                cursorX = 0;
                cursorY += lineAdvance;
                continue;
            }

            var glyph = ResolveGlyph(sourceChar, shapeFont);
            if (sourceChar == ' ')
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
        StrokePoint point,
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

    private static GlyphDefinition ResolveGlyph(char value, CadShapeFont shapeFont)
    {
        if (value == ' ')
            return Glyphs[value];

        if (shapeFont.GlyphSet == CadShapeFontGlyphSet.BoxFallback)
            return FallbackGlyph;

        var key = char.ToUpperInvariant(value);
        if (Glyphs.TryGetValue(key, out var glyph))
            return glyph;

        if (shapeFont.SupportsUnicode && value > 0x7F)
            return ResolveUnicodeFallbackGlyph(value);

        return FallbackGlyph;
    }

    private static GlyphDefinition ResolveUnicodeFallbackGlyph(char value)
    {
        if (value is >= '\u3040' and <= '\u30FF')
            return KanaFallbackGlyph;

        if (value >= '\u2E80')
            return CjkFallbackGlyph;

        return FallbackGlyph;
    }

    private static IReadOnlyDictionary<char, GlyphDefinition> CreateGlyphs()
    {
        var glyphs = new Dictionary<char, GlyphDefinition>
        {
            [' '] = Glyph(0.62),
            ['0'] = Glyph(1.0, "LT-RT", "RT-RB", "RB-LB", "LB-LT", "LT-RB"),
            ['1'] = Glyph(0.72, "TC-RT", "RT-RB", "LB-RB"),
            ['2'] = Glyph(1.0, "LT-RT", "RT-RM", "RM-LM", "LM-LB", "LB-RB"),
            ['3'] = Glyph(1.0, "LT-RT", "RT-RB", "LM-RM", "LB-RB"),
            ['4'] = Glyph(1.0, "LT-LM", "RT-RB", "LM-RM"),
            ['5'] = Glyph(1.0, "RT-LT", "LT-LM", "LM-RM", "RM-RB", "RB-LB"),
            ['6'] = Glyph(1.0, "RT-LT", "LT-LB", "LB-RB", "RB-RM", "RM-LM"),
            ['7'] = Glyph(1.0, "LT-RT", "RT-RB"),
            ['8'] = Glyph(1.0, "LT-RT", "RT-RB", "RB-LB", "LB-LT", "LM-RM"),
            ['9'] = Glyph(1.0, "RB-RT", "RT-LT", "LT-LM", "LM-RM", "RB-LB"),
            ['A'] = Glyph(1.0, "LB-LT", "LT-RT", "RT-RB", "LM-RM"),
            ['B'] = Glyph(1.0, "LT-LB", "LT-RT", "RT-RM", "RM-LM", "LM-RB", "RB-LB"),
            ['C'] = Glyph(1.0, "RT-LT", "LT-LB", "LB-RB"),
            ['D'] = Glyph(1.0, "LT-LB", "LT-RT", "RT-RB", "RB-LB"),
            ['E'] = Glyph(1.0, "RT-LT", "LT-LB", "LM-RM", "LB-RB"),
            ['F'] = Glyph(1.0, "LT-LB", "LT-RT", "LM-RM"),
            ['G'] = Glyph(1.0, "RT-LT", "LT-LB", "LB-RB", "RB-RM", "RM-MC"),
            ['H'] = Glyph(1.0, "LT-LB", "RT-RB", "LM-RM"),
            ['I'] = Glyph(0.72, "LT-RT", "TC-BC", "LB-RB"),
            ['J'] = Glyph(1.0, "LT-RT", "RT-RB", "RB-LB", "LB-LM"),
            ['K'] = Glyph(1.0, "LT-LB", "LM-RT", "LM-RB"),
            ['L'] = Glyph(1.0, "LT-LB", "LB-RB"),
            ['M'] = Glyph(1.0, "LB-LT", "LT-MC", "MC-RT", "RT-RB"),
            ['N'] = Glyph(1.0, "LB-LT", "LT-RB", "RB-RT"),
            ['O'] = Glyph(1.0, "LT-RT", "RT-RB", "RB-LB", "LB-LT"),
            ['P'] = Glyph(1.0, "LB-LT", "LT-RT", "RT-RM", "RM-LM"),
            ['Q'] = Glyph(1.0, "LT-RT", "RT-RB", "RB-LB", "LB-LT", "MC-RB"),
            ['R'] = Glyph(1.0, "LB-LT", "LT-RT", "RT-RM", "RM-LM", "LM-RB"),
            ['S'] = Glyph(1.0, "RT-LT", "LT-LM", "LM-RM", "RM-RB", "RB-LB"),
            ['T'] = Glyph(1.0, "LT-RT", "TC-BC"),
            ['U'] = Glyph(1.0, "LT-LB", "LB-RB", "RB-RT"),
            ['V'] = Glyph(1.0, "LT-BC", "BC-RT"),
            ['W'] = Glyph(1.0, "LT-LB", "LB-MC", "MC-RB", "RB-RT"),
            ['X'] = Glyph(1.0, "LT-RB", "RT-LB"),
            ['Y'] = Glyph(1.0, "LT-MC", "RT-MC", "MC-BC"),
            ['Z'] = Glyph(1.0, "LT-RT", "RT-LB", "LB-RB"),
            ['.'] = Glyph(0.38, "BC-RB"),
            [','] = Glyph(0.38, "MC-LB"),
            [':'] = Glyph(0.38, "TC-MC", "BC-RB"),
            [';'] = Glyph(0.38, "TC-MC", "MC-LB"),
            ['-'] = Glyph(0.72, "LM-RM"),
            ['_'] = Glyph(0.72, "LB-RB"),
            ['+'] = Glyph(0.85, "LM-RM", "TC-BC"),
            ['/'] = Glyph(0.85, "RB-LT"),
            ['\\'] = Glyph(0.85, "LT-RB"),
            ['='] = Glyph(0.85, "TM-RM", "LM-BM"),
            ['('] = Glyph(0.55, "RT-TC", "TC-BC", "BC-RB"),
            [')'] = Glyph(0.55, "LT-TC", "TC-BC", "BC-LB"),
            ['['] = Glyph(0.55, "RT-LT", "LT-LB", "LB-RB"),
            [']'] = Glyph(0.55, "LT-RT", "RT-RB", "RB-LB"),
            ['?'] = Glyph(1.0, "LT-RT", "RT-RM", "RM-MC", "MC-BC", "BC-RB"),
            ['!'] = Glyph(0.38, "TC-MC", "MC-BC", "LB-RB"),
            ['#'] = Glyph(1.0, "TM-BM", "TR-BR", "LM-RM", "UL-UR"),
            ['*'] = Glyph(1.0, "LT-RB", "RT-LB", "LM-RM", "TC-BC"),
            ['%'] = Glyph(1.0, "LT-RB", "LT-LM", "LM-MC", "RB-RM", "RM-MC"),
            ['&'] = Glyph(1.0, "RT-LT", "LT-MC", "MC-LB", "LB-RB", "MC-RM"),
            ['@'] = Glyph(1.0, "LT-RT", "RT-RB", "RB-LB", "LB-LT", "MC-RM", "RM-RB")
        };

        return glyphs;
    }

    private static GlyphDefinition Glyph(double advance, params string[] segments)
    {
        return new GlyphDefinition(
            advance,
            segments.Select(ToStroke).ToArray());
    }

    private static StrokeSegment ToStroke(string value)
    {
        var parts = value.Split('-', 2);
        return new StrokeSegment(ToPoint(parts[0]), ToPoint(parts[1]));
    }

    private static StrokePoint ToPoint(string value)
    {
        return value switch
        {
            "LT" => new StrokePoint(0.0, 0.0),
            "TC" => new StrokePoint(0.5, 0.0),
            "RT" => new StrokePoint(1.0, 0.0),
            "UL" => new StrokePoint(0.0, 0.28),
            "TM" => new StrokePoint(0.35, 0.28),
            "TR" => new StrokePoint(0.65, 0.28),
            "LM" => new StrokePoint(0.0, 0.5),
            "MC" => new StrokePoint(0.5, 0.5),
            "RM" => new StrokePoint(1.0, 0.5),
            "BM" => new StrokePoint(0.35, 0.72),
            "BR" => new StrokePoint(0.65, 0.72),
            "LB" => new StrokePoint(0.0, 1.0),
            "BC" => new StrokePoint(0.5, 1.0),
            "RB" => new StrokePoint(1.0, 1.0),
            "UR" => new StrokePoint(1.0, 0.28),
            _ => throw new InvalidOperationException($"Unknown stroke font point: {value}")
        };
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

    private sealed record GlyphDefinition(double Advance, IReadOnlyList<StrokeSegment> Strokes);

    private readonly record struct StrokeSegment(StrokePoint Start, StrokePoint End);

    private readonly record struct StrokePoint(double X, double Y);
}
