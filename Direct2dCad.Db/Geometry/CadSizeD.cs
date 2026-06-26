namespace Direct2dCad.Db.Geometry;

public readonly struct CadSizeD : IEquatable<CadSizeD>
{
    public static readonly CadSizeD Zero = new(0, 0);

    public double Width { get; }
    public double Height { get; }

    public bool IsEmpty => Width < 0 || Height < 0;

    public CadSizeD(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public bool NearEquals(CadSizeD other, double tolerance = 1e-9)
    {
        return Math.Abs(Width - other.Width) <= tolerance &&
               Math.Abs(Height - other.Height) <= tolerance;
    }

    public bool Equals(CadSizeD other)
    {
        return Width.Equals(other.Width) && Height.Equals(other.Height);
    }

    public override bool Equals(object? obj)
    {
        return obj is CadSizeD other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Width, Height);
    }

    public override string ToString()
    {
        return $"({Width} x {Height})";
    }

    public static bool operator ==(CadSizeD left, CadSizeD right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CadSizeD left, CadSizeD right)
    {
        return !left.Equals(right);
    }
}
