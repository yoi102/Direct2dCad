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
        var right = Math.Max((long)X + Width, (long)other.X + other.Width);
        var bottom = Math.Max((long)Y + Height, (long)other.Y + other.Height);
        return new CadScreenRect(
            left,
            top,
            SaturateDimension(right - left),
            SaturateDimension(bottom - top));
    }

    public bool Intersects(CadScreenRect other)
    {
        if (IsEmpty || other.IsEmpty)
            return false;

        return X < (long)other.X + other.Width &&
               other.X < (long)X + Width &&
               Y < (long)other.Y + other.Height &&
               other.Y < (long)Y + Height;
    }

    public bool IntersectsOrTouches(CadScreenRect other)
    {
        if (IsEmpty || other.IsEmpty)
            return false;

        return X <= (long)other.X + other.Width &&
               other.X <= (long)X + Width &&
               Y <= (long)other.Y + other.Height &&
               other.Y <= (long)Y + Height;
    }

    private static int SaturateDimension(long value)
    {
        return (int)Math.Clamp(value, 0L, int.MaxValue);
    }
}

public sealed class CadRenderInvalidation
{
    private const int MaxDirtyScreenRectCount = 32;
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

        dirtyScreenRects.TryGetNonEnumeratedCount(out var dirtyRectCount);
        if (dirtyRectCount > 256)
            return ReduceLargeBatch(dirtyScreenRects);
        var merged = new List<CadScreenRect>(dirtyRectCount);
        foreach (var rect in dirtyScreenRects)
        {
            if (rect.IsEmpty)
                continue;

            AddDirtyRect(merged, rect);
        }

        if (merged.Count == 0)
            return Empty;

        return CreateFromMergedRects(merged);
    }

    private static CadRenderInvalidation ReduceLargeBatch(IEnumerable<CadScreenRect> rectangles)
    {
        var source = rectangles as IReadOnlyList<CadScreenRect> ?? rectangles.ToArray();
        var aggregate = CalculateAggregate(source);
        if (aggregate.IsEmpty)
            return Empty;

        // Bound planning work for bulk edits. Each bucket conservatively contains
        // its complete input rectangles, including those crossing bucket boundaries.
        var buckets = new CadScreenRect[MaxDirtyScreenRectCount];
        foreach (var rect in source)
        {
            if (rect.IsEmpty)
                continue;
            var column = (int)Math.Clamp(
                ((double)rect.X + rect.Width * 0.5 - aggregate.X) / aggregate.Width * 8, 0, 7);
            var row = (int)Math.Clamp(
                ((double)rect.Y + rect.Height * 0.5 - aggregate.Y) / aggregate.Height * 4, 0, 3);
            var index = row * 8 + column;
            buckets[index] = buckets[index].Union(rect);
        }
        return FromScreenRects(buckets);
    }

    public static CadRenderInvalidation FromScreenRectsPreservingCoverage(
        IEnumerable<CadScreenRect> dirtyScreenRects)
    {
        ArgumentNullException.ThrowIfNull(dirtyScreenRects);

        var source = dirtyScreenRects
            .Where(static rect => !rect.IsEmpty)
            .ToArray();
        if (source.Length == 0)
            return Empty;

        var disjoint = new List<CadScreenRect>(source.Length);
        var fragments = new List<CadScreenRect>(4);
        var nextFragments = new List<CadScreenRect>(4);
        foreach (var rect in source)
        {
            fragments.Clear();
            fragments.Add(rect);
            foreach (var existing in disjoint)
            {
                nextFragments.Clear();
                foreach (var fragment in fragments)
                    AddDifference(nextFragments, fragment, existing);

                (fragments, nextFragments) = (nextFragments, fragments);
                if (fragments.Count == 0)
                    break;
            }

            disjoint.AddRange(fragments);
            if (disjoint.Count > MaxDirtyScreenRectCount)
                return FromScreenRects(source);
        }

        return CreateFromMergedRects(disjoint);
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

        paddingPixels = Math.Max(0.0, paddingPixels);
        var p1 = viewport.WorldToScreen(new CadPointD(bounds.MinX, bounds.MinY));
        var p2 = viewport.WorldToScreen(new CadPointD(bounds.MaxX, bounds.MaxY));
        var left = Math.Min(p1.X, p2.X) - paddingPixels;
        var top = Math.Min(p1.Y, p2.Y) - paddingPixels;
        var right = Math.Max(p1.X, p2.X) + paddingPixels;
        var bottom = Math.Max(p1.Y, p2.Y) + paddingPixels;

        if (!double.IsFinite(left) ||
            !double.IsFinite(top) ||
            !double.IsFinite(right) ||
            !double.IsFinite(bottom))
        {
            return Full;
        }

        var x = (int)Math.Floor(Math.Clamp(left, 0.0, surfaceWidth));
        var y = (int)Math.Floor(Math.Clamp(top, 0.0, surfaceHeight));
        var maxX = (int)Math.Ceiling(Math.Clamp(right, 0.0, surfaceWidth));
        var maxY = (int)Math.Ceiling(Math.Clamp(bottom, 0.0, surfaceHeight));
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

        var merged = new List<CadScreenRect>(
            _dirtyScreenRects.Length + other._dirtyScreenRects.Length);
        foreach (var rect in _dirtyScreenRects)
            AddDirtyRect(merged, rect);
        foreach (var rect in other._dirtyScreenRects)
            AddDirtyRect(merged, rect);

        return CreateFromMergedRects(merged);
    }

    public CadRenderInvalidation UnionPreservingCoverage(CadRenderInvalidation? other)
    {
        if (other is null || other.IsEmpty)
            return this;

        if (IsEmpty)
            return other;

        if (IsFull || other.IsFull)
            return Full;

        return FromScreenRectsPreservingCoverage(
            _dirtyScreenRects.Concat(other._dirtyScreenRects));
    }

    private static CadRenderInvalidation CreateFromMergedRects(List<CadScreenRect> merged)
    {
        var aggregate = CalculateAggregate(merged);
        return new CadRenderInvalidation(false, [.. merged], aggregate);
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
            MergeLowestWastePair(rects);
    }

    private static void AddDifference(
        List<CadScreenRect> destination,
        CadScreenRect source,
        CadScreenRect covered)
    {
        if (!source.Intersects(covered))
        {
            destination.Add(source);
            return;
        }

        var sourceRight = (long)source.X + source.Width;
        var sourceBottom = (long)source.Y + source.Height;
        var intersectionLeft = Math.Max(source.X, covered.X);
        var intersectionTop = Math.Max(source.Y, covered.Y);
        var intersectionRight = Math.Min(
            sourceRight,
            (long)covered.X + covered.Width);
        var intersectionBottom = Math.Min(
            sourceBottom,
            (long)covered.Y + covered.Height);

        AddIfNotEmpty(destination, source.X, source.Y, sourceRight, intersectionTop);
        AddIfNotEmpty(destination, source.X, intersectionBottom, sourceRight, sourceBottom);
        AddIfNotEmpty(
            destination,
            source.X,
            intersectionTop,
            intersectionLeft,
            intersectionBottom);
        AddIfNotEmpty(
            destination,
            intersectionRight,
            intersectionTop,
            sourceRight,
            intersectionBottom);
    }

    private static void AddIfNotEmpty(
        List<CadScreenRect> destination,
        long left,
        long top,
        long right,
        long bottom)
    {
        if (right <= left || bottom <= top)
            return;

        destination.Add(new CadScreenRect(
            (int)Math.Clamp(left, int.MinValue, int.MaxValue),
            (int)Math.Clamp(top, int.MinValue, int.MaxValue),
            (int)Math.Clamp(right - left, 0L, int.MaxValue),
            (int)Math.Clamp(bottom - top, 0L, int.MaxValue)));
    }

    private static void MergeLowestWastePair(List<CadScreenRect> rects)
    {
        if (rects.Count < 2)
            return;

        var bestLeft = 0;
        var bestRight = 1;
        var bestUnion = rects[0].Union(rects[1]);
        var bestWaste = CalculateMergeWaste(rects[0], rects[1], bestUnion);

        for (var left = 0; left < rects.Count - 1; left++)
        {
            for (var right = left + 1; right < rects.Count; right++)
            {
                var union = rects[left].Union(rects[right]);
                var waste = CalculateMergeWaste(rects[left], rects[right], union);
                if (waste > bestWaste ||
                    waste == bestWaste && union.Area >= bestUnion.Area)
                {
                    continue;
                }

                bestLeft = left;
                bestRight = right;
                bestUnion = union;
                bestWaste = waste;
            }
        }

        rects.RemoveAt(bestRight);
        rects.RemoveAt(bestLeft);
        AddDirtyRect(rects, bestUnion);
    }

    private static long CalculateMergeWaste(
        CadScreenRect first,
        CadScreenRect second,
        CadScreenRect union) => Math.Max(0, union.Area - first.Area - second.Area);

    private static bool ShouldMerge(CadScreenRect first, CadScreenRect second)
    {
        if (first.Intersects(second))
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
