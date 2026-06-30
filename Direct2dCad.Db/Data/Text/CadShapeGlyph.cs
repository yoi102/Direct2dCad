namespace Direct2dCad.Db.Data.Text;

public sealed class CadShapeGlyph
{
    public CadShapeGlyph(double advance, IReadOnlyList<CadShapeGlyphStroke> strokes)
    {
        if (advance <= 0 || double.IsNaN(advance) || double.IsInfinity(advance))
            throw new ArgumentOutOfRangeException(nameof(advance));

        Advance = advance;
        Strokes = strokes ?? throw new ArgumentNullException(nameof(strokes));
    }

    public double Advance { get; }

    public IReadOnlyList<CadShapeGlyphStroke> Strokes { get; }
}

public readonly record struct CadShapeGlyphStroke(CadShapeGlyphPoint Start, CadShapeGlyphPoint End);

public readonly record struct CadShapeGlyphPoint(double X, double Y);
