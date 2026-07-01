using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering;

public readonly record struct CadScreenRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public long Area => IsEmpty ? 0 : (long)Width * Height;

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

    public bool IntersectsOrTouches(CadScreenRect other)
    {
        if (IsEmpty || other.IsEmpty)
            return false;

        return X <= other.X + other.Width &&
               other.X <= X + Width &&
               Y <= other.Y + other.Height &&
               other.Y <= Y + Height;
    }
}

public sealed class CadRenderInvalidation
{
    private const int MaxDirtyScreenRectCount = 12;
    private const double MaxMergeWasteRatio = 1.25;
    private static readonly CadRenderInvalidation EmptyInvalidation = new(false, [], default);
    private static readonly CadRenderInvalidation FullInvalidation = new(true, [], default);
    private readonly CadScreenRect[] _dirtyScreenRects;

    private CadRenderInvalidation(
        bool isFull,
        CadScreenRect[] dirtyScreenRects,
        CadScreenRect dirtyScreenRect)
    {
        IsFull = isFull;
        _dirtyScreenRects = dirtyScreenRects;
        DirtyScreenRect = dirtyScreenRect;
    }

    public bool IsFull { get; }

    public IReadOnlyList<CadScreenRect> DirtyScreenRects => _dirtyScreenRects;

    public CadScreenRect DirtyScreenRect { get; }

    public bool IsEmpty => !IsFull && _dirtyScreenRects.Length == 0;

    public static CadRenderInvalidation Empty => EmptyInvalidation;

    public static CadRenderInvalidation Full => FullInvalidation;

    public static CadRenderInvalidation FromScreenRect(CadScreenRect dirtyScreenRect)
    {
        return dirtyScreenRect.IsEmpty
            ? Empty
            : new CadRenderInvalidation(false, [dirtyScreenRect], dirtyScreenRect);
    }

    public static CadRenderInvalidation FromScreenRects(IEnumerable<CadScreenRect> dirtyScreenRects)
    {
        ArgumentNullException.ThrowIfNull(dirtyScreenRects);

        var merged = new List<CadScreenRect>();
        foreach (var rect in dirtyScreenRects)
        {
            if (rect.IsEmpty)
                continue;

            AddDirtyRect(merged, rect);
        }

        if (merged.Count == 0)
            return Empty;

        var aggregate = CalculateAggregate(merged);
        return new CadRenderInvalidation(false, [.. merged], aggregate);
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

        if (IsEmpty)
            return other;

        if (IsFull || other.IsFull)
            return Full;

        return FromScreenRects(_dirtyScreenRects.Concat(other._dirtyScreenRects));
    }

    private static void AddDirtyRect(List<CadScreenRect> rects, CadScreenRect rect)
    {
        var rectToAdd = rect;

        for (var i = 0; i < rects.Count; i++)
        {
            var existing = rects[i];
            if (!ShouldMerge(existing, rectToAdd))
                continue;

            rectToAdd = existing.Union(rectToAdd);
            rects.RemoveAt(i);
            i = -1;
        }

        rects.Add(rectToAdd);

        if (rects.Count > MaxDirtyScreenRectCount)
        {
            var aggregate = CalculateAggregate(rects);
            rects.Clear();
            rects.Add(aggregate);
        }
    }

    private static bool ShouldMerge(CadScreenRect first, CadScreenRect second)
    {
        if (first.IntersectsOrTouches(second))
            return true;

        var union = first.Union(second);
        var sourceArea = first.Area + second.Area;
        return sourceArea > 0 && union.Area <= sourceArea * MaxMergeWasteRatio;
    }

    private static CadScreenRect CalculateAggregate(IReadOnlyList<CadScreenRect> rects)
    {
        var aggregate = default(CadScreenRect);
        foreach (var rect in rects)
            aggregate = aggregate.Union(rect);

        return aggregate;
    }
}
