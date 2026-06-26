using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Styles.FillStyles;

public enum CadGradientKind
{
    Linear,
    Radial
}

public readonly struct CadGradientStop : IEquatable<CadGradientStop>
{
    public double Offset { get; }
    public CadColor Color { get; }

    public CadGradientStop(double offset, CadColor color)
    {
        if (offset < 0 || offset > 1 || double.IsNaN(offset) || double.IsInfinity(offset))
            throw new ArgumentOutOfRangeException(nameof(offset));

        Offset = offset;
        Color = color;
    }

    public bool Equals(CadGradientStop other) => Offset.Equals(other.Offset) && Color.Equals(other.Color);
    public override bool Equals(object? obj) => obj is CadGradientStop other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Offset, Color);
    public static bool operator ==(CadGradientStop left, CadGradientStop right) => left.Equals(right);
    public static bool operator !=(CadGradientStop left, CadGradientStop right) => !left.Equals(right);
}

/// <summary>
/// Gradient 填充样式。
/// 纯色填充不单独建 SolidFillStyle，使用两个相同颜色的色标表示。
/// </summary>
public sealed class CadGradientFillStyle : CadFillStyle
{
    private readonly List<CadGradientStop> _stops = new();

    public override CadFillKind FillKind => CadFillKind.Gradient;

    public CadGradientKind GradientKind { get; private set; }
    public IReadOnlyList<CadGradientStop> Stops => _stops;
    public double GradientAngle { get; private set; }
    public double GradientScale { get; private set; }
    public CadPointD GradientOrigin { get; private set; }
    public bool IsCentered { get; private set; }

    public bool IsSolid => _stops.Count > 0 && _stops.All(x => x.Color.Equals(_stops[0].Color));

    internal CadGradientFillStyle(
        StyleId id,
        string name,
        CadGradientKind gradientKind,
        IEnumerable<CadGradientStop> stops,
        double gradientAngle = 0.0,
        double gradientScale = 1.0,
        CadPointD? gradientOrigin = null,
        bool isCentered = true)
        : base(id, name)
    {
        GradientKind = gradientKind;
        SetStops(stops);
        GradientAngle = gradientAngle;
        GradientScale = GuardPositive(gradientScale, nameof(gradientScale));
        GradientOrigin = gradientOrigin ?? CadPointD.Origin;
        IsCentered = isCentered;
    }

    internal static CadGradientFillStyle CreateSolid(
        StyleId id,
        string name,
        CadColor color)
    {
        return new CadGradientFillStyle(
            id,
            name,
            CadGradientKind.Linear,
            new[]
            {
                new CadGradientStop(0.0, color),
                new CadGradientStop(1.0, color)
            });
    }

    public void SetGradientKind(CadGradientKind gradientKind) => GradientKind = gradientKind;

    public void SetStops(IEnumerable<CadGradientStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);

        var orderedStops = stops.OrderBy(x => x.Offset).ToArray();

        if (orderedStops.Length < 2)
            throw new ArgumentException("Gradient requires at least two stops.", nameof(stops));

        if (orderedStops[0].Offset != 0.0)
            throw new ArgumentException("Gradient first stop offset must be 0.", nameof(stops));

        if (orderedStops[^1].Offset != 1.0)
            throw new ArgumentException("Gradient last stop offset must be 1.", nameof(stops));

        _stops.Clear();
        _stops.AddRange(orderedStops);
    }

    public void SetAngle(double angle) => GradientAngle = angle;
    public void SetScale(double scale) => GradientScale = GuardPositive(scale, nameof(scale));
    public void SetOrigin(CadPointD origin) => GradientOrigin = origin;
    public void SetCentered(bool value) => IsCentered = value;

    private static double GuardPositive(double value, string paramName)
    {
        return value <= 0 || double.IsNaN(value) || double.IsInfinity(value)
            ? throw new ArgumentOutOfRangeException(paramName)
            : value;
    }
}
