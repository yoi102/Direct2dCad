using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadShapeText : CadEntity
{
    public const double DefaultInvertedMarginFactor = CadText.DefaultInvertedMarginFactor;

    public string Text { get; private set; }
    public CadPointD Position { get; private set; }
    public double Height { get; private set; }
    public double RotationRadians { get; private set; }
    public double WidthFactor { get; private set; }
    public double CharacterSpacingFactor { get; private set; }
    public double ObliqueAngleRadians { get; private set; }
    public StyleId? GraphicStyleId { get; private set; }
    public bool IsInverted { get; private set; }
    public double InvertedMarginFactor { get; private set; }
    public CadShapeFontId ShapeFontId { get; private set; }

    public CadRectD TextBounds => CadShapeFontMetrics.MeasureBounds(
        Text,
        Position,
        Height,
        WidthFactor,
        CharacterSpacingFactor,
        ObliqueAngleRadians,
        RotationRadians,
        ShapeFontId);
    public CadRectD InvertedBackgroundBounds => TextBounds.Inflate(GetInvertedMargin());

    public override CadRectD Bounds => IsInverted ? InvertedBackgroundBounds : TextBounds;

    internal CadShapeText(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        string text,
        CadPointD position,
        double height,
        double rotationRadians = 0,
        double widthFactor = CadStrokeFont.DefaultWidthFactor,
        double characterSpacingFactor = CadStrokeFont.DefaultCharacterSpacingFactor,
        double obliqueAngleRadians = CadStrokeFont.DefaultObliqueAngleRadians,
        string name = "",
        bool isInverted = false,
        double invertedMarginFactor = DefaultInvertedMarginFactor,
        CadShapeFontId shapeFontId = default)
        : base(id, layerId, ownerBlockId, name)
    {
        Text = text ?? string.Empty;
        Position = position;
        Height = GuardPositive(height, nameof(height));
        RotationRadians = GuardFinite(rotationRadians, nameof(rotationRadians));
        WidthFactor = GuardPositive(widthFactor, nameof(widthFactor));
        CharacterSpacingFactor = GuardNonNegative(characterSpacingFactor, nameof(characterSpacingFactor));
        ObliqueAngleRadians = GuardFinite(obliqueAngleRadians, nameof(obliqueAngleRadians));
        IsInverted = isInverted;
        InvertedMarginFactor = GuardNonNegative(invertedMarginFactor, nameof(invertedMarginFactor));
        ShapeFontId = CadShapeFontRegistry.GetOrDefault(shapeFontId).Id;
    }

    public IReadOnlyList<CadStrokeTextSegment> CreateStrokeSegments()
    {
        return CadStrokeFont.CreateSegments(
            Text,
            Position,
            Height,
            WidthFactor,
            CharacterSpacingFactor,
            ObliqueAngleRadians,
            RotationRadians,
            ShapeFontId);
    }

    public void SetText(string text)
    {
        Text = text ?? string.Empty;
    }

    public void SetPosition(CadPointD position) => Position = position;

    public void SetHeight(double height) => Height = GuardPositive(height, nameof(height));

    public void SetRotation(double rotationRadians) => RotationRadians = GuardFinite(rotationRadians, nameof(rotationRadians));

    public void SetWidthFactor(double widthFactor) => WidthFactor = GuardPositive(widthFactor, nameof(widthFactor));

    public void SetCharacterSpacingFactor(double characterSpacingFactor)
    {
        CharacterSpacingFactor = GuardNonNegative(characterSpacingFactor, nameof(characterSpacingFactor));
    }

    public void SetObliqueAngle(double obliqueAngleRadians)
    {
        ObliqueAngleRadians = GuardFinite(obliqueAngleRadians, nameof(obliqueAngleRadians));
    }

    public void SetInverted(bool isInverted) => IsInverted = isInverted;

    public void SetShapeFont(CadShapeFontId shapeFontId)
    {
        ShapeFontId = CadShapeFontRegistry.GetOrDefault(shapeFontId).Id;
    }

    public void SetInvertedMarginFactor(double invertedMarginFactor)
    {
        InvertedMarginFactor = GuardNonNegative(invertedMarginFactor, nameof(invertedMarginFactor));
    }

    public double GetInvertedMargin()
    {
        return Height * InvertedMarginFactor;
    }

    public void SetGeometry(
        CadPointD position,
        double height,
        double rotationRadians,
        double widthFactor,
        double characterSpacingFactor,
        double obliqueAngleRadians)
    {
        Position = position;
        Height = GuardPositive(height, nameof(height));
        RotationRadians = GuardFinite(rotationRadians, nameof(rotationRadians));
        WidthFactor = GuardPositive(widthFactor, nameof(widthFactor));
        CharacterSpacingFactor = GuardNonNegative(characterSpacingFactor, nameof(characterSpacingFactor));
        ObliqueAngleRadians = GuardFinite(obliqueAngleRadians, nameof(obliqueAngleRadians));
    }

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;

    private static double GuardPositive(double value, string paramName)
    {
        return value <= 0 || double.IsNaN(value) || double.IsInfinity(value)
            ? throw new ArgumentOutOfRangeException(paramName)
            : value;
    }

    private static double GuardNonNegative(double value, string paramName)
    {
        return value < 0 || double.IsNaN(value) || double.IsInfinity(value)
            ? throw new ArgumentOutOfRangeException(paramName)
            : value;
    }

    private static double GuardFinite(double value, string paramName)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? throw new ArgumentOutOfRangeException(paramName)
            : value;
    }
}
