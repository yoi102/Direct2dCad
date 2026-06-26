namespace Direct2dCad.Db.Geometry;

public readonly struct CadPointD : IEquatable<CadPointD>
{
    public static readonly CadPointD Origin = new(0, 0);

    public double X { get; }
    public double Y { get; }

    public CadPointD(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double DistanceTo(CadPointD other)
    {
        return Math.Sqrt(DistanceSquaredTo(other));
    }

    public double DistanceSquaredTo(CadPointD other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return dx * dx + dy * dy;
    }

    public CadPointD Transform(CadMatrixD matrix)
    {
        return matrix.TransformPoint(this);
    }

    public bool NearEquals(CadPointD other, double tolerance = 1e-9)
    {
        return Math.Abs(X - other.X) <= tolerance &&
               Math.Abs(Y - other.Y) <= tolerance;
    }

    public bool Equals(CadPointD other)
    {
        return X.Equals(other.X) && Y.Equals(other.Y);
    }

    public override bool Equals(object? obj)
    {
        return obj is CadPointD other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y);
    }

    public override string ToString()
    {
        return $"({X}, {Y})";
    }

    public static CadPointD operator +(CadPointD point, CadVectorD vector)
    {
        return new CadPointD(point.X + vector.X, point.Y + vector.Y);
    }

    public static CadPointD operator -(CadPointD point, CadVectorD vector)
    {
        return new CadPointD(point.X - vector.X, point.Y - vector.Y);
    }

    public static CadVectorD operator -(CadPointD left, CadPointD right)
    {
        return new CadVectorD(left.X - right.X, left.Y - right.Y);
    }

    public static bool operator ==(CadPointD left, CadPointD right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CadPointD left, CadPointD right)
    {
        return !left.Equals(right);
    }
}
