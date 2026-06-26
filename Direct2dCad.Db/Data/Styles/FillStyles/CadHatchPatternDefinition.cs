using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Styles.FillStyles;

/// <summary>
/// Hatch 图案定义。
/// 这里只描述“图案本身”，不保存颜色、缩放、旋转、原点。
/// 颜色、缩放、旋转、原点由 CadHatchFillStyle 保存。
/// </summary>
public sealed class CadHatchPatternDefinition : IEquatable<CadHatchPatternDefinition>
{
    private readonly List<CadHatchLineDefinition> _lines = new();

    public HatchPatternId Id { get; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public IReadOnlyList<CadHatchLineDefinition> Lines => _lines;

    internal CadHatchPatternDefinition(
        HatchPatternId id,
        string name,
        IEnumerable<CadHatchLineDefinition> lines,
        string description = "")
    {
        Id = id;
        Name = GuardName(name);
        Description = description?.Trim() ?? string.Empty;
        ReplaceLines(lines);
    }

    public void Rename(string name) => Name = GuardName(name);
    public void SetDescription(string description) => Description = description?.Trim() ?? string.Empty;

    public void AddLine(CadHatchLineDefinition line) => _lines.Add(line);

    public bool RemoveLine(CadHatchLineDefinition line)
    {
        if (_lines.Count <= 1)
            throw new InvalidOperationException("Hatch pattern requires at least one line definition.");

        return _lines.Remove(line);
    }


    public void ReplaceLines(IEnumerable<CadHatchLineDefinition> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var list = lines.ToArray();
        if (list.Length == 0)
            throw new ArgumentException("Hatch pattern requires at least one line definition.", nameof(lines));

        _lines.Clear();
        _lines.AddRange(list);
    }

    public bool Equals(CadHatchPatternDefinition? other) => other is not null && Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is CadHatchPatternDefinition other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();

    private static string GuardName(string name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Name cannot be empty.", nameof(name))
            : name.Trim();
    }
}

/// <summary>
/// Hatch 图案中的一组重复线定义，类似 AutoCAD .pat 中的一行 pattern line。
/// Angle 建议统一使用 degree。
/// DashPattern：空数组为实线；正数为绘制长度；负数为空白长度；0 可表示点。
/// </summary>
public readonly struct CadHatchLineDefinition : IEquatable<CadHatchLineDefinition>
{
    private readonly double[] _dashPattern;

    public double Angle { get; }
    public CadPointD Origin { get; }
    public CadVectorD Offset { get; }
    public IReadOnlyList<double> DashPattern => _dashPattern;
    public bool IsSolidLine => _dashPattern.Length == 0;

    public CadHatchLineDefinition(
        double angle,
        CadPointD origin,
        CadVectorD offset,
        IEnumerable<double>? dashPattern = null)
    {
        if (offset.LengthSquared <= double.Epsilon)
            throw new ArgumentException("Offset cannot be zero.", nameof(offset));

        Angle = angle;
        Origin = origin;
        Offset = offset;
        _dashPattern = dashPattern?.ToArray() ?? Array.Empty<double>();
        ValidateDashPattern(_dashPattern);
    }

    public bool Equals(CadHatchLineDefinition other)
    {
        return Angle.Equals(other.Angle)
               && Origin.Equals(other.Origin)
               && Offset.Equals(other.Offset)
               && _dashPattern.SequenceEqual(other._dashPattern);
    }

    public override bool Equals(object? obj) => obj is CadHatchLineDefinition other && Equals(other);

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(Angle, Origin, Offset);

        foreach (var value in _dashPattern)
            hash = HashCode.Combine(hash, value);

        return hash;
    }

    public static bool operator ==(CadHatchLineDefinition left, CadHatchLineDefinition right) => left.Equals(right);
    public static bool operator !=(CadHatchLineDefinition left, CadHatchLineDefinition right) => !left.Equals(right);

    private static void ValidateDashPattern(double[] dashPattern)
    {
        foreach (var value in dashPattern)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("Dash pattern cannot contain NaN or Infinity.", nameof(dashPattern));
        }
    }
}
