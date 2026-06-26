using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadText : CadEntity
{
    public string Text { get; private set; }
    public CadPointD Position { get; private set; }
    public double Height { get; private set; }
    public double RotationRadians { get; private set; }
    public StyleId? GraphicStyleId { get; private set; }
    public StyleId? TextStyleId { get; private set; }

    public override CadRectD Bounds => CadRectD.FromLTRB(
        Position.X,
        Position.Y,
        Position.X + Math.Max(Text.Length, 1) * Height * 0.6,
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
}
