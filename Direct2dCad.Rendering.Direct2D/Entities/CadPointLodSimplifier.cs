using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Direct2D.Entities;

internal static class CadPointLodSimplifier
{
    public static IReadOnlyList<CadPointD> Simplify(
        IReadOnlyList<CadPointD> points,
        bool closed,
        double tolerance)
    {
        if (points.Count < (closed ? 4 : 3) ||
            tolerance <= 0 ||
            !double.IsFinite(tolerance))
        {
            return points;
        }

        return closed
            ? SimplifyClosed(points, tolerance)
            : SimplifyOpen(points, tolerance);
    }

    private static IReadOnlyList<CadPointD> SimplifyClosed(
        IReadOnlyList<CadPointD> points,
        double tolerance)
    {
        var count = points.Count;
        if (points[0] == points[^1])
            count--;
        if (count < 4)
            return points;

        var firstIndex = FindFarthestPoint(points, count, 0);
        var secondIndex = FindFarthestPoint(points, count, firstIndex);
        if (firstIndex == secondIndex)
            return points;

        var firstChain = BuildClosedChain(points, count, firstIndex, secondIndex);
        var secondChain = BuildClosedChain(points, count, secondIndex, firstIndex);
        var firstSimplified = SimplifyOpen(firstChain, tolerance);
        var secondSimplified = SimplifyOpen(secondChain, tolerance);

        var result = new List<CadPointD>(
            firstSimplified.Count + secondSimplified.Count - 2);
        result.AddRange(firstSimplified);
        for (var index = 1; index < secondSimplified.Count - 1; index++)
            result.Add(secondSimplified[index]);

        return result.Count >= 3 ? result : points;
    }

    private static IReadOnlyList<CadPointD> SimplifyOpen(
        IReadOnlyList<CadPointD> points,
        double tolerance)
    {
        if (points.Count < 3)
            return points;

        var toleranceSquared = tolerance * tolerance;
        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;

        var ranges = new Stack<(int Start, int End)>();
        ranges.Push((0, points.Count - 1));
        while (ranges.Count > 0)
        {
            var (start, end) = ranges.Pop();
            var maximumDistanceSquared = 0.0;
            var maximumIndex = -1;
            for (var index = start + 1; index < end; index++)
            {
                var distanceSquared = DistanceToSegmentSquared(
                    points[index],
                    points[start],
                    points[end]);
                if (distanceSquared <= maximumDistanceSquared)
                    continue;

                maximumDistanceSquared = distanceSquared;
                maximumIndex = index;
            }

            if (maximumIndex < 0 || maximumDistanceSquared <= toleranceSquared)
                continue;

            keep[maximumIndex] = true;
            ranges.Push((start, maximumIndex));
            ranges.Push((maximumIndex, end));
        }

        var result = new List<CadPointD>(points.Count);
        for (var index = 0; index < points.Count; index++)
        {
            if (keep[index])
                result.Add(points[index]);
        }

        return result;
    }

    private static int FindFarthestPoint(
        IReadOnlyList<CadPointD> points,
        int count,
        int originIndex)
    {
        var farthestIndex = originIndex;
        var farthestDistanceSquared = 0.0;
        var origin = points[originIndex];
        for (var index = 0; index < count; index++)
        {
            var delta = points[index] - origin;
            var distanceSquared = delta.LengthSquared;
            if (distanceSquared <= farthestDistanceSquared)
                continue;

            farthestDistanceSquared = distanceSquared;
            farthestIndex = index;
        }

        return farthestIndex;
    }

    private static IReadOnlyList<CadPointD> BuildClosedChain(
        IReadOnlyList<CadPointD> points,
        int count,
        int startIndex,
        int endIndex)
    {
        var result = new List<CadPointD>();
        var index = startIndex;
        while (true)
        {
            result.Add(points[index]);
            if (index == endIndex)
                return result;

            index = (index + 1) % count;
        }
    }

    private static double DistanceToSegmentSquared(
        CadPointD point,
        CadPointD start,
        CadPointD end)
    {
        var segment = end - start;
        var lengthSquared = segment.LengthSquared;
        if (lengthSquared <= double.Epsilon)
            return (point - start).LengthSquared;

        var projection = Math.Clamp((point - start).Dot(segment) / lengthSquared, 0.0, 1.0);
        var closest = start + segment * projection;
        return (point - closest).LengthSquared;
    }
}
