using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Resources;
using Vortice.Direct2D1;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class Direct2DGeometryPreparationTests
{
    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Schedule_PreparesGeometryAndTransfersItToTheRenderThread()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var service = new Direct2DGeometryPreparationService(factory);
        var document = CadDocument.Create("Background geometry");
        var line = document.AddPolyline([new CadPointD(2, 3), new CadPointD(18, 24)]);

        service.Schedule(document);

        Direct2DPreparedGeometry? prepared = null;
        Assert.True(SpinWait.SpinUntil(
            () => service.TryTakeNext(out prepared),
            TimeSpan.FromSeconds(5)));
        Assert.NotNull(prepared);
        Assert.Equal(line.Id, prepared!.EntityId);
        Assert.NotNull(prepared.Geometry);
        Assert.True(prepared.Complexity > 0);
        prepared.Dispose();
        Assert.False(service.TryTakeNext(out _));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Schedule_ReplacesAnOlderPendingBatch()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var service = new Direct2DGeometryPreparationService(factory);
        var firstDocument = CadDocument.Create("First batch");
        firstDocument.AddPolyline([CadPointD.Origin, new CadPointD(1, 1)]);
        var secondDocument = CadDocument.Create("Second batch");
        var secondLine = secondDocument.AddPolyline([CadPointD.Origin, new CadPointD(4, 9)]);

        service.Schedule(firstDocument);
        service.Schedule(secondDocument);

        Direct2DPreparedGeometry? prepared = null;
        Assert.True(SpinWait.SpinUntil(
            () => service.TryTakeNext(out prepared),
            TimeSpan.FromSeconds(5)));
        Assert.NotNull(prepared);
        Assert.Equal(secondLine.Id, prepared!.EntityId);
        prepared.Dispose();
        Assert.False(service.TryTakeNext(out _));
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void Dispose_RejectsFurtherSchedulingAndPolling()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        var service = new Direct2DGeometryPreparationService(factory);
        service.Dispose();
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => service.Schedule(CadDocument.Create("Disposed")));
        Assert.Throws<ObjectDisposedException>(
            () => service.TryTakeNext(out _));
        Assert.Throws<ObjectDisposedException>(() => _ = service.IsPending);
    }
}
