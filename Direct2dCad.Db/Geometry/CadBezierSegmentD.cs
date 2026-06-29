namespace Direct2dCad.Db.Geometry;

public readonly struct CadBezierSegmentD : IEquatable<CadBezierSegmentD>
{
    public CadPointD Start { get; }
    public CadPointD Control1 { get; }
    public CadPointD Control2 { get; }
    public CadPointD End { get; }

    public CadBezierSegmentD(
        CadPointD start,
        CadPointD control1,
        CadPointD control2,
        CadPointD end)
    {
        Start = start;
        Control1 = control1;
        Control2 = control2;
        End = end;
    }

    public CadPointD Evaluate(double t)
    {
        t = Math.Clamp(t, 0, 1);
        var u = 1.0 - t;
        var tt = t * t;
        var uu = u * u;
        var uuu = uu * u;
        var ttt = tt * t;

        return new CadPointD(
            uuu * Start.X +
            3.0 * uu * t * Control1.X +
            3.0 * u * tt * Control2.X +
            ttt * End.X,
            uuu * Start.Y +
            3.0 * uu * t * Control1.Y +
            3.0 * u * tt * Control2.Y +
            ttt * End.Y);
    }

    public bool Equals(CadBezierSegmentD other)
    {
        return Start.Equals(other.Start) &&
               Control1.Equals(other.Control1) &&
               Control2.Equals(other.Control2) &&
               End.Equals(other.End);
    }

    public override bool Equals(object? obj)
    {
        return obj is CadBezierSegmentD other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Start, Control1, Control2, End);
    }

    public static bool operator ==(CadBezierSegmentD left, CadBezierSegmentD right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(CadBezierSegmentD left, CadBezierSegmentD right)
    {
        return !left.Equals(right);
    }
}
