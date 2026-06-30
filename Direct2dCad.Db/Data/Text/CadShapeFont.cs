using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Text;

public readonly record struct CadShapeFontId(string Value)
{
    public static readonly CadShapeFontId Unicode = new("unicode");
    public static readonly CadShapeFontId Simplex = new("simplex");
    public static readonly CadShapeFontId MonoLine = new("monoline");
    public static readonly CadShapeFontId BoxFallback = new("box-fallback");

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

    public override string ToString() => Value ?? string.Empty;
}

public enum CadShapeFontGlyphSet
{
    Simplex,
    BoxFallback
}

public sealed class CadShapeFont
{
    public CadShapeFontId Id { get; }
    public string Name { get; }
    public CadShapeFontGlyphSet GlyphSet { get; }
    public bool SupportsUnicode { get; }

    public CadShapeFont(
        CadShapeFontId id,
        string name,
        CadShapeFontGlyphSet glyphSet = CadShapeFontGlyphSet.Simplex,
        bool supportsUnicode = false)
    {
        Id = id.IsEmpty ? throw new ArgumentException("Shape font id cannot be empty.", nameof(id)) : id;
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Shape font name cannot be empty.", nameof(name))
            : name.Trim();
        GlyphSet = glyphSet;
        SupportsUnicode = supportsUnicode;
    }
}

public static class CadShapeFontRegistry
{
    public static CadShapeFontId DefaultShapeFontId => CadShapeFontId.Unicode;

    public static IReadOnlyList<CadShapeFont> Defaults { get; } =
    [
        new CadShapeFont(
            CadShapeFontId.Unicode,
            "Unicode Shape",
            supportsUnicode: true),
        new CadShapeFont(CadShapeFontId.Simplex, "Simplex"),
        new CadShapeFont(CadShapeFontId.MonoLine, "MonoLine"),
        new CadShapeFont(
            CadShapeFontId.BoxFallback,
            "Box Fallback",
            CadShapeFontGlyphSet.BoxFallback,
            supportsUnicode: true)
    ];

    public static CadShapeFont GetOrDefault(CadShapeFontId shapeFontId)
    {
        if (shapeFontId.IsEmpty)
            shapeFontId = DefaultShapeFontId;

        return Defaults.FirstOrDefault(x => string.Equals(
                   x.Id.Value,
                   shapeFontId.Value,
                   StringComparison.OrdinalIgnoreCase))
               ?? Defaults[0];
    }

    public static CadShapeFontId FromStoredValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? DefaultShapeFontId
            : new CadShapeFontId(value.Trim());
    }
}

public static class CadShapeFontMetrics
{
    public static CadRectD MeasureBounds(
        string text,
        CadPointD position,
        double height,
        double widthFactor = CadStrokeFont.DefaultWidthFactor,
        double characterSpacingFactor = CadStrokeFont.DefaultCharacterSpacingFactor,
        double obliqueAngleRadians = CadStrokeFont.DefaultObliqueAngleRadians,
        double rotationRadians = 0,
        CadShapeFontId shapeFontId = default)
    {
        var font = CadShapeFontRegistry.GetOrDefault(shapeFontId);
        return CadStrokeFont.MeasureBounds(
            text,
            position,
            height,
            widthFactor,
            characterSpacingFactor,
            obliqueAngleRadians,
            rotationRadians,
            font.Id);
    }
}
