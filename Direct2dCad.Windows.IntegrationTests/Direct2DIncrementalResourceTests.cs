using Direct2dCad.ChangeTracking;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering.Direct2D.Entities;
using Direct2dCad.Rendering.Direct2D.Transient;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class Direct2DIncrementalResourceTests
{
    [Fact]
    public void CompositePreviewReusesGeometryAcrossTransformsAndReleasesRemovedItems()
    {
        using var target = new ImageSourceDirect2DResource();
        using var styles = new Direct2DStyleResourceCache();
        using var text = new Direct2DTextFormatResourceCache();
        using var resources = new Direct2DResourceCache(styles, text, new Direct2DRenderStatisticsCollector(),
            target.Factory, target.DwriteFactory, target.Context);
        using var cache = new Direct2DTransientPathCache(resources, new Direct2DGeometryFactory());
        var document = CadDocument.Create("Preview cache");
        var entity = document.AddCompositePath(new(0, 0),
            [new CadCompositeLineSegment(new(10, 0)), new CadCompositeArcSegment(new(10, 10), Math.PI / 2)]);
        var item = new CadTransientCompositePath(entity.StartPoint, entity.Segments, false, entity.Bounds, new(CadColor.Red, 1));
        var scene = new CadTransientScene();
        scene.Replace([item]);
        Assert.Null(cache.Get(item));
        cache.Prepare(scene);
        var geometry = cache.Get(item);
        Assert.NotNull(geometry);
        scene.Replace([new CadTransientGroup([item], CadMatrixD.CreateScale(100, 100))]);
        cache.Prepare(scene);
        Assert.Same(geometry, cache.Get(item));
        var changed = item with { Segments = new CadCompositePathSegment[] { new CadCompositeLineSegment(new(30, 0)) } };
        scene.Replace([changed]);
        cache.Prepare(scene);
        Assert.Null(cache.Get(item));
        Assert.Equal(IntPtr.Zero, geometry.NativePointer);
        var replacement = cache.Get(changed);
        Assert.NotNull(replacement);
        cache.Clear();
        Assert.Equal(IntPtr.Zero, replacement.NativePointer);
        cache.Prepare(scene);
        Assert.NotSame(replacement, cache.Get(changed));
        scene.Clear();
        cache.Prepare(scene);
        Assert.Null(cache.Get(changed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SplineFillChangesDiscardPendingOrCachedLodAndUndoPreparesItAgain(bool finishPreparation)
    {
        using var target = new ImageSourceDirect2DResource();
        using var styles = new Direct2DStyleResourceCache();
        using var text = new Direct2DTextFormatResourceCache();
        styles.Reset(target.Factory, target.Context);
        text.Reset(target.DwriteFactory);
        using var cache = new Direct2DResourceCache(styles, text, new Direct2DRenderStatisticsCollector(),
            target.Factory, target.DwriteFactory, target.Context);
        var document = CadDocument.Create("Spline fill LOD");
        var spline = document.AddSpline(Enumerable.Range(0, 300).Select(index =>
            new CadPointD(100 * Math.Cos(index * Math.Tau / 300), 100 * Math.Sin(index * Math.Tau / 300))), closed: true);
        var fill = document.CreateSolidFillStyle("Solid", CadColor.Red);
        cache.RebuildAll(document);
        Assert.True(cache.TryGetEntityResources(spline.Id, out var bucket));
        var fullGeometry = bucket!.Geometry;
        Assert.True(cache.PrepareLevelOfDetailGeometries(document, true, true, out _));
        if (finishPreparation)
        {
            DrainLod();
            Assert.NotNull(bucket.MediumDetailGeometry);
        }

        var setFill = new SetEntityFillStyleCommand([spline.Id], fill);
        cache.ApplyChanges(document, setFill.Execute(document));
        DrainLod();
        Assert.True(bucket.AreLevelOfDetailGeometriesInitialized);
        Assert.Null(bucket.MediumDetailGeometry);
        Assert.Null(bucket.LowDetailGeometry);
        Assert.Same(fullGeometry, bucket.Geometry);

        cache.ApplyChanges(document, setFill.Undo(document));
        DrainLod();
        Assert.NotNull(bucket.MediumDetailGeometry);
        Assert.Same(fullGeometry, bucket.Geometry);

        void DrainLod() => Assert.True(SpinWait.SpinUntil(
            () => !cache.PrepareLevelOfDetailGeometries(document, true, true, out _), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void AppearanceMetadataAndOrderKeepGeometryButFrozenLayerReleasesIt()
    {
        using var target = new ImageSourceDirect2DResource();
        using var styles = new Direct2DStyleResourceCache();
        using var text = new Direct2DTextFormatResourceCache();
        styles.Reset(target.Factory, target.Context);
        text.Reset(target.DwriteFactory);
        using var cache = new Direct2DResourceCache(styles, text, new Direct2DRenderStatisticsCollector(),
            target.Factory, target.DwriteFactory, target.Context);
        var document = CadDocument.Create("Incremental resources");
        var entity = document.AddPolyline([new(0, 0), new(10, 0), new(10, 10)]);
        cache.RebuildAll(document);
        Assert.True(cache.TryGetEntityResources(entity.Id, out var bucket));
        var geometry = bucket!.Geometry;
        Assert.NotNull(geometry);

        ICadCommand[] commands = [
            new RenameLayerCommand(LayerId.Default, "Renamed"),
            new SetEntityColorCommand([entity.Id], CadColor.Red),
            new SetLayerAppearanceCommand(LayerId.Default, CadColor.Blue, new CadLineWeight(3)),
            new SetLayerDrawingPrioritiesCommand(new Dictionary<LayerId, int> { [LayerId.Default] = 7 }),
            new SetLayerStateCommand(LayerId.Default, true, true, false)];
        foreach (var command in commands)
        {
            cache.ApplyChanges(document, command.Execute(document));
            Assert.True(cache.TryGetEntityResources(entity.Id, out bucket));
            Assert.Same(geometry, bucket!.Geometry);
            cache.ApplyChanges(document, command.Undo(document));
            Assert.True(cache.TryGetEntityResources(entity.Id, out bucket));
            Assert.Same(geometry, bucket!.Geometry);
        }

        var freeze = new SetLayerStateCommand(LayerId.Default, true, false, true);
        cache.ApplyChanges(document, freeze.Execute(document));
        Assert.False(cache.TryGetEntityResources(entity.Id, out _));
        cache.ApplyChanges(document, freeze.Undo(document));
        Assert.True(cache.TryGetEntityResources(entity.Id, out bucket));
        Assert.NotSame(geometry, bucket!.Geometry);
    }

    [Fact]
    public void LateBackgroundResultsDoNotOverwriteEditedOrDeletedResources()
    {
        using var target = new ImageSourceDirect2DResource();
        using var styles = new Direct2DStyleResourceCache();
        using var text = new Direct2DTextFormatResourceCache();
        styles.Reset(target.Factory, target.Context);
        text.Reset(target.DwriteFactory);
        using var cache = new Direct2DResourceCache(styles, text, new Direct2DRenderStatisticsCollector());
        var document = CadDocument.Create("Late results");
        var edited = document.AddPolyline([new(0, 0), new(10, 0)]);
        var deleted = document.AddPolyline([new(0, 0), new(20, 0)]);
        var unchanged = document.AddPolyline([new(0, 0), new(30, 0)]);
        cache.ResetDeviceResources(target.Factory, target.DwriteFactory, target.Context, document);

        edited.ReplacePoints([new(0, 0), new(100, 0)]);
        cache.ApplyChanges(document, CadDocumentChangeSet.ForEntity(edited.Id, CadEntityChangeKind.Geometry));
        cache.ApplyChanges(document, new DeleteEntitiesCommand([deleted.Id]).Execute(document));
        Assert.True(cache.TryGetEntityResources(edited.Id, out var bucket));
        var currentGeometry = bucket!.Geometry;
        Assert.True(SpinWait.SpinUntil(() => !cache.ApplyBackgroundGeometryPreparation(document, 1), TimeSpan.FromSeconds(10)));

        Assert.True(cache.TryGetEntityResources(edited.Id, out bucket));
        Assert.Same(currentGeometry, bucket!.Geometry);
        Assert.Equal(100, bucket.Geometry!.ComputeLength(), 3);
        Assert.False(cache.TryGetEntityResources(deleted.Id, out _));
        Assert.True(cache.TryGetEntityResources(unchanged.Id, out bucket));
        Assert.Equal(30, bucket!.Geometry!.ComputeLength(), 3);
    }
}
