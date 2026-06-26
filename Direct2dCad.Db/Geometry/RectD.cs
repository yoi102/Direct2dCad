namespace Direct2dCad.Db.Geometry;

public readonly struct CadRectD : IEquatable<CadRectD>
{
    public static readonly CadRectD Empty = new(
        double.PositiveInfinity,
        double.PositiveInfinity,
        double.NegativeInfinity,
        double.NegativeInfinity);

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }

    public double Left => MinX;
    public double Bottom => MinY;
    public double Right => MaxX;
    public double Top => MaxY;

    public double Width => IsEmpty ? 0 : MaxX - MinX;
    public double Height => IsEmpty ? 0 : MaxY - MinY;

    public CadPointD Center => IsEmpty
        ? CadPointD.Origin
        : new CadPointD((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5);

    public CadSizeD Size => IsEmpty
        ? CadSizeD.Zero
        : new CadSizeD(Width, Height);

    public bool IsEmpty => MaxX < MinX || MaxY < MinY;

    public CadRectD(double minX, double minY, double maxX, double maxY)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public static CadRectD FromLTRB(double left, double bottom, double right, double top)
    {
        return new CadRectD(
            Math.Min(left, right),
            Math.Min(bottom, top),
            Math.Max(left, right),
            Math.Max(bottom, top));
    }

    public static CadRectD FromXYWH(double x, double y, double width, double height)
    {
        return FromLTRB(x, y, x + width, y + height);
    }

    public static CadRectD FromCenter(CadPointD center, double width, double height)
    {
        var halfWidth = width * 0.5;
        var halfHeight = height * 0.5;

        return FromLTRB(
            center.X - halfWidth,
            center.Y - halfHeight,
            center.X + halfWidth,
            center.Y + halfHeight);
    }

    public bool Contains(CadPointD point)
    {
        return Contains(point.X, point.Y);
    }

    public bool Contains(double x, double y)
    {
        return !IsEmpty &&
               x >= MinX && x <= MaxX &&
               y >= MinY && y <= MaxY;
    }

    public bool Contains(CadRectD other)
    {
        if (IsEmpty || other.IsEmpty)
            return false;

        return other.MinX >= MinX &&
               other.MaxX <= MaxX &&
               other.MinY >= MinY &&
               other.MaxY <= MaxY;
    }

    public bool Intersects(CadRectD other)
    {
        if (IsEmpty || other.IsEmpty)
            return false;

        return MinX <= other.MaxX &&
               MaxX >= other.MinX &&
               MinY <= other.MaxY &&
               MaxY >= other.MinY;
    }

    public CadRectD Intersection(CadRectD other)
    {
        if (!Intersects(other))
            return Empty;

        return new CadRectD(
            Math.Max(MinX, other.MinX),
            Math.Max(MinY, other.MinY),
            Math.Min(MaxX, other.MaxX),
            Math.Min(MaxY, other.MaxY));
    }

    public CadRectD Union(CadRectD other)
    {
        if (IsEmpty)
            return other;

        if (other.IsEmpty)
            return this;

        return new CadRectD(
            Math.Min(MinX, other.MinX),
            Math.Min(MinY, other.MinY),
            Math.Max(MaxX, other.MaxX),
            Math.Max(MaxY, other.MaxY));
    }

    public CadRectD ExpandToInclude(CadPointD point)
    {
        if (IsEmpty)
            return new CadRectD(point.X, point.Y, point.X, point.Y);

        return new CadRectD(
            Math.Min(MinX, point.X),
            Math.Min(MinY, point.Y),
            Math.Max(MaxX, point.X),
            Math.Max(MaxY, point.Y));
    }

    public CadRectD Inflate(double value)
    {
        return Inflate(value, value);
    }

    public CadRectD Inflate(double x, double y)
    {
        if (IsEmpty)
            return this;

        return new CadRectD(
            MinX - x,
            MinY - y,
            MaxX + x,
            MaxY + y);
    }

    public CadRectD Translate(CadVectorD offset)
    {
        if (IsEmpty)
            return this;

        return new CadRectD(
            MinX + offset.X,
            MinY + offset.Y,
            MaxX + offset.X,
            MaxY + offset.Y);
    }

    public CadRectD Transform(CadMatrixD matrix)
    {
        if (IsEmpty)
            return this;

        var p1 = matrix.TransformPoint(new CadPointD(MinX, MinY));
        var p2 = matrix.TransformPoint(new CadPointD(MaxX, MinY));
        var p3 = matrix.TransformPoint(new CadPointD(MaxX, MaxY));
        var p4 = matrix.TransformPoint(new CadPointD(MinX, MaxY));

        return Empty
            .ExpandToInclude(p1)
            .ExpandToInclude(p2)
            .ExpandToInclude(p3)
            .ExpandToInclude(p4);
    }

    public bool NearEquals(CadRectD other, double tolerance = 1e-9)
    {
        if (IsEmpty && other.IsEmpty)
            return true;

        return Math.Abs(MinX - other.MinX) <= tolerance &&
               Math.Abs(MinY - other.MinY) <= tolerance &&
               Math.Abs(MaxX - other.MaxX) <= tolerance &&
               Math.Abs(MaxY - other.MaxY) <= tolerance;
    }

    public bool Equals(CadRectD other)
    {
        if (IsEmpty && other.IsEmpty)
            return true;

        return MinX.Equals(other.MinX) &&
               MinY.Equals(other.MinY) &&
               MaxX.Equals(other.MaxX) &&
               MaxY.Equals(other.MaxY);
    }

    public override bool Equals(object? obj)
    {
        return obj is CadRectD other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (IsEmpty)
            return 0;

        return HashCode.Combine(MinX, MinY, MaxX, MaxY);
    }

    public override string ToString()
    {
        return IsEmpty
            ? "Empty"
            : $"({MinX}, {MinY}) - ({MaxX}, {MaxY})";
    }

    public static bool operator ==(CadRectD left, CadRectD right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CadRectD left, CadRectD right)
    {
        return !left.Equals(right);
    }
}
