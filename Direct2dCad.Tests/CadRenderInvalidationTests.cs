using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;

namespace Direct2dCad.Tests;

public sealed class CadRenderInvalidationTests
{
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
