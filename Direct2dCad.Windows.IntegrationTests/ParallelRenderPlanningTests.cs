using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class ParallelRenderPlanningTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void WeightedBatchesPreserveOrderAndBalanceMixedComplexity(int workers)
    {
        var document = CadDocument.Create("Weighted batches");
        for (var i = 0; i < 120; i++) document.AddLine(new(i, 0), new(i, 1));
        for (var i = 0; i < 12; i++) document.AddSpline(Enumerable.Range(0, 32)
            .Select(index => new CadPointD(index, i + index % 2)));
        var entities = document.Entities.Values.ToArray();
        var options = new CadRenderOptions { IsParallelRenderingEnabled = true,
            ParallelRenderingMode = CadParallelRenderingMode.SharedDeviceContexts,
            ParallelRenderingEntityThreshold = 2, ParallelRenderingWorkerCount = workers };
        Assert.True(Direct2DParallelRenderPlanner.TryCreatePlan(document, options,
            options.ParallelRenderingMode, entities, 320, 240, out var plan));
        Assert.Equal(entities, plan.Batches.SelectMany(batch => batch));
        Assert.All(plan.Batches, batch => Assert.NotEmpty(batch));
        var costs = plan.Batches.Select(batch => batch.Sum(entity =>
            Direct2DEntityOrderCache.EstimateEntityRenderWork(document, entity))).ToArray();
        Assert.InRange(costs.Max() - costs.Min(), 0, 32);
    }

    [Fact]
    public void ParallelVisibilityUsesBufferedQueryAndRestoresDrawingOrder()
    {
        var document = CadDocument.Create("Indexed visibility");
        var first = document.AddLine(new(0, 0), new(1, 1));
        var second = document.AddLine(new(2, 0), new(3, 1));
        for (var i = 0; i < 1000; i++) document.AddLine(new(10000 + i, 0), new(10000 + i, 1));
        var viewport = new CadViewport();
        viewport.SetSize(320, 240);
        viewport.SetView(2, new(160, 120));
        var queries = 0;
        var options = new CadRenderOptions
        {
            EntityBoundsQueryInto = (owner, bounds, output) =>
            {
                queries++;
                Assert.Equal(Direct2dCad.Db.BlockId.ModelSpace, owner);
                output.Add(second.Id);
                output.Add(first.Id);
            }
        };
        using var renderer = new Direct2DSceneRender();
        var result = renderer.GetVisibleEntitiesForParallelRendering(document, viewport, options);
        Assert.Equal(1, queries);
        Assert.Equal(new CadEntity[] { first, second }, result);
    }
}
