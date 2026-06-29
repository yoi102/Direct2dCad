using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadText : CadEntity
{
    public const double FontSizeScale = 0.78;

    private CadRectD _localBounds;
    private bool _requiresBoundsMeasurement;

    public string Text { get; private set; }
    public CadPointD Position { get; private set; }
    public double Height { get; private set; }
    public double RotationRadians { get; private set; }
    public StyleId? GraphicStyleId { get; private set; }
    public StyleId? TextStyleId { get; private set; }

    public CadRectD LocalBounds => _localBounds;
    public bool RequiresBoundsMeasurement => _requiresBoundsMeasurement;

    public override CadRectD Bounds => LocalBounds.Translate(Position - CadPointD.Origin);

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
        MarkBoundsForMeasurement();
    }

    public void SetText(string text)
    {
        Text = text ?? string.Empty;
        MarkBoundsForMeasurement();
    }

    public void SetPosition(CadPointD position) => Position = position;

    public void SetHeight(double height)
    {
        Height = GuardPositive(height, nameof(height));
        MarkBoundsForMeasurement();
    }

    public void SetRotation(double rotationRadians) => RotationRadians = rotationRadians;

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;

    internal void SetTextStyleInternal(StyleId? textStyleId)
    {
        TextStyleId = textStyleId;
        MarkBoundsForMeasurement();
    }

    public bool SetLocalBounds(CadRectD bounds, double tolerance = 1e-6)
    {
        if (bounds.IsEmpty ||
            bounds.Width <= 0 ||
            bounds.Height <= 0 ||
            double.IsNaN(bounds.Width) ||
            double.IsNaN(bounds.Height) ||
            double.IsInfinity(bounds.Width) ||
            double.IsInfinity(bounds.Height))
        {
            return false;
        }

        if (!_requiresBoundsMeasurement && _localBounds.NearEquals(bounds, tolerance))
            return false;

        _localBounds = bounds;
        _requiresBoundsMeasurement = false;
        return true;
    }

    public void MarkBoundsForMeasurement()
    {
        _localBounds = CreateUnmeasuredLocalBounds(Height);
        _requiresBoundsMeasurement = true;
    }

    public static CadRectD CreateUnmeasuredBounds(CadPointD position, double height)
    {
        return CreateUnmeasuredLocalBounds(height).Translate(position - CadPointD.Origin);
    }

    private static double GuardPositive(double value, string paramName)
    {
        return value <= 0 || double.IsNaN(value) || double.IsInfinity(value)
            ? throw new ArgumentOutOfRangeException(paramName)
            : value;
    }

    private static CadRectD CreateUnmeasuredLocalBounds(double height)
    {
        var size = height > 0 && !double.IsNaN(height) && !double.IsInfinity(height)
            ? height
            : 1.0;

        return CadRectD.FromLTRB(
            0,
            0,
            size,
            size);
    }
}
