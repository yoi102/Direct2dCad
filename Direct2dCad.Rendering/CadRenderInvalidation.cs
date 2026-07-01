using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering;

public readonly record struct CadScreenRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public CadScreenRect Union(CadScreenRect other)
    {
        if (IsEmpty)
            return other;

        if (other.IsEmpty)
            return this;

        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        var right = Math.Max(X + Width, other.X + other.Width);
        var bottom = Math.Max(Y + Height, other.Y + other.Height);
        return new CadScreenRect(left, top, right - left, bottom - top);
    }
}

public sealed class CadRenderInvalidation
{
    private static readonly CadRenderInvalidation FullInvalidation = new(true, default);

    private CadRenderInvalidation(bool isFull, CadScreenRect dirtyScreenRect)
    {
        IsFull = isFull;
        DirtyScreenRect = dirtyScreenRect;
    }

    public bool IsFull { get; }

    public CadScreenRect DirtyScreenRect { get; }

    public bool IsEmpty => !IsFull && DirtyScreenRect.IsEmpty;

    public static CadRenderInvalidation Full => FullInvalidation;

    public static CadRenderInvalidation FromScreenRect(CadScreenRect dirtyScreenRect)
    {
        return dirtyScreenRect.IsEmpty
            ? new CadRenderInvalidation(false, default)
            : new CadRenderInvalidation(false, dirtyScreenRect);
    }

    public static CadRenderInvalidation FromWorldBounds(
        CadViewport viewport,
        CadRectD bounds,
        int surfaceWidth,
        int surfaceHeight,
        double paddingPixels = 4.0)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        if (bounds.IsEmpty || surfaceWidth <= 0 || surfaceHeight <= 0)
            return FromScreenRect(default);

        var p1 = viewport.WorldToScreen(new CadPointD(bounds.MinX, bounds.MinY));
        var p2 = viewport.WorldToScreen(new CadPointD(bounds.MaxX, bounds.MaxY));
        var left = Math.Min(p1.X, p2.X) - paddingPixels;
        var top = Math.Min(p1.Y, p2.Y) - paddingPixels;
        var right = Math.Max(p1.X, p2.X) + paddingPixels;
        var bottom = Math.Max(p1.Y, p2.Y) + paddingPixels;

        var x = Math.Max(0, (int)Math.Floor(left));
        var y = Math.Max(0, (int)Math.Floor(top));
        var maxX = Math.Min(surfaceWidth, (int)Math.Ceiling(right));
        var maxY = Math.Min(surfaceHeight, (int)Math.Ceiling(bottom));
        return FromScreenRect(new CadScreenRect(x, y, maxX - x, maxY - y));
    }

    public CadRenderInvalidation Union(CadRenderInvalidation? other)
    {
        if (other is null || other.IsEmpty)
            return this;

        if (IsFull || other.IsFull)
            return Full;

        return FromScreenRect(DirtyScreenRect.Union(other.DirtyScreenRect));
    }
}
