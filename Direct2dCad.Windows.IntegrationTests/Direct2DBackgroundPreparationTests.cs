using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class Direct2DBackgroundPreparationTests
{
    [Fact]
    public void PreparationService_RecoversAfterWorkerFailure()
    {
        using var service = new Direct2DBackgroundPreparationService();
        var document = CadDocument.Create("Background preparation");
        var line = document.AddLine(
            CadPointD.Origin,
            new CadPointD(10, 0));
        var invalidOwner = new OwnerPreparationSnapshot(
            BlockId.ModelSpace,
            [
                new EntityPreparationSnapshot(
                    null!,
                    0,
                    0,
                    line.Bounds,
                    false,
                    true,
                    1,
                    null)
            ]);

        service.Schedule(document, 1, [invalidOwner]);

        Assert.True(SpinWait.SpinUntil(
            () => service.NeedsSchedule(document, 1),
            TimeSpan.FromSeconds(2)));

        service.Schedule(document, 2, [CreateOwnerSnapshot(line)]);
        PreparedDocumentPlan? plan = null;
        Assert.True(SpinWait.SpinUntil(
            () => (plan = service.TryGet(document, 2)) is not null,
            TimeSpan.FromSeconds(2)));

        Assert.Equal(2, plan!.Version);
        Assert.Single(plan.Owners[BlockId.ModelSpace].OrderedEntities);
    }

    [Fact]
    public void PreparationService_ReplacesStaleGeneration()
    {
        using var service = new Direct2DBackgroundPreparationService();
        var document = CadDocument.Create("Background preparation");
        var line = document.AddLine(
            CadPointD.Origin,
            new CadPointD(10, 0));
        var staleEntities = Enumerable
            .Range(0, 50_000)
            .Select(index => new EntityPreparationSnapshot(
                line,
                index % 8,
                index,
                line.Bounds,
                false,
                true,
                1,
                null))
            .ToArray();

        service.Schedule(
            document,
            1,
            [new OwnerPreparationSnapshot(BlockId.ModelSpace, staleEntities)]);
        service.Schedule(document, 2, [CreateOwnerSnapshot(line)]);

        PreparedDocumentPlan? plan = null;
        Assert.True(SpinWait.SpinUntil(
            () => (plan = service.TryGet(document, 2)) is not null,
            TimeSpan.FromSeconds(2)));

        Assert.Equal(2, plan!.Version);
        Assert.Single(plan.Owners[BlockId.ModelSpace].OrderedEntities);
    }

    private static OwnerPreparationSnapshot CreateOwnerSnapshot(
        Direct2dCad.Db.Data.Entities.CadEntity entity) =>
        new(
            BlockId.ModelSpace,
            [
                new EntityPreparationSnapshot(
                    entity,
                    0,
                    entity.ZIndex,
                    entity.Bounds,
                    entity.IsErased,
                    entity.IsVisible,
                    1,
                    null)
            ]);
}
