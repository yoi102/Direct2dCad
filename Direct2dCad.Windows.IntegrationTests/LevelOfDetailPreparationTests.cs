using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Resources;
using Vortice.Direct2D1;
using Bucket = Direct2dCad.Rendering.Direct2D.Resources.Direct2DResourceCache.EntityResourceBucket;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class LevelOfDetailPreparationTests
{
    [Fact]
    public void RequestsOnlyQueueWorkAndPreparedGeometryUsesCapturedPoints()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var preparation = new Direct2DLevelOfDetailPreparation();
        var document = CadDocument.Create("LOD");
        var line = document.AddPolyline(Points(300));
        using var bucket = new Bucket(line.Id);
        var resources = new Dictionary<EntityId, Bucket> { [line.Id] = bucket };
        preparation.Request(line.Id);
        preparation.Request(line.Id);
        Assert.Null(bucket.MediumDetailGeometry);
        Assert.False(bucket.AreLevelOfDetailGeometriesInitialized);
        Assert.True(preparation.Prepare(document, factory, resources, Budget(), out _));
        line.ReplacePoints(Points(600));
        Drain(preparation, document, factory, resources);
        Assert.True(bucket.AreLevelOfDetailGeometriesInitialized);
        Assert.NotNull(bucket.MediumDetailGeometry);
        Assert.InRange(bucket.MediumDetailGeometry.ComputeLength(), 298, 301);
    }

    [Fact]
    public void StaleGeometryCannotOverwriteEditedOrDeletedBuckets()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var preparation = new Direct2DLevelOfDetailPreparation();
        var document = CadDocument.Create("LOD");
        var edited = document.AddPolyline(Points(300));
        var deleted = document.AddPolyline(Points(300));
        using var bucket = new Bucket(edited.Id);
        using var removed = new Bucket(deleted.Id);
        var resources = new Dictionary<EntityId, Bucket> { [edited.Id] = bucket, [deleted.Id] = removed };
        preparation.Request(edited.Id);
        preparation.Request(deleted.Id);
        preparation.Prepare(document, factory, resources, Budget(), out _);
        edited.ReplacePoints(Points(600));
        bucket.LodRevision++;
        resources.Remove(deleted.Id);
        preparation.Request(edited.Id);
        Drain(preparation, document, factory, resources);
        Assert.InRange(bucket.MediumDetailGeometry!.ComputeLength(), 598, 601);
        Assert.Null(removed.MediumDetailGeometry);
    }

    [Fact]
    public void FilledClosedSplineNeverReceivesTopologyChangingSimplification()
    {
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);
        using var preparation = new Direct2DLevelOfDetailPreparation();
        var document = CadDocument.Create("LOD");
        var spline = document.AddSpline(Points(30), closed: true);
        spline.SetFillStyleInternal(new StyleId(123));
        using var bucket = new Bucket(spline.Id);
        var resources = new Dictionary<EntityId, Bucket> { [spline.Id] = bucket };
        preparation.Request(spline.Id);
        Drain(preparation, document, factory, resources);
        Assert.True(bucket.AreLevelOfDetailGeometriesInitialized);
        Assert.Null(bucket.MediumDetailGeometry);
        Assert.Null(bucket.LowDetailGeometry);
        preparation.Dispose();
        preparation.Dispose();
        Assert.Throws<ObjectDisposedException>(() => preparation.Request(spline.Id));
    }

    private static IEnumerable<CadPointD> Points(int count) =>
        Enumerable.Range(0, count).Select(index => new CadPointD(index, index % 2 * 0.001));

    private static ResourcePreparationBudget Budget() => new(64, TimeSpan.FromMilliseconds(2));

    private static void Drain(Direct2DLevelOfDetailPreparation preparation, CadDocument document,
        ID2D1Factory factory, IReadOnlyDictionary<EntityId, Bucket> resources) =>
        Assert.True(SpinWait.SpinUntil(() => !preparation.Prepare(document, factory, resources, Budget(), out _),
            TimeSpan.FromSeconds(10)));
}
