using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;

namespace Direct2dCad.Tests;

public sealed class CadRenderInvalidationTests
{
    [Theory]
    [InlineData(257)]
    [InlineData(20000)]
    public void BulkReductionRetainsEveryInputRectangleAndLimitsOutput(int count)
    {
        var random = new Random(73);
        var rects = Enumerable.Range(0, count).Select(_ => new CadScreenRect(
            random.Next(-10000, 10000), random.Next(-10000, 10000),
            random.Next(1, 100), random.Next(1, 100))).ToArray();
        var result = CadRenderInvalidation.FromScreenRects(rects);
        Assert.False(result.IsFull);
        Assert.InRange(result.DirtyScreenRects.Count, 1, 32);
        foreach (var rect in rects)
            Assert.Contains(result.DirtyScreenRects, coverage => Contains(coverage, rect));
        for (var left = 0; left < result.DirtyScreenRects.Count; left++)
        for (var right = left + 1; right < result.DirtyScreenRects.Count; right++)
            Assert.False(result.DirtyScreenRects[left].Intersects(result.DirtyScreenRects[right]));
    }

    [Fact]
    public void BulkReductionDoesNotConnectTwoDistantClusters()
    {
        var rects = Enumerable.Range(0, 1000).Select(i => i % 2 == 0
            ? new CadScreenRect(0, 0, 10, 10)
            : new CadScreenRect(10000, 10000, 10, 10)).ToArray();
        Assert.Equal(2, CadRenderInvalidation.FromScreenRects(rects).DirtyScreenRects.Count);
        Assert.True(CadRenderInvalidation.FromScreenRects(new CadScreenRect[1000]).IsEmpty);
    }

    private static bool Contains(CadScreenRect outer, CadScreenRect inner) =>
        outer.X <= inner.X && outer.Y <= inner.Y &&
        (long)outer.X + outer.Width >= (long)inner.X + inner.Width &&
        (long)outer.Y + outer.Height >= (long)inner.Y + inner.Height;

    [Fact]
    public void FromScreenRects_KeepsCornerTouchingThinRectsSeparate()
    {
        var horizontal = new CadScreenRect(0, 0, 100, 2);
        var vertical = new CadScreenRect(100, 2, 2, 100);

        var invalidation = CadRenderInvalidation.FromScreenRects(
            [horizontal, vertical]);

        Assert.Equal(2, invalidation.DirtyScreenRects.Count);
    }

    [Fact]
    public void FromScreenRects_MergesOverlappingRects()
    {
        var invalidation = CadRenderInvalidation.FromScreenRects(
        [
            new CadScreenRect(10, 10, 20, 20),
            new CadScreenRect(25, 25, 20, 20)
        ]);

        var dirty = Assert.Single(invalidation.DirtyScreenRects);
        Assert.Equal(new CadScreenRect(10, 10, 35, 35), dirty);
    }

    [Fact]
    public void FromScreenRectsPreservingCoverage_DecomposesCrossWithoutExpandingToBounds()
    {
        var horizontal = new CadScreenRect(0, 90, 200, 20);
        var vertical = new CadScreenRect(90, 0, 20, 200);

        var invalidation = CadRenderInvalidation.FromScreenRectsPreservingCoverage(
            [horizontal, vertical]);

        Assert.Equal(new CadScreenRect(0, 0, 200, 200), invalidation.DirtyScreenRect);
        Assert.Equal(3, invalidation.DirtyScreenRects.Count);
        Assert.Equal(7_600, invalidation.DirtyScreenRects.Sum(rect => rect.Area));
        for (var left = 0; left < invalidation.DirtyScreenRects.Count - 1; left++)
        {
            for (var right = left + 1; right < invalidation.DirtyScreenRects.Count; right++)
            {
                Assert.False(invalidation.DirtyScreenRects[left]
                    .Intersects(invalidation.DirtyScreenRects[right]));
            }
        }
    }

    [Fact]
    public void UnionPreservingCoverage_KeepsMovedCrossesAsDisjointStrips()
    {
        var first = CadRenderInvalidation.FromScreenRectsPreservingCoverage(
        [
            new CadScreenRect(0, 40, 200, 20),
            new CadScreenRect(40, 0, 20, 200)
        ]);
        var second = CadRenderInvalidation.FromScreenRectsPreservingCoverage(
        [
            new CadScreenRect(0, 140, 200, 20),
            new CadScreenRect(140, 0, 20, 200)
        ]);

        var invalidation = first.UnionPreservingCoverage(second);

        Assert.Equal(new CadScreenRect(0, 0, 200, 200), invalidation.DirtyScreenRect);
        Assert.True(invalidation.DirtyScreenRects.Count > 1);
        Assert.Equal(14_400, invalidation.DirtyScreenRects.Sum(rect => rect.Area));
        Assert.All(
            invalidation.DirtyScreenRects,
            rect => Assert.True(rect.Area < 200L * 200));
    }

    [Fact]
    public void FromScreenRects_NormalizesForcedMergeAgainstRemainingRects()
    {
        var rects = Enumerable.Range(0, 33)
            .Select(index => new CadScreenRect(index * 20, 0, 8, 8))
            .ToArray();

        var invalidation = CadRenderInvalidation.FromScreenRects(rects);

        Assert.True(invalidation.DirtyScreenRects.Count <= 32);
        for (var left = 0; left < invalidation.DirtyScreenRects.Count - 1; left++)
        {
            for (var right = left + 1; right < invalidation.DirtyScreenRects.Count; right++)
            {
                Assert.False(
                    invalidation.DirtyScreenRects[left]
                        .Intersects(invalidation.DirtyScreenRects[right]));
            }
        }
    }

    [Fact]
    public void FromWorldBounds_ProjectsPaddingAndClampsToSurface()
    {
        var viewport = new CadViewport();
        viewport.SetSize(100, 80);
        viewport.SetView(2.0, new CadPointD(50, 40));

        var invalidation = CadRenderInvalidation.FromWorldBounds(
            viewport,
            CadRectD.FromXYWH(-30, -20, 20, 20),
            100,
            80,
            paddingPixels: 4.0);

        var dirty = Assert.Single(invalidation.DirtyScreenRects);
        Assert.Equal(new CadScreenRect(0, 36, 34, 44), dirty);
    }

    [Fact]
    public void FromWorldBounds_ReturnsEmptyForEmptyBoundsOrSurface()
    {
        var viewport = new CadViewport();
        viewport.SetSize(100, 80);
        viewport.SetView(1.0, CadPointD.Origin);

        Assert.True(CadRenderInvalidation.FromWorldBounds(
            viewport,
            CadRectD.Empty,
            100,
            80).IsEmpty);
        Assert.True(CadRenderInvalidation.FromWorldBounds(
            viewport,
            CadRectD.FromXYWH(0, 0, 10, 10),
            0,
            80).IsEmpty);
    }

    [Fact]
    public void FromWorldBounds_ReturnsFullForNonFinitePadding()
    {
        var viewport = new CadViewport();
        viewport.SetSize(100, 80);
        viewport.SetView(1.0, CadPointD.Origin);

        var invalidation = CadRenderInvalidation.FromWorldBounds(
            viewport,
            CadRectD.FromXYWH(double.MaxValue, 0, 10, 10),
            100,
            80,
            paddingPixels: double.PositiveInfinity);

        Assert.True(invalidation.IsFull);
    }

    [Fact]
    public void FromWorldBounds_NegativePaddingDoesNotShrinkDirtyRegion()
    {
        var viewport = new CadViewport();
        viewport.SetSize(100, 80);
        viewport.SetView(1.0, new CadPointD(50, 40));
        var bounds = CadRectD.FromXYWH(-10, -10, 20, 20);

        var invalidation = CadRenderInvalidation.FromWorldBounds(
            viewport,
            bounds,
            100,
            80,
            paddingPixels: -100);
        var expected = CadRenderInvalidation.FromWorldBounds(
            viewport,
            bounds,
            100,
            80,
            paddingPixels: 0);

        Assert.Equal(expected.DirtyScreenRects, invalidation.DirtyScreenRects);
    }

    [Fact]
    public void Union_PropagatesFullAndIgnoresEmptyInvalidations()
    {
        var partial = CadRenderInvalidation.FromScreenRect(
            new CadScreenRect(10, 10, 20, 20));

        Assert.Same(partial, partial.Union(CadRenderInvalidation.Empty));
        Assert.Same(CadRenderInvalidation.Full, partial.Union(CadRenderInvalidation.Full));
    }
}
