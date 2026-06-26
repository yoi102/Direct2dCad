namespace Direct2dCad.Db.Geometry;

public readonly struct CadVectorD : IEquatable<CadVectorD>
{
    public static readonly CadVectorD Zero = new(0, 0);
    public static readonly CadVectorD UnitX = new(1, 0);
    public static readonly CadVectorD UnitY = new(0, 1);

    public double X { get; }
    public double Y { get; }

    public double Length => Math.Sqrt(LengthSquared);
    public double LengthSquared => X * X + Y * Y;

    public CadVectorD(double x, double y)
    {
        X = x;
        Y = y;
    }

    public CadVectorD Normalize()
    {
        var length = Length;

        if (length <= double.Epsilon)
            return Zero;

        return new CadVectorD(X / length, Y / length);
    }

    public double Dot(CadVectorD other)
    {
        return X * other.X + Y * other.Y;
    }

    public double Cross(CadVectorD other)
    {
        return X * other.Y - Y * other.X;
    }

    public CadVectorD Perpendicular()
    {
        return new CadVectorD(-Y, X);
    }

    public CadVectorD Transform(CadMatrixD matrix)
    {
        return matrix.TransformVector(this);
    }

    public bool NearEquals(CadVectorD other, double tolerance = 1e-9)
    {
        return Math.Abs(X - other.X) <= tolerance &&
               Math.Abs(Y - other.Y) <= tolerance;
    }

    public bool Equals(CadVectorD other)
    {
        return X.Equals(other.X) && Y.Equals(other.Y);
    }

    public override bool Equals(object? obj)
    {
        return obj is CadVectorD other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }

    public static CadVectorD operator +(CadVectorD left, CadVectorD right)
    {
        return new CadVectorD(left.X + right.X, left.Y + right.Y);
    }

    public static CadVectorD operator -(CadVectorD left, CadVectorD right)
    {
        return new CadVectorD(left.X - right.X, left.Y - right.Y);
    }

    public static CadVectorD operator -(CadVectorD vector)
    {
        return new CadVectorD(-vector.X, -vector.Y);
    }

    public static CadVectorD operator *(CadVectorD vector, double scale)
    {
        return new CadVectorD(vector.X * scale, vector.Y * scale);
    }

    public static CadVectorD operator *(double scale, CadVectorD vector)
    {
        return new CadVectorD(vector.X * scale, vector.Y * scale);
    }

    public static CadVectorD operator /(CadVectorD vector, double scale)
    {
        return new CadVectorD(vector.X / scale, vector.Y / scale);
    }

    public static bool operator ==(CadVectorD left, CadVectorD right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CadVectorD left, CadVectorD right)
    {
        return !left.Equals(right);
    }
}
