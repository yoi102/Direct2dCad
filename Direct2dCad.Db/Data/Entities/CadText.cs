using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadText : CadEntity
{
    public const double FontSizeScale = 0.78;

    public string Text { get; private set; }
    public CadPointD Position { get; private set; }
    public double Height { get; private set; }
    public double RotationRadians { get; private set; }
    public StyleId? GraphicStyleId { get; private set; }
    public StyleId? TextStyleId { get; private set; }

    public double EstimatedWidth => EstimateTextWidth(Text, Height);

    public override CadRectD Bounds => CadRectD.FromLTRB(
        Position.X,
        Position.Y,
        Position.X + EstimatedWidth,
        Position.Y + Height);

    internal CadText(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        string text,
        CadPointD position,
        double height,
        double rotationRadians = 0,
        StyleId? textStyleId = null,
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        Text = text ?? string.Empty;
        Position = position;
        Height = GuardPositive(height, nameof(height));
        RotationRadians = rotationRadians;
        TextStyleId = textStyleId;
    }

    public void SetText(string text) => Text = text ?? string.Empty;

    public void SetPosition(CadPointD position) => Position = position;

    public void SetHeight(double height) => Height = GuardPositive(height, nameof(height));

    public void SetRotation(double rotationRadians) => RotationRadians = rotationRadians;

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;

    internal void SetTextStyleInternal(StyleId? textStyleId) => TextStyleId = textStyleId;

    private static double GuardPositive(double value, string paramName)
    {
        return value <= 0 || double.IsNaN(value) || double.IsInfinity(value)
            ? throw new ArgumentOutOfRangeException(paramName)
            : value;
    }

    public static double EstimateTextWidth(string text, double height)
    {
        if (string.IsNullOrEmpty(text))
            return height;

        var units = 0.0;
        foreach (var ch in text)
            units += IsWideCharacter(ch) ? 1.0 : 0.6;

        return Math.Max(Math.Ceiling(units), 1.0) * height;
    }

    private static bool IsWideCharacter(char ch)
    {
        return ch is >= '\u1100' and <= '\u115f'
            or >= '\u2e80' and <= '\ua4cf'
            or >= '\uac00' and <= '\ud7a3'
            or >= '\uf900' and <= '\ufaff'
            or >= '\ufe10' and <= '\ufe6f'
            or >= '\uff00' and <= '\uff60'
            or >= '\uffe0' and <= '\uffe6';
    }
}
