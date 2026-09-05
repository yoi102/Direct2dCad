using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering.Direct2D.Resources;
using Vortice.Direct2D1;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class ResourcePreparationBudgetTests
{
    [Fact]
    public void TimeAndItemBudgetsBothStopFurtherWork()
    {
        var clock = new ManualClock();
        var timeBudget = new ResourcePreparationBudget(64, TimeSpan.FromMilliseconds(2), clock);
        Assert.True(timeBudget.TryStartItem());
        clock.Ticks = TimeSpan.FromMilliseconds(2).Ticks;
        Assert.False(timeBudget.TryStartItem());
        Assert.Equal(1, timeBudget.ProcessedItems);
        var countBudget = new ResourcePreparationBudget(1, TimeSpan.FromDays(1), clock);
        Assert.True(countBudget.TryStartItem());
        Assert.False(countBudget.TryStartItem());
    }

    [Fact]
    public void InvalidatedResultsCountAgainstTheConsumptionBudget()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var service = new Direct2DGeometryPreparationService(factory);
        var document = CadDocument.Create("Budget");
        for (var index = 0; index < 300; index++)
            document.AddPolyline([new(0, 0), new(index + 1, 1)]);
        service.Schedule(document);
        Direct2DPreparedGeometry? first = null;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            service.CaptureStep(new ResourcePreparationBudget(64, TimeSpan.FromMilliseconds(2)));
            return service.TryTakeNext(out first);
        }, TimeSpan.FromSeconds(5)));
        first!.Dispose();
        service.Invalidate(document.Entities.Keys);
        var budget = new ResourcePreparationBudget(2, TimeSpan.FromDays(1));
        Assert.False(service.TryTakeNext(out _, budget));
        Assert.InRange(budget.ProcessedItems, 1, 2);
        Assert.True(service.IsPending);
        Assert.True(SpinWait.SpinUntil(() =>
        {
            service.CaptureStep(new ResourcePreparationBudget(64, TimeSpan.FromMilliseconds(2)));
            service.TryTakeNext(out var next);
            next?.Dispose();
            return !service.IsPending;
        }, TimeSpan.FromSeconds(5)));
    }

    private sealed class ManualClock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }
}
