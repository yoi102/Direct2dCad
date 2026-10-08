using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection("Native zoom pixel comparisons")]
public sealed class Direct2DPrecisionCacheRegressionTests
{
    [Fact]
    public void UnreferencedFarDefinitionKeepsNormalOwnerSceneAndChunkCachesEnabled()
    {
        using var fixture = new CacheFixture();
        var document = CadDocument.Create("Unreferenced far definition");
        for (var i = 0; i < 1200; i++)
            document.AddLine(new(i % 40, i / 40), new(i % 40 + .5, i / 40));
        var farDefinition = document.CreateBlockDefinition("Unused", new(1_000_000, 1_000_000));
        var far = document.AddLine(new(1_000_000, 1_000_000), new(1_000_000.01, 1_000_000));
        document.MoveEntityToBlock(far.Id, farDefinition);
        fixture.Resources.RebuildAll(document);
        Assert.True(fixture.Resources.HasLocalGeometry);
        var viewport = Viewport();
        var options = Options();
        using var order = new Direct2DEntityOrderCache();
        using var chunks = new Direct2DCommandListChunkCache(fixture.Resources, order, fixture.Statistics);
        using var tiles = new Direct2DSceneTileCache(fixture.Resources, fixture.Statistics);
        for (var step = 0; step < 300; step++)
        {
            var pendingChunks = chunks.Prepare(fixture.Target.Context!, document, viewport, options,
                order.GetOrderedEntities(document, BlockId.ModelSpace), 1200,
                static (_, _, _, _, _) => { }, true);
            var pendingTiles = tiles.Prepare(fixture.Target.Context!, document, viewport, options, 1200,
                static (_, _, _, _) => true, true);
            if (!pendingChunks && !pendingTiles) break;
        }
        Assert.True(chunks.EstimatedBytes > 0);
        Assert.True(tiles.EstimatedBytes > 0);
        Assert.True(chunks.CanReplayCompletely(document, viewport, options));
        Assert.True(tiles.CanDrawCompletely(viewport, options));
    }

    [Fact]
    public void UnreferencedFarDefinitionKeepsNormalBlockDefinitionCacheEnabled()
    {
        var document = CadDocument.Create("Mixed block definitions");
        var nearDefinition = document.CreateBlockDefinition("Near", CadPointD.Origin);
        var nearLine = document.AddLine(new(-4, 0), new(4, 0));
        document.MoveEntityToBlock(nearLine.Id, nearDefinition);
        document.AddBlockReference(nearDefinition, new(-15, 0));
        document.AddBlockReference(nearDefinition, new(15, 0));
        var farDefinition = document.CreateBlockDefinition("Unused", new(1_000_000, 1_000_000));
        var farLine = document.AddLine(new(1_000_000, 1_000_000), new(1_000_000.01, 1_000_000));
        document.MoveEntityToBlock(farLine.Id, farDefinition);
        using var host = Host(document, Viewport(), new CadRenderResourceBudget());
        Prepare(host);
        host.Render(CadRenderInvalidation.Full);
        Assert.True(host.RenderStatistics.BlockDefinitionCacheBytes > 0);
        Assert.True(host.RenderStatistics.BlockDefinitionCommandListReplayCount >= 2);
    }

    [Fact]
    public void PrecisionGateFollowsVisibleOwnersNestedReferencesAndGeometryChanges()
    {
        using var fixture = new CacheFixture();
        var document = CadDocument.Create("Scoped precision");
        var farDefinition = document.CreateBlockDefinition("Far", new(1_000_000, 1_000_000));
        var far = document.AddLine(new(1_000_000, 1_000_000), new(1_000_000.01, 1_000_000));
        document.MoveEntityToBlock(far.Id, farDefinition);
        var parent = document.CreateBlockDefinition("Nested", CadPointD.Origin);
        document.AddBlockReference(farDefinition, CadPointD.Origin, ownerBlockId: parent);
        var offscreen = document.AddBlockReference(parent, new(5000, 0));
        document.AddBlockReference(parent, CadPointD.Origin, ownerBlockId: BlockId.PaperSpace);
        fixture.Resources.RebuildAll(document);
        var viewport = Viewport();
        var model = Options();
        var paper = Options(BlockId.PaperSpace);
        Assert.True(fixture.Resources.RequiresPrecision(document, parent));
        Assert.False(fixture.Resources.RequiresPrecision(document, viewport, model));
        Assert.True(fixture.Resources.RequiresPrecision(document, viewport, paper));
        offscreen.SetPosition(CadPointD.Origin);
        document.RefreshBlockReferenceBounds();
        fixture.Resources.ApplyChanges(document, CadDocumentChangeSet.ForEntity(offscreen.Id, CadEntityChangeKind.Geometry));
        Assert.True(fixture.Resources.RequiresPrecision(document, viewport, model));
        far.SetGeometry(CadPointD.Origin, new(.01, 0));
        document.RefreshBlockReferenceBounds();
        fixture.Resources.ApplyChanges(document, CadDocumentChangeSet.ForEntity(far.Id, CadEntityChangeKind.Geometry));
        Assert.False(fixture.Resources.RequiresPrecision(document, farDefinition));
        Assert.False(fixture.Resources.RequiresPrecision(document, viewport, model));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReferencedFarDefinitionsKeepOriginPixelsInModelAndPaperOwners(bool nested, bool paper)
    {
        byte[] Render(double coordinate)
        {
            var document = CadDocument.Create("Visible rebased definition");
            document.GetLayer(LayerId.Default).SetColor(CadColor.Red);
            var definition = document.CreateBlockDefinition("Source", new(coordinate, coordinate));
            var line = document.AddLine(new(coordinate, coordinate), new(coordinate + .01, coordinate));
            line.SetLineWeight(new(2));
            document.MoveEntityToBlock(line.Id, definition);
            if (nested)
            {
                var parent = document.CreateBlockDefinition("Parent", CadPointD.Origin);
                document.AddBlockReference(definition, CadPointD.Origin, ownerBlockId: parent);
                definition = parent;
            }
            var owner = paper ? BlockId.PaperSpace : BlockId.ModelSpace;
            document.AddBlockReference(definition, new(-.02, 0), ownerBlockId: owner);
            document.AddBlockReference(definition, new(.02, 0), ownerBlockId: owner);
            var viewport = Viewport();
            viewport.SetView(1000, new(48, 48));
            using var host = Host(document, viewport, new CadRenderResourceBudget());
            host.SetRenderOptions(Options(owner));
            Prepare(host);
            host.Render(CadRenderInvalidation.Full);
            if (coordinate != 0) Assert.Equal(0, host.RenderStatistics.BlockDefinitionCacheBytes);
            return host.CaptureBackBufferPixels();
        }
        var near = Render(0);
        Assert.Contains(near.Where((_, i) => i % 4 == 2), value => value > 0);
        Assert.Equal(near, Render(1_000_000));
    }

    [Fact]
    public void DirectRebuildAndRemovalRefreshPrecisionAndHiddenLayersKeepTheirCandidates()
    {
        using var fixture = new CacheFixture();
        var document = CadDocument.Create("Direct resource updates");
        var definition = document.CreateBlockDefinition("Definition", CadPointD.Origin);
        var line = document.AddLine(CadPointD.Origin, new(.01, 0));
        document.MoveEntityToBlock(line.Id, definition);
        document.AddBlockReference(definition, CadPointD.Origin);
        fixture.Resources.RebuildAll(document);
        var viewport = Viewport();
        var options = Options();
        Assert.False(fixture.Resources.RequiresPrecision(document, viewport, options));
        line.SetGeometry(new(1_000_000, 1_000_000), new(1_000_000.01, 1_000_000));
        document.GetBlock(definition).SetBasePoint(new(1_000_000, 1_000_000));
        document.RefreshBlockReferenceBounds();
        fixture.Resources.RebuildEntityResources(document, line.Id);
        document.GetLayer(LayerId.Default).SetVisible(false);
        Assert.False(fixture.Resources.RequiresPrecision(document, viewport, options));
        document.GetLayer(LayerId.Default).SetVisible(true);
        Assert.True(fixture.Resources.RequiresPrecision(document, viewport, options));
        document.RemoveEntity(line.Id);
        document.RefreshBlockReferenceBounds();
        fixture.Resources.RemoveEntity(line.Id);
        Assert.False(fixture.Resources.RequiresPrecision(document, definition));
        Assert.False(fixture.Resources.RequiresPrecision(document, viewport, options));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1_000_000, 0)]
    [InlineData(100_000_000, .4)]
    public void ImageBudgetEvictionPreservesPixelsAfterRepeatedFramesMoveAndPayloadReplacement(double coordinate, double rotation)
    {
        var document = CadDocument.Create("Rebased image eviction");
        var image = document.AddImage(CadRectD.FromXYWH(coordinate - 20, coordinate - 20, 40, 40),
            64, 64, 256, ImagePixels(0), rotationRadians: rotation);
        var viewport = Viewport(coordinate);
        using var reference = Host(document, viewport, new CadRenderResourceBudget());
        using var constrained = Host(document, viewport, new CadRenderResourceBudget(4096, 4096));
        VerifyFrames();
        image.SetBounds(image.FrameBounds.Translate(new CadVectorD(5, -3)));
        Apply(CadEntityChangeKind.Geometry);
        VerifyFrames();
        image.SetImageData(64, 64, 256, ImagePixels(53));
        Apply(CadEntityChangeKind.EmbeddedData);
        VerifyFrames();

        void Apply(CadEntityChangeKind kind)
        {
            var changes = CadDocumentChangeSet.ForEntity(image.Id, kind);
            reference.ApplyChanges(document, changes);
            constrained.ApplyChanges(document, changes);
        }
        void VerifyFrames()
        {
            reference.Render(CadRenderInvalidation.Full);
            var expected = reference.CaptureBackBufferPixels();
            Assert.Contains(expected.Where((_, i) => i % 4 != 3), value => value > 0);
            for (var frame = 0; frame < 3; frame++)
            {
                constrained.Render(CadRenderInvalidation.Full);
                Assert.Equal(expected, constrained.CaptureBackBufferPixels());
                Assert.True(constrained.RenderStatistics.GpuCacheEvictionCount > 0);
                Assert.InRange(constrained.ResourceStatistics.EstimatedRetainedCacheBytes, 0, 4096);
            }
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MovingFarSourceToUnusedDefinitionRestoresCachesThroughPublicResourceUpdates(bool directRebuild, bool nested)
    {
        var document = CadDocument.Create("Owner transfer precision recovery");
        for (var i = 0; i < 1200; i++)
            document.AddLine(new(i % 40, i / 40), new(i % 40 + .5, i / 40));
        var definition = document.CreateBlockDefinition("Referenced", CadPointD.Origin);
        var near = document.AddLine(CadPointD.Origin, new(10, 0));
        var far = document.AddLine(new(1_000_000, 1_000_000), new(1_000_000.01, 1_000_000));
        document.MoveEntityToBlock(near.Id, definition);
        document.MoveEntityToBlock(far.Id, definition);
        var referenced = definition;
        if (nested)
        {
            referenced = document.CreateBlockDefinition("Parent", CadPointD.Origin);
            document.AddBlockReference(definition, CadPointD.Origin, ownerBlockId: referenced);
        }
        document.AddBlockReference(referenced, CadPointD.Origin);
        var unused = document.CreateBlockDefinition("Unused", CadPointD.Origin);
        using var host = Host(document, Viewport(), new CadRenderResourceBudget());
        Prepare(host);
        host.Render(CadRenderInvalidation.Full);
        Assert.Equal(0, host.RenderStatistics.SceneTileCacheBytes);
        document.MoveEntityToBlock(far.Id, unused);
        document.RefreshBlockReferenceBounds();
        if (directRebuild) host.RebuildEntity(document, far.Id);
        else host.ApplyChanges(document, CadDocumentChangeSet.ForEntity(far.Id, CadEntityChangeKind.Geometry));
        Prepare(host);
        host.Render(CadRenderInvalidation.Full);
        Assert.True(host.RenderStatistics.SceneTileCacheBytes > 0);
        Assert.True(host.RenderStatistics.TileReplayCount > 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(256)]
    [InlineData(257)]
    public void SparseOffscreenPrecisionCandidatesAvoidQueryingTheNormalVisibleSet(int dangerousCount)
    {
        using var fixture = new CacheFixture();
        var document = CadDocument.Create("Sparse precision candidates");
        var normal = document.AddLine(CadPointD.Origin, new(10, 0));
        for (var i = 0; i < dangerousCount; i++)
            document.AddLine(new(1_000_000 + i, 1_000_000), new(1_000_000 + i + .01, 1_000_000));
        fixture.Resources.RebuildAll(document);
        var queries = 0;
        var options = new CadRenderOptions
        {
            DrawGrid = false, DrawOrigin = false,
            EntityBoundsQueryInto = (_, _, ids) =>
            {
                Assert.True(dangerousCount > 256, "A sparse precision set must inspect its own bounds.");
                queries++;
                ids.Add(normal.Id);
            }
        };
        Assert.False(fixture.Resources.RequiresPrecision(document, Viewport(), options));
        Assert.False(fixture.Resources.RequiresPrecision(document, Viewport(), options));
        normal.SetGeometry(new(1, 0), new(11, 0));
        fixture.Resources.ApplyChanges(document, CadDocumentChangeSet.ForEntity(normal.Id, CadEntityChangeKind.Geometry));
        Assert.False(fixture.Resources.RequiresPrecision(document, Viewport(), options));
        Assert.Equal(dangerousCount <= 256 ? 0 : 3, queries);
    }

    private static byte[] ImagePixels(int shift)
    {
        var pixels = new byte[64 * 64 * 4];
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                var i = (y * 64 + x) * 4;
                pixels[i] = (byte)((x * 3 + shift) % 256);
                pixels[i + 1] = (byte)((y * 3 + shift) % 256);
                pixels[i + 2] = (byte)((x * 2 + y * 2 + shift) % 256);
                pixels[i + 3] = 255;
            }
        return pixels;
    }

    private static CadViewport Viewport(double coordinate = 0)
    {
        var viewport = new CadViewport();
        viewport.SetSize(96, 96);
        viewport.SetView(1, new(48 - coordinate, 48 + coordinate));
        return viewport;
    }

    private static CadRenderOptions Options(BlockId? owner = null) => new()
    {
        ActiveOwnerBlockId = owner ?? BlockId.ModelSpace,
        DrawGrid = false, DrawOrigin = false, DrawGripHandles = false,
        IsAntialiasingEnabled = false, IsLevelOfDetailEnabled = false,
        EnableGeometryRealizations = false, IsBackgroundChunkRecordingEnabled = false
    };

    private static Direct2DImageRenderHost Host(CadDocument document, CadViewport viewport, CadRenderResourceBudget budget)
    {
        var host = new Direct2DImageRenderHost(null, budget);
        host.AttachImageSource(new ImageSource());
        host.SetSize(96, 96);
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        host.SetRenderOptions(Options());
        return host;
    }

    private static void Prepare(Direct2DImageRenderHost host)
    {
        var pending = true;
        for (var step = 0; step < 500 && pending; step++)
        {
            pending = host.PrepareRenderCacheStep();
            if (pending) Thread.Sleep(1);
        }
        Assert.False(pending);
    }

    private sealed class ImageSource : ID3D11ImageSource
    {
        public int SurfaceWidth => 96;
        public int SurfaceHeight => 96;
        public void SetSize(int width, int height) { }
        public void SetSurface(nint surface9Ptr) { }
        public void Present(Action presentAction, IReadOnlyList<IntRect>? dirtyRects = null) => presentAction();
        public void Invalidate() { }
        public void Invalidate(IntRect dirtyRect) { }
        public void Invalidate(IReadOnlyList<IntRect> dirtyRects) { }
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
            Resources = new(_styles, _text, Statistics, Target.Factory, Target.DwriteFactory, Target.Context);
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
