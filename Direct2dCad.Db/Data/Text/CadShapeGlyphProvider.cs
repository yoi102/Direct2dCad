using System.Text;

namespace Direct2dCad.Db.Data.Text;

public interface ICadShapeGlyphProvider
{
    bool TryGetGlyph(Rune codePoint, CadShapeFont font, out CadShapeGlyph glyph);
}

public static class CadShapeGlyphProviderRegistry
{
    private static readonly object Gate = new();
    private static readonly List<ICadShapeGlyphProvider> Providers = [new BuiltInCadShapeGlyphProvider()];

    public static IDisposable RegisterProvider(ICadShapeGlyphProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (Gate)
        {
            Providers.Insert(0, provider);
        }

        return new ProviderRegistration(provider);
    }

    public static CadShapeGlyph ResolveGlyph(Rune codePoint, CadShapeFont font)
    {
        ICadShapeGlyphProvider[] providers;
        lock (Gate)
        {
            providers = [.. Providers];
        }

        foreach (var provider in providers)
        {
            if (provider.TryGetGlyph(codePoint, font, out var glyph))
                return glyph;
        }

        return CadShapeGlyphs.FallbackBox;
    }

    private sealed class ProviderRegistration(ICadShapeGlyphProvider provider) : IDisposable
    {
        public void Dispose()
        {
            lock (Gate)
            {
                Providers.Remove(provider);
            }
        }
    }
}

internal sealed class BuiltInCadShapeGlyphProvider : ICadShapeGlyphProvider
{
    public bool TryGetGlyph(Rune codePoint, CadShapeFont font, out CadShapeGlyph glyph)
    {
        if (IsSpace(codePoint))
        {
            glyph = CadShapeGlyphs.Space;
            return true;
        }

        if (font.GlyphSet == CadShapeFontGlyphSet.BoxFallback)
        {
            glyph = CadShapeGlyphs.FallbackBox;
            return true;
        }

        if (TryGetAsciiGlyph(codePoint, out glyph))
            return true;

        glyph = font.SupportsUnicode && font.GlyphSet == CadShapeFontGlyphSet.UnicodeFallback
            ? CadShapeGlyphs.CreateUnicodeFallback(codePoint)
            : CadShapeGlyphs.FallbackBox;
        return true;
    }

    private static bool TryGetAsciiGlyph(Rune codePoint, out CadShapeGlyph glyph)
    {
        if (codePoint.Value <= char.MaxValue)
        {
            var key = char.ToUpperInvariant((char)codePoint.Value);
            if (CadShapeGlyphs.Ascii.TryGetValue(key, out glyph!))
                return true;
        }

        glyph = null!;
        return false;
    }

    private static bool IsSpace(Rune codePoint)
    {
        return codePoint.Value is ' ' or '\t' or 0x00A0 or 0x3000;
    }
}

internal static class CadShapeGlyphs
{
    public static readonly CadShapeGlyph Space = Glyph(0.62);
    public static readonly CadShapeGlyph FallbackBox = Glyph(1.0, "LT-RT", "RT-RB", "RB-LB", "LB-LT", "LT-RB");

    public static IReadOnlyDictionary<char, CadShapeGlyph> Ascii { get; } = CreateAsciiGlyphs();

    public static CadShapeGlyph CreateUnicodeFallback(Rune codePoint)
    {
        if (codePoint.Value is >= 0x3040 and <= 0x30FF)
            return CreateKanaFallback(codePoint.Value);

        if (codePoint.Value >= 0x2E80)
            return CreateIdeographFallback(codePoint.Value);

        return CreateSymbolFallback(codePoint.Value);
    }

    private static CadShapeGlyph CreateIdeographFallback(int codePoint)
    {
        var strokes = CreateOuterBox();
        Add(strokes, 0.50, 0.00, 0.50, 1.00);
        Add(strokes, 0.00, 0.50, 1.00, 0.50);

        if ((codePoint & 0x01) != 0) Add(strokes, 0.22, 0.00, 0.22, 1.00);
        if ((codePoint & 0x02) != 0) Add(strokes, 0.78, 0.00, 0.78, 1.00);
        if ((codePoint & 0x04) != 0) Add(strokes, 0.00, 0.25, 1.00, 0.25);
        if ((codePoint & 0x08) != 0) Add(strokes, 0.00, 0.75, 1.00, 0.75);
        if ((codePoint & 0x10) != 0) Add(strokes, 0.12, 0.12, 0.88, 0.88);
        if ((codePoint & 0x20) != 0) Add(strokes, 0.88, 0.12, 0.12, 0.88);

        return new CadShapeGlyph(1.0, strokes);
    }

    private static CadShapeGlyph CreateKanaFallback(int codePoint)
    {
        var strokes = new List<CadShapeGlyphStroke>();
        Add(strokes, 0.12, 0.14, 0.88, 0.14);
        Add(strokes, 0.76, 0.14, 0.76, 0.72);
        Add(strokes, 0.76, 0.72, 0.42, 0.92);

        if ((codePoint & 0x01) != 0) Add(strokes, 0.18, 0.36, 0.84, 0.36);
        if ((codePoint & 0x02) != 0) Add(strokes, 0.22, 0.18, 0.40, 0.86);
        if ((codePoint & 0x04) != 0) Add(strokes, 0.16, 0.64, 0.64, 0.64);
        if ((codePoint & 0x08) != 0) Add(strokes, 0.28, 0.90, 0.88, 0.48);

        return new CadShapeGlyph(1.0, strokes);
    }

    private static CadShapeGlyph CreateSymbolFallback(int codePoint)
    {
        var strokes = CreateOuterBox();

        if ((codePoint & 0x01) != 0) Add(strokes, 0.50, 0.00, 0.50, 1.00);
        if ((codePoint & 0x02) != 0) Add(strokes, 0.00, 0.50, 1.00, 0.50);
        if ((codePoint & 0x04) != 0) Add(strokes, 0.12, 0.12, 0.88, 0.88);
        if ((codePoint & 0x08) != 0) Add(strokes, 0.88, 0.12, 0.12, 0.88);

        return new CadShapeGlyph(1.0, strokes);
    }

    private static IReadOnlyDictionary<char, CadShapeGlyph> CreateAsciiGlyphs()
    {
        return new Dictionary<char, CadShapeGlyph>
        {
            [' '] = Space,
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
    }

    private static CadShapeGlyph Glyph(double advance, params string[] segments)
    {
        return new CadShapeGlyph(
            advance,
            segments.Select(ToStroke).ToArray());
    }

    private static CadShapeGlyphStroke ToStroke(string value)
    {
        var parts = value.Split('-', 2);
        return new CadShapeGlyphStroke(ToPoint(parts[0]), ToPoint(parts[1]));
    }

    private static CadShapeGlyphPoint ToPoint(string value)
    {
        return value switch
        {
            "LT" => new CadShapeGlyphPoint(0.0, 0.0),
            "TC" => new CadShapeGlyphPoint(0.5, 0.0),
            "RT" => new CadShapeGlyphPoint(1.0, 0.0),
            "UL" => new CadShapeGlyphPoint(0.0, 0.28),
            "TM" => new CadShapeGlyphPoint(0.35, 0.28),
            "TR" => new CadShapeGlyphPoint(0.65, 0.28),
            "LM" => new CadShapeGlyphPoint(0.0, 0.5),
            "MC" => new CadShapeGlyphPoint(0.5, 0.5),
            "RM" => new CadShapeGlyphPoint(1.0, 0.5),
            "BM" => new CadShapeGlyphPoint(0.35, 0.72),
            "BR" => new CadShapeGlyphPoint(0.65, 0.72),
            "LB" => new CadShapeGlyphPoint(0.0, 1.0),
            "BC" => new CadShapeGlyphPoint(0.5, 1.0),
            "RB" => new CadShapeGlyphPoint(1.0, 1.0),
            "UR" => new CadShapeGlyphPoint(1.0, 0.28),
            _ => throw new InvalidOperationException($"Unknown shape glyph point: {value}")
        };
    }

    private static List<CadShapeGlyphStroke> CreateOuterBox()
    {
        return
        [
            Stroke(0.0, 0.0, 1.0, 0.0),
            Stroke(1.0, 0.0, 1.0, 1.0),
            Stroke(1.0, 1.0, 0.0, 1.0),
            Stroke(0.0, 1.0, 0.0, 0.0)
        ];
    }

    private static void Add(List<CadShapeGlyphStroke> strokes, double startX, double startY, double endX, double endY)
    {
        strokes.Add(Stroke(startX, startY, endX, endY));
    }

    private static CadShapeGlyphStroke Stroke(double startX, double startY, double endX, double endY)
    {
        return new CadShapeGlyphStroke(
            new CadShapeGlyphPoint(startX, startY),
            new CadShapeGlyphPoint(endX, endY));
    }
}
