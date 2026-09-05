using Direct2dCad.ChangeTracking;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Windows.IntegrationTests;

public sealed class Direct2DCacheUpdateTests
{
    [Fact]
    public void NewZoomProfileUsesCurrentBoundsAfterGeometryMovesIntoView()
    {
        using var fixture = new CacheFixture();
        var document = CadDocument.Create("Fresh profile bounds");
        var line = document.AddLine(new CadPointD(5000, 0), new CadPointD(5000, 10));
        var other = document.AddLine(new CadPointD(5001, 0), new CadPointD(5001, 10));
        CadEntity[] entities = [line, other];
        fixture.Resources.RebuildAll(document);
        using var order = new Direct2DEntityOrderCache();
        using var cache = new Direct2DCommandListChunkCache(fixture.Resources, order, fixture.Statistics);
        var viewport = Viewport();
        var options = new CadRenderOptions { IsBackgroundChunkRecordingEnabled = false, HiddenEntityIds = new HashSet<EntityId> { other.Id } };
        void Prepare()
        {
            for (var i = 0; i < 100; i++)
                if (!cache.Prepare(fixture.Target.Context!, document, viewport, options, entities, 1200,
                        static (_, _, _, _, _) => { }, true))
                    break;
        }
        Prepare();
        line.SetGeometry(CadPointD.Origin, new CadPointD(0, 10));
        var changes = CadDocumentChangeSet.ForEntity(line.Id, CadEntityChangeKind.Geometry);
        cache.ApplyChanges(document, changes);
        fixture.Resources.ApplyChanges(document, changes);
        viewport.SetView(2, viewport.Offset);
        Prepare();
        var drawn = new List<EntityId>();
        Assert.True(cache.TryDraw(fixture.Target.Context!, document, viewport, options, null,
            (_, _, entity, _, _) => drawn.Add(entity.Id), static (_, _, _, _, _) => { }));
        Assert.Equal([line.Id], drawn);
    }

    [Fact]
    public void CancelledRecorderDiscardsOldOutputAndCanRecordAgainBeforeResourceRelease()
    {
        using var fixture = new CacheFixture();
        var document = CadDocument.Create("Recorder cancellation");
        CadEntity[] entities = Enumerable.Range(0, 1200).Select(i =>
            document.AddLine(new CadPointD(i, 0), new CadPointD(i, 10))).ToArray();
        fixture.Resources.RebuildAll(document);
        using var worker = new Direct2DChunkRecordingWorker(fixture.Resources);
        worker.Reset(fixture.Target.Factory, fixture.Target.Device);
        var options = new CadRenderOptions { EnableGeometryRealizations = false };
        Assert.True(worker.TrySchedule(document, Viewport(), options, entities, out _));
        worker.CancelPending();
        worker.CancelAndWait();
        Assert.False(worker.TryTakeCompleted(out _));
        Assert.True(worker.LastCancellationWaitMilliseconds >= 0);
        Assert.True(worker.TrySchedule(document, Viewport(), options, entities, out var requestId));
        Direct2DChunkRecordingWorker.RecordingResult? result = null;
        Assert.True(SpinWait.SpinUntil(() => worker.TryTakeCompleted(out result!), TimeSpan.FromSeconds(10)));
        using (result)
        {
            Assert.Equal(requestId, result!.RequestId);
            Assert.False(result.IsFailed);
            Assert.False(result.IsCancelled);
            Assert.NotNull(result.CommandList);
            Assert.Equal(entities.Length, result.RecordedEntityCount);
        }
        worker.CancelAndWait();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChunkPlanChangesPreserveUnrelatedSpaceAndInvalidateReferencingOwners(bool nested)
    {
        using var fixture = new CacheFixture();
        var document = CadDocument.Create("Scoped chunk plans");
        var definition = nested ? document.CreateBlockDefinition("Nested", CadPointD.Origin) : BlockId.ModelSpace;
        var changed = document.AddLine(CadPointD.Origin, new CadPointD(10, 10));
        document.MoveEntityToBlock(changed.Id, definition);
        if (nested)
        {
            var parent = document.CreateBlockDefinition("Parent", CadPointD.Origin);
            document.AddBlockReference(definition, CadPointD.Origin, ownerBlockId: parent);
            document.AddBlockReference(parent, CadPointD.Origin);
        }
        var paperLine = document.AddLine(CadPointD.Origin, new CadPointD(20, 20));
        document.MoveEntityToBlock(paperLine.Id, BlockId.PaperSpace);
        fixture.Resources.RebuildAll(document);
        using var order = new Direct2DEntityOrderCache();
        using var cache = new Direct2DCommandListChunkCache(fixture.Resources, order, fixture.Statistics);
        var submissions = 0;
        void Prepare(BlockId owner)
        {
            var options = new CadRenderOptions { ActiveOwnerBlockId = owner, IsBackgroundChunkRecordingEnabled = false };
            for (var i = 0; i < 100; i++)
                if (!cache.Prepare(fixture.Target.Context!, document, Viewport(), options,
                        order.GetOrderedEntities(document, owner), 1200,
                        (_, _, _, _, _) => submissions++, true))
                    break;
        }
        Prepare(BlockId.ModelSpace);
        Prepare(BlockId.PaperSpace);
        Assert.True(cache.EstimatedBytes > 0);
        foreach (var kind in new[] { CadEntityChangeKind.DrawOrder, CadEntityChangeKind.Layer,
                     CadEntityChangeKind.Fill, CadEntityChangeKind.Created, CadEntityChangeKind.Deleted })
        {
            cache.ApplyChanges(document, CadDocumentChangeSet.ForEntity(changed.Id, kind));
            var before = submissions;
            Prepare(BlockId.PaperSpace);
            Assert.Equal(before, submissions);
            Prepare(BlockId.ModelSpace);
            Assert.True(submissions > before);
        }
    }

    [Fact]
    public void LayerChangesKeepGeometryAndTextLayoutButUpdateStrokeAndVisibility()
    {
        using var fixture = new CacheFixture();
        var document = CadDocument.Create("Layer resource reuse");
        var target = document.CreateLayer("Target", CadColor.Red, new CadLineWeight(3));
        var path = document.AddPolyline([CadPointD.Origin, new CadPointD(10, 10), new CadPointD(20, 0)]);
        var text = document.AddText("Layout reuse", CadPointD.Origin, 5);
        fixture.Resources.RebuildAll(document);
        var oldPath = fixture.Resources.EntityResources[path.Id];
        var oldText = fixture.Resources.EntityResources[text.Id];
        var geometry = oldPath.Geometry;
        var layout = oldText.TextLayout;
        Assert.NotNull(geometry);
        Assert.NotNull(layout);
        var command = new ChangeLayerCommand([path.Id, text.Id], target);
        fixture.Resources.ApplyChanges(document, command.Execute(document));
        Assert.Same(oldPath, fixture.Resources.EntityResources[path.Id]);
        Assert.Same(geometry, fixture.Resources.EntityResources[path.Id].Geometry);
        Assert.Same(layout, fixture.Resources.EntityResources[text.Id].TextLayout);
        Assert.Equal(CadColor.Red, oldPath.StrokeColor);
        Assert.Equal(3, oldPath.StrokeWidth);
        fixture.Resources.ApplyChanges(document, command.Undo(document));
        Assert.Same(geometry, fixture.Resources.EntityResources[path.Id].Geometry);
        Assert.Same(layout, fixture.Resources.EntityResources[text.Id].TextLayout);

        document.GetLayer(target).SetVisible(false);
        document.ChangeEntityLayer(path.Id, target);
        fixture.Resources.ApplyChanges(document, CadDocumentChangeSet.ForEntity(path.Id, CadEntityChangeKind.Layer));
        Assert.False(fixture.Resources.EntityResources.ContainsKey(path.Id));
        document.ChangeEntityLayer(path.Id, LayerId.Default);
        fixture.Resources.ApplyChanges(document, CadDocumentChangeSet.ForEntity(path.Id, CadEntityChangeKind.Layer));
        Assert.NotNull(fixture.Resources.EntityResources[path.Id].Geometry);
    }

    [Fact]
    public void ChunkBatchInvalidatesEachChunkOnceAndSubsequentBatchStillRebuilds()
    {
        using var fixture = new CacheFixture();
        var document = CadDocument.Create("Chunk deduplication");
        CadEntity[] entities = Enumerable.Range(0, 1200).Select(i =>
            document.AddLine(new CadPointD(i, 0), new CadPointD(i + 1, 1))).ToArray();
        fixture.Resources.RebuildAll(document);
        using var order = new Direct2DEntityOrderCache();
        using var cache = new Direct2DCommandListChunkCache(fixture.Resources, order, fixture.Statistics);
        var viewport = Viewport();
        var options = new CadRenderOptions { IsBackgroundChunkRecordingEnabled = false };
        for (var i = 0; i < 100; i++)
            if (!cache.Prepare(fixture.Target.Context!, document, viewport, options, entities, 1200,
                    static (_, _, _, _, _) => { }, true))
                break;
        Assert.True(cache.EstimatedBytes > 0);
        var batch = CadDocumentChangeSet.ForEntities(entities.Take(32).Select(e => e.Id), CadEntityChangeKind.Geometry);
        cache.ApplyChanges(document, batch);
        Assert.Equal(1, cache.LastInvalidatedChunkCount);
        cache.ApplyChanges(document, batch);
        Assert.Equal(1, cache.LastInvalidatedChunkCount);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(-20)]
    public void TileBatchInvalidatesOldAndNewLocationsWithoutDuplicates(double x)
    {
        using var fixture = new CacheFixture();
        var document = CadDocument.Create("Tile deduplication");
        var line = document.AddLine(new CadPointD(x, 10), new CadPointD(x + 2, 12));
        using var cache = new Direct2DSceneTileCache(fixture.Resources, fixture.Statistics);
        var viewport = Viewport();
        var options = new CadRenderOptions();
        var built = 0;
        bool Draw(Vortice.Direct2D1.ID2D1DeviceContext _, CadDocument __, CadViewport ___, CadRenderOptions ____)
        { built++; return true; }
        for (var i = 0; i < 100; i++)
            if (!cache.Prepare(fixture.Target.Context!, document, viewport, options, 20000, Draw, true))
                break;
        Assert.True(built > 1);
        var initialBytes = cache.EstimatedBytes;
        line.SetGeometry(new CadPointD(x + 520, 10), new CadPointD(x + 522, 12));
        var changes = new CadDocumentChangeSet(Enumerable.Repeat(
            new CadEntityChange(line.Id, CadEntityChangeKind.Geometry), 1000));
        cache.ApplyChanges(document, changes);
        Assert.InRange(cache.LastInvalidatedTileCount, 1, built);
        Assert.True(cache.EstimatedBytes < initialBytes);
        var invalidated = cache.LastInvalidatedTileCount;
        var before = built;
        for (var i = 0; i < 100; i++)
            if (!cache.Prepare(fixture.Target.Context!, document, viewport, options, 20000, Draw, true))
                break;
        Assert.Equal(invalidated, built - before);
        Assert.Equal(initialBytes, cache.EstimatedBytes);
    }

    private static CadViewport Viewport()
    {
        var viewport = new CadViewport();
        viewport.SetSize(1600, 1200);
        viewport.SetView(1, new CadPointD(500, 600));
        return viewport;
    }

    private sealed class CacheFixture : IDisposable
    {
        public ImageSourceDirect2DResource Target { get; } = new();
        private readonly Direct2DStyleResourceCache _styles = new();
        private readonly Direct2DTextFormatResourceCache _text = new();
        public Direct2DRenderStatisticsCollector Statistics { get; } = new();
        public Direct2DResourceCache Resources { get; }

        public CacheFixture()
        {
            _styles.Reset(Target.Factory, Target.Context);
            _text.Reset(Target.DwriteFactory);
            Resources = new Direct2DResourceCache(_styles, _text, Statistics,
                Target.Factory, Target.DwriteFactory, Target.Context);
        }

        public void Dispose()
        {
            Resources.Dispose();
            _text.Dispose();
            _styles.Dispose();
            Target.Dispose();
        }
    }
}
