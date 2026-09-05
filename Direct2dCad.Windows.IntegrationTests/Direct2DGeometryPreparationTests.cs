using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Resources;
using Vortice.Direct2D1;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class Direct2DGeometryPreparationTests
{
    [Fact]
    public void SchedulingDoesNotCapturePointArraysAndCaptureHonorsItsItemBudget()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var service = new Direct2DGeometryPreparationService(factory);
        var document = CadDocument.Create("Deferred snapshot");
        var line = document.AddPolyline([new(0, 0), new(10, 0)]);
        service.Schedule(document);
        Assert.False(service.TryTakeNext(out _));
        line.ReplacePoints([new(0, 0), new(20, 0)]);
        service.CaptureStep(new ResourcePreparationBudget(1, TimeSpan.FromSeconds(10)));
        line.ReplacePoints([new(0, 0), new(30, 0)]);
        using var prepared = WaitForNext(service);
        Assert.Equal(20, prepared.Geometry!.ComputeLength(), 3);
    }

    [Fact]
    public void PriorityIncludesNestedDefinitionsAndSkipsInvalidatedUncapturedEntities()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var service = new Direct2DGeometryPreparationService(factory);
        var document = CadDocument.Create("Priority");
        var outside = document.AddLine(new(1000, 1000), new(1100, 1100));
        var child = document.AddPolyline([new(0, 0), new(10, 0)]);
        var definition = document.CreateBlockDefinition("Detail", CadPointD.Origin);
        document.MoveEntityToBlock(child.Id, definition);
        var reference = document.AddBlockReference(definition, CadPointD.Origin);
        service.Schedule(document);
        service.Prioritize([reference.Id, reference.Id]);
        service.Invalidate(outside.Id);
        using var first = WaitForNext(service);
        using var second = WaitForNext(service);
        Assert.Equal(reference.Id, first.EntityId);
        Assert.Equal(child.Id, second.EntityId);
        Assert.False(service.NeedsPriority);
        Assert.True(SpinWait.SpinUntil(() =>
        {
            service.CaptureStep(new ResourcePreparationBudget(64, TimeSpan.FromMilliseconds(2)));
            Assert.False(service.TryTakeNext(out _));
            return !service.IsPending;
        }, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void CaptureStepCopiesValuesAndInvalidatesOnlyEditedEntities()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var service = new Direct2DGeometryPreparationService(factory);
        var document = CadDocument.Create("Snapshot");
        var first = document.AddPolyline([new(0, 0), new(10, 0)]);
        var second = document.AddPolyline([new(0, 0), new(20, 0)]);
        service.Schedule(document);
        service.CaptureStep(new ResourcePreparationBudget(64, TimeSpan.FromSeconds(10)));
        first.ReplacePoints([new(0, 0), new(100, 0), new(100, 100)]);
        service.Invalidate(first.Id);
        second.ReplacePoints([new(0, 0), new(50, 0), new(50, 50)]);

        using var prepared = WaitForNext(service);
        Assert.Equal(second.Id, prepared.EntityId);
        Assert.Equal(2, prepared.Complexity);
        Assert.NotNull(prepared.Geometry);
        Assert.Equal(20, prepared.Geometry.ComputeLength(), 3);
        Assert.False(service.TryTakeNext(out _));
    }

    [Fact]
    public void LargeBatchPublishesBeforeCompletionAndCanBeReplacedWhileQueueIsFull()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var service = new Direct2DGeometryPreparationService(factory);
        var document = CadDocument.Create("Large");
        for (var i = 0; i < 2000; i++)
            document.AddPolyline([new(0, 0), new(i + 1, 1)]);
        service.Schedule(document);
        using var first = WaitForNext(service);
        Assert.True(service.IsPending);

        var replacement = CadDocument.Create("Replacement");
        var entity = replacement.AddPolyline([new(0, 0), new(5, 5)]);
        service.Schedule(replacement);
        using var current = WaitForNext(service);
        Assert.Equal(entity.Id, current.EntityId);
    }

    [Fact]
    public void ShapeTextPreparationCarriesRealStrokeComplexity()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var service = new Direct2DGeometryPreparationService(factory);
        var document = CadDocument.Create("Text");
        document.AddShapeText("CAD", CadPointD.Origin, 10);
        service.Schedule(document);
        using var prepared = WaitForNext(service);
        Assert.NotNull(prepared.Geometry);
        Assert.True(prepared.Complexity > 3);
    }

    [Fact]
    public void PrimitiveWithoutGeometryStillProducesAValidResult()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var service = new Direct2DGeometryPreparationService(factory);
        var document = CadDocument.Create("Primitive");
        var circle = document.AddCircle(CadPointD.Origin, 5);
        service.Schedule(document);
        using var prepared = WaitForNext(service);
        Assert.Equal(circle.Id, prepared.EntityId);
        Assert.Null(prepared.Geometry);
    }

    private static Direct2DPreparedGeometry WaitForNext(Direct2DGeometryPreparationService service)
    {
        Direct2DPreparedGeometry? prepared = null;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            service.CaptureStep(new ResourcePreparationBudget(64, TimeSpan.FromMilliseconds(2)));
            return service.TryTakeNext(out prepared);
        }, TimeSpan.FromSeconds(10)));
        return Assert.IsType<Direct2DPreparedGeometry>(prepared);
    }

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
            () =>
            {
                service.CaptureStep(new ResourcePreparationBudget(64, TimeSpan.FromMilliseconds(2)));
                return service.TryTakeNext(out prepared);
            },
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
            () =>
            {
                service.CaptureStep(new ResourcePreparationBudget(64, TimeSpan.FromMilliseconds(2)));
                return service.TryTakeNext(out prepared);
            },
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
