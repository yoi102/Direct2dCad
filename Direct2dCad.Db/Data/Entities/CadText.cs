using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadText : CadEntity
{
    public const double FontSizeScale = 0.78;
    public const double DefaultInvertedMarginFactor = 0.12;

    private CadRectD _localBounds;
    private CadRectD _bounds;
    private bool _requiresBoundsMeasurement;

    public string Text { get; private set; }
    public CadPointD Position { get; private set; }
    public double Height { get; private set; }
    public double RotationRadians { get; private set; }
    public StyleId? GraphicStyleId { get; private set; }
    public StyleId? TextStyleId { get; private set; }
    public bool IsInverted { get; private set; }
    public double InvertedMarginFactor { get; private set; }

    public CadRectD LocalBounds => _localBounds;
    public CadRectD TextBounds => LocalBounds.Translate(Position - CadPointD.Origin);
    public CadRectD InvertedBackgroundBounds => TextBounds.Inflate(GetInvertedMargin());
    public bool RequiresBoundsMeasurement => _requiresBoundsMeasurement;

    public override CadRectD Bounds => _bounds;

    internal CadText(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        string text,
        CadPointD position,
        double height,
        double rotationRadians = 0,
        StyleId? textStyleId = null,
        string name = "",
        bool isInverted = false,
        double invertedMarginFactor = DefaultInvertedMarginFactor)
        : base(id, layerId, ownerBlockId, name)
    {
        Text = text ?? string.Empty;
        Position = position;
        Height = GuardPositive(height, nameof(height));
        RotationRadians = GuardFinite(rotationRadians, nameof(rotationRadians));
        TextStyleId = textStyleId;
        IsInverted = isInverted;
        InvertedMarginFactor = GuardNonNegative(invertedMarginFactor, nameof(invertedMarginFactor));
        MarkBoundsForMeasurement();
    }

    public void SetText(string text)
    {
        Text = text ?? string.Empty;
        MarkBoundsForMeasurement();
    }

    public void SetPosition(CadPointD position)
    {
        Position = position;
        RebuildBounds();
    }

    public void SetHeight(double height)
    {
        Height = GuardPositive(height, nameof(height));
        MarkBoundsForMeasurement();
    }

    public void SetRotation(double rotationRadians)
    {
        RotationRadians = GuardFinite(rotationRadians, nameof(rotationRadians));
        RebuildBounds();
    }

    public CadPointD WorldToTextSpace(CadPointD point)
    {
        return RotateAround(point, Position, -RotationRadians);
    }

    public void SetInverted(bool isInverted)
    {
        IsInverted = isInverted;
        RebuildBounds();
    }

    public void SetInvertedMarginFactor(double invertedMarginFactor)
    {
        InvertedMarginFactor = GuardNonNegative(invertedMarginFactor, nameof(invertedMarginFactor));
        RebuildBounds();
    }

    public double GetInvertedMargin()
    {
        return Height * InvertedMarginFactor;
    }

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
        RebuildBounds();
        return true;
    }

    public void MarkBoundsForMeasurement()
    {
        _localBounds = CreateUnmeasuredLocalBounds(Height);
        _requiresBoundsMeasurement = true;
        RebuildBounds();
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

    private CadRectD CalculateRotatedBounds(CadRectD bounds)
    {
        if (bounds.IsEmpty || Math.Abs(RotationRadians) <= 1e-12)
            return bounds;

        var rotatedBounds = CadRectD.Empty;
        rotatedBounds = rotatedBounds.ExpandToInclude(RotateAround(new CadPointD(bounds.MinX, bounds.MinY), Position, RotationRadians));
        rotatedBounds = rotatedBounds.ExpandToInclude(RotateAround(new CadPointD(bounds.MaxX, bounds.MinY), Position, RotationRadians));
        rotatedBounds = rotatedBounds.ExpandToInclude(RotateAround(new CadPointD(bounds.MaxX, bounds.MaxY), Position, RotationRadians));
        rotatedBounds = rotatedBounds.ExpandToInclude(RotateAround(new CadPointD(bounds.MinX, bounds.MaxY), Position, RotationRadians));
        return rotatedBounds;
    }

    private void RebuildBounds()
    {
        _bounds = CalculateRotatedBounds(
            IsInverted ? InvertedBackgroundBounds : TextBounds);
    }

    private static CadPointD RotateAround(CadPointD point, CadPointD center, double angleRadians)
    {
        if (Math.Abs(angleRadians) <= 1e-12)
            return point;

        var cos = Math.Cos(angleRadians);
        var sin = Math.Sin(angleRadians);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        return new CadPointD(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
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
