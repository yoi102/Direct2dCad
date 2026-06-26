namespace Direct2dCad.Db.Geometry;

public readonly struct CadMatrixD : IEquatable<CadMatrixD>
{
    public static readonly CadMatrixD Identity = new(
        1, 0,
        0, 1,
        0, 0);

    public double M11 { get; }
    public double M12 { get; }
    public double M21 { get; }
    public double M22 { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }

    public bool IsIdentity => Equals(Identity);

    public CadMatrixD(
        double m11,
        double m12,
        double m21,
        double m22,
        double offsetX,
        double offsetY)
    {
        M11 = m11;
        M12 = m12;
        M21 = m21;
        M22 = m22;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public static CadMatrixD CreateTranslation(double x, double y)
    {
        return new CadMatrixD(
            1, 0,
            0, 1,
            x, y);
    }

    public static CadMatrixD CreateTranslation(CadVectorD offset)
    {
        return CreateTranslation(offset.X, offset.Y);
    }

    public static CadMatrixD CreateScale(double scale)
    {
        return CreateScale(scale, scale);
    }

    public static CadMatrixD CreateScale(double scaleX, double scaleY)
    {
        return new CadMatrixD(
            scaleX, 0,
            0, scaleY,
            0, 0);
    }

    public static CadMatrixD CreateScale(double scaleX, double scaleY, CadPointD center)
    {
        return CreateTranslation(-center.X, -center.Y) *
               CreateScale(scaleX, scaleY) *
               CreateTranslation(center.X, center.Y);
    }

    public static CadMatrixD CreateRotation(double radians)
    {
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        return new CadMatrixD(
            cos, sin,
            -sin, cos,
            0, 0);
    }

    public static CadMatrixD CreateRotation(double radians, CadPointD center)
    {
        return CreateTranslation(-center.X, -center.Y) *
               CreateRotation(radians) *
               CreateTranslation(center.X, center.Y);
    }

    public CadPointD TransformPoint(CadPointD point)
    {
        var x = point.X * M11 + point.Y * M21 + OffsetX;
        var y = point.X * M12 + point.Y * M22 + OffsetY;

        return new CadPointD(x, y);
    }

    public CadVectorD TransformVector(CadVectorD vector)
    {
        var x = vector.X * M11 + vector.Y * M21;
        var y = vector.X * M12 + vector.Y * M22;

        return new CadVectorD(x, y);
    }

    public bool TryInvert(out CadMatrixD inverse)
    {
        var determinant = M11 * M22 - M12 * M21;

        if (Math.Abs(determinant) <= double.Epsilon)
        {
            inverse = Identity;
            return false;
        }

        var inv = 1.0 / determinant;

        inverse = new CadMatrixD(
            M22 * inv,
            -M12 * inv,
            -M21 * inv,
            M11 * inv,
            (M21 * OffsetY - OffsetX * M22) * inv,
            (OffsetX * M12 - M11 * OffsetY) * inv);

        return true;
    }

    public CadMatrixD Invert()
    {
        if (!TryInvert(out var inverse))
            throw new InvalidOperationException("Matrix is not invertible.");

        return inverse;
    }

    public bool NearEquals(CadMatrixD other, double tolerance = 1e-9)
    {
        return Math.Abs(M11 - other.M11) <= tolerance &&
               Math.Abs(M12 - other.M12) <= tolerance &&
               Math.Abs(M21 - other.M21) <= tolerance &&
               Math.Abs(M22 - other.M22) <= tolerance &&
               Math.Abs(OffsetX - other.OffsetX) <= tolerance &&
               Math.Abs(OffsetY - other.OffsetY) <= tolerance;
    }

    public bool Equals(CadMatrixD other)
    {
        return M11.Equals(other.M11) &&
               M12.Equals(other.M12) &&
               M21.Equals(other.M21) &&
               M22.Equals(other.M22) &&
               OffsetX.Equals(other.OffsetX) &&
               OffsetY.Equals(other.OffsetY);
    }

    public override bool Equals(object? obj)
    {
        return obj is CadMatrixD other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(M11, M12, M21, M22, OffsetX, OffsetY);
    }

    public override string ToString()
    {
        return $"[{M11}, {M12}; {M21}, {M22}; {OffsetX}, {OffsetY}]";
    }

    public static CadMatrixD operator *(CadMatrixD left, CadMatrixD right)
    {
        return new CadMatrixD(
            left.M11 * right.M11 + left.M12 * right.M21,
            left.M11 * right.M12 + left.M12 * right.M22,
            left.M21 * right.M11 + left.M22 * right.M21,
            left.M21 * right.M12 + left.M22 * right.M22,
            left.OffsetX * right.M11 + left.OffsetY * right.M21 + right.OffsetX,
            left.OffsetX * right.M12 + left.OffsetY * right.M22 + right.OffsetY);
    }

    public static bool operator ==(CadMatrixD left, CadMatrixD right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CadMatrixD left, CadMatrixD right)
    {
        return !left.Equals(right);
    }
}
