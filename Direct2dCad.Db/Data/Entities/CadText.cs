using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadText : CadEntity
{
    public const double FontSizeScale = 0.78;

    private double _estimatedWidth;

    public string Text { get; private set; }
    public CadPointD Position { get; private set; }
    public double Height { get; private set; }
    public double RotationRadians { get; private set; }
    public StyleId? GraphicStyleId { get; private set; }
    public StyleId? TextStyleId { get; private set; }

    public double EstimatedWidth => _estimatedWidth;

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
        UpdateEstimatedWidth();
    }

    public void SetText(string text)
    {
        Text = text ?? string.Empty;
        UpdateEstimatedWidth();
    }

    public void SetPosition(CadPointD position) => Position = position;

    public void SetHeight(double height)
    {
        Height = GuardPositive(height, nameof(height));
        UpdateEstimatedWidth();
    }

    public void SetRotation(double rotationRadians) => RotationRadians = rotationRadians;

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;

    internal void SetTextStyleInternal(StyleId? textStyleId) => TextStyleId = textStyleId;

    private void UpdateEstimatedWidth()
    {
        _estimatedWidth = EstimateTextWidth(Text, Height);
    }

    private static double GuardPositive(double value, string paramName)
    {
        return value <= 0 || double.IsNaN(value) || double.IsInfinity(value)
            ? throw new ArgumentOutOfRangeException(paramName)
            : value;
    }

    public static double EstimateTextWidth(string text, double height)
    {
        if (string.IsNullOrEmpty(text))
            return height * FontSizeScale;

        var emUnits = 0.0;
        foreach (var ch in text)
            emUnits += EstimateCharacterEmWidth(ch);

        // Keep this model-side estimate close to the Direct2D font size used by rendering.
        // The old estimate rounded character units up to full text-height units, which made
        // short Latin text bounds much wider than the actual rendered glyphs.
        var width = emUnits * FontSizeScale * height;
        var overhangPadding = height * 0.08;
        return Math.Max(width + overhangPadding, height * FontSizeScale * 0.25);
    }

    private static double EstimateCharacterEmWidth(char ch)
    {
        if (IsWideCharacter(ch))
            return 1.0;

        return ch switch
        {
            ' ' or '\t' => 0.33,
            'i' or 'l' or 'I' or '|' or '!' or '.' or ',' or ':' or ';' or '\'' or '`' => 0.28,
            'j' or 'f' or 't' or '(' or ')' or '[' or ']' or '{' or '}' => 0.36,
            'm' or 'w' or 'M' or 'W' or '@' or '#' => 0.82,
            '-' or '_' or '/' or '\\' => 0.45,
            _ when char.IsDigit(ch) => 0.56,
            _ when char.IsUpper(ch) => 0.64,
            _ => 0.52
        };
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
