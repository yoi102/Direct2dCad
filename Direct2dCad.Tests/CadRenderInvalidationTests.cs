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
}
