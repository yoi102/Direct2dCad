using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class Direct2DDirtyRegionPlannerTests
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Normalize_NullFullOrInvalidTargetFallsBackToFull()
    {
        var planner = new Direct2DDirtyRegionPlanner();
        var partial = CadRenderInvalidation.FromScreenRect(new CadScreenRect(1, 2, 3, 4));

        Assert.True(planner.Normalize(null, 100, 100, ConstantCost).IsFull);
        Assert.True(planner.Normalize(CadRenderInvalidation.Full, 100, 100, ConstantCost).IsFull);
        Assert.True(planner.Normalize(partial, 0, 100, ConstantCost).IsFull);
        Assert.True(planner.Normalize(partial, 100, 0, ConstantCost).IsFull);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Normalize_ClampsRectsAndDropsRectsOutsideTarget()
    {
        var planner = new Direct2DDirtyRegionPlanner();
        var invalidation = CadRenderInvalidation.FromScreenRects(
        [
            new CadScreenRect(-50, -50, 20, 20),
            new CadScreenRect(90, 90, 30, 30),
            new CadScreenRect(120, 0, 10, 10)
        ]);

        var normalized = planner.Normalize(
            invalidation,
            100,
            100,
            ConstantCost);

        var dirty = Assert.Single(normalized.DirtyScreenRects);
        Assert.Equal(new CadScreenRect(90, 90, 10, 10), dirty);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Normalize_ReturnsEmptyWhenAllRectsAreOutsideTarget()
    {
        var planner = new Direct2DDirtyRegionPlanner();
        var invalidation = CadRenderInvalidation.FromScreenRect(
            new CadScreenRect(200, 200, 10, 10));

        var normalized = planner.Normalize(
            invalidation,
            100,
            100,
            ConstantCost);

        Assert.True(normalized.IsEmpty);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Normalize_AreaAtPartialThresholdFallsBackToFull()
    {
        var planner = new Direct2DDirtyRegionPlanner();
        var invalidation = CadRenderInvalidation.FromScreenRect(
            new CadScreenRect(0, 0, 65, 100));

        var normalized = planner.Normalize(
            invalidation,
            100,
            100,
            ConstantCost);

        Assert.True(normalized.IsFull);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Normalize_UsesCostToMergeSeparatedRects()
    {
        var planner = new Direct2DDirtyRegionPlanner();
        var invalidation = CadRenderInvalidation.FromScreenRects(
        [
            new CadScreenRect(0, 0, 10, 10),
            new CadScreenRect(20, 0, 10, 10)
        ]);

        var normalized = planner.Normalize(
            invalidation,
            1000,
            1000,
            ConstantCost);

        var dirty = Assert.Single(normalized.DirtyScreenRects);
        Assert.Equal(new CadScreenRect(0, 0, 30, 10), dirty);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Normalize_PreservesInfiniteCrossStripsAfterCostOptimization()
    {
        var planner = new Direct2DDirtyRegionPlanner();
        var invalidation = CadRenderInvalidation.FromScreenRectsPreservingCoverage(
        [
            new CadScreenRect(0, 108, 320, 24),
            new CadScreenRect(148, 0, 24, 240)
        ]);

        var normalized = planner.Normalize(
            invalidation,
            320,
            240,
            ConstantCost);

        Assert.False(normalized.IsFull);
        Assert.Equal(new CadScreenRect(0, 0, 320, 240), normalized.DirtyScreenRect);
        Assert.Equal(3, normalized.DirtyScreenRects.Count);
        Assert.Equal(12_864, normalized.DirtyScreenRects.Sum(rect => rect.Area));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Normalize_KeepsSeparatedRectsWhenUnionIsMoreExpensive()
    {
        var planner = new Direct2DDirtyRegionPlanner();
        var invalidation = CadRenderInvalidation.FromScreenRects(
        [
            new CadScreenRect(0, 0, 10, 10),
            new CadScreenRect(20, 0, 10, 10)
        ]);

        var normalized = planner.Normalize(
            invalidation,
            1000,
            1000,
            static rect => rect.Area);

        Assert.Equal(2, normalized.DirtyScreenRects.Count);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Normalize_CompactsMoreThanPairwiseLimitBeforeOptimizing()
    {
        var planner = new Direct2DDirtyRegionPlanner();
        var rects = Enumerable.Range(0, 9)
            .Select(index => new CadScreenRect(index * 20, 0, 10, 10));
        var invalidation = CadRenderInvalidation.FromScreenRects(rects);

        var normalized = planner.Normalize(
            invalidation,
            1000,
            1000,
            ConstantCost);

        var dirty = Assert.Single(normalized.DirtyScreenRects);
        Assert.Equal(new CadScreenRect(0, 0, 170, 10), dirty);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void TryGetCombinedBounds_ReturnsCombinedBoundsWhenCostAndCoverageAllowIt()
    {
        var planner = new Direct2DDirtyRegionPlanner();
        var invalidation = CadRenderInvalidation.FromScreenRects(
        [
            new CadScreenRect(0, 0, 10, 10),
            new CadScreenRect(20, 0, 10, 10)
        ]);

        var combined = planner.TryGetCombinedBounds(
            invalidation,
            1000,
            1000,
            ConstantCost,
            out var combinedBounds);

        Assert.True(combined);
        Assert.Equal(new CadScreenRect(0, 0, 30, 10), combinedBounds);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void TryGetCombinedBounds_RejectsLargeCoverageWaste()
    {
        var planner = new Direct2DDirtyRegionPlanner();
        var invalidation = CadRenderInvalidation.FromScreenRects(
        [
            new CadScreenRect(0, 0, 10, 10),
            new CadScreenRect(90, 90, 10, 10)
        ]);

        var combined = planner.TryGetCombinedBounds(
            invalidation,
            100,
            100,
            ConstantCost,
            out _);

        Assert.False(combined);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void TryGetCombinedBounds_RejectsCombinedBoundsCoveringMostOfTarget()
    {
        var planner = new Direct2DDirtyRegionPlanner();
        var invalidation = CadRenderInvalidation.FromScreenRects(
        [
            new CadScreenRect(0, 0, 10, 10),
            new CadScreenRect(30, 30, 10, 10)
        ]);

        var combined = planner.TryGetCombinedBounds(
            invalidation,
            50,
            50,
            ConstantCost,
            out _);

        Assert.False(combined);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void TryGetCombinedBounds_RejectsCombinedCostThatExceedsTolerance()
    {
        var planner = new Direct2DDirtyRegionPlanner();
        var invalidation = CadRenderInvalidation.FromScreenRects(
        [
            new CadScreenRect(0, 0, 10, 10),
            new CadScreenRect(20, 0, 10, 10)
        ]);

        var combined = planner.TryGetCombinedBounds(
            invalidation,
            1000,
            1000,
            static rect => rect.Area,
            out _);

        Assert.False(combined);
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void TryGetCombinedBounds_RejectsFullAndSingleRegionInvalidations()
    {
        var planner = new Direct2DDirtyRegionPlanner();
        var single = CadRenderInvalidation.FromScreenRect(
            new CadScreenRect(1, 1, 10, 10));

        Assert.False(planner.TryGetCombinedBounds(
            CadRenderInvalidation.Full,
            100,
            100,
            ConstantCost,
            out _));
        Assert.False(planner.TryGetCombinedBounds(
            single,
            100,
            100,
            ConstantCost,
            out _));
    }

    private static double ConstantCost(CadScreenRect _) => 1.0;
}
