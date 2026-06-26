namespace Direct2dCad.Db.Data.Styles;

public enum CadStyleKind
{
    Graphic,
    Text,
    Fill
}

public abstract class CadStyle : IEquatable<CadStyle>
{
    public StyleId Id { get; }
    public string Name { get; private set; }
    public abstract CadStyleKind Kind { get; }

    protected CadStyle(StyleId id, string name)
    {
        Id = id;
        Name = GuardName(name);
    }

    public void Rename(string name) => Name = GuardName(name);

    public bool Equals(CadStyle? other) => other is not null && Id.Equals(other.Id);
    public override bool Equals(object? obj) => obj is CadStyle other && Equals(other);
    public override int GetHashCode() => Id.GetHashCode();

    protected static string GuardName(string name)
    {
        return string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Name cannot be empty.", nameof(name))
            : name.Trim();
    }
}
