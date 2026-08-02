namespace Direct2dCad.Db.Cad;

/// <summary>
/// A reusable stroke pattern. Positive values draw and negative values leave gaps;
/// an empty pattern is continuous. Values are expressed in document units.
/// </summary>
public sealed class CadLineTypeDefinition : IEquatable<CadLineTypeDefinition>
{
    private readonly double[] _dashPattern;

    public LineTypeId Id { get; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public IReadOnlyList<double> DashPattern => _dashPattern;
    public bool IsContinuous => _dashPattern.Length == 0;

    internal CadLineTypeDefinition(
        LineTypeId id,
        string name,
        IEnumerable<double>? dashPattern = null,
        string description = "")
    {
        Id = id;
        Name = GuardName(name);
        Description = description?.Trim() ?? string.Empty;
        _dashPattern = dashPattern?.ToArray() ?? [];
        ValidatePattern(_dashPattern);
    }

    public void Rename(string name) => Name = GuardName(name);
    public void SetDescription(string description) => Description = description?.Trim() ?? string.Empty;

    public bool Equals(CadLineTypeDefinition? other) => other is not null && Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is CadLineTypeDefinition other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();

    private static string GuardName(string name) => string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("Line type name cannot be empty.", nameof(name))
        : name.Trim();

    private static void ValidatePattern(double[] pattern)
    {
        foreach (var value in pattern)
        {
            if (!double.IsFinite(value) || Math.Abs(value) <= double.Epsilon)
                throw new ArgumentException("Line type dash values must be finite and non-zero.", nameof(pattern));
        }
    }
}
