using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection("Native zoom pixel comparisons")]
public sealed class InlineMoveVisibilityIntegrationTests
{
    private const int Width = 160, Height = 120;

    [Theory]
    [InlineData(false, false, 0, 12)]
    [InlineData(false, true, 400, -400)]
    [InlineData(false, false, 0, 400)]
    [InlineData(true, false, 0, 12)]
    [InlineData(true, true, 400, -400)]
    [InlineData(true, false, 0, 400)]
    public void SpatialMoveCandidatesPreserveSourceOrderTranslatedSelectionAndDirtyPixels(
        bool block, bool sourceOnTop, double sourceX, double offsetX)
    {
        var scene = CreateScene(block, sourceOnTop, sourceX, offscreenCount: 0);
        var queries = 0;
        using var indexed = CreateHost(scene, (_, bounds, destination) =>
        {
            queries++;
            // Deliberately return duplicates and reverse z order, including a block
            // child that does not belong to the active owner.
            foreach (var entity in scene.QueryEntities.Reverse())
            {
                if (!bounds.Intersects(entity.Bounds))
                    continue;
                destination.Add(entity.Id);
                destination.Add(entity.Id);
            }
        });
        using var exhaustive = CreateHost(scene);
        SetMove(scene, offsetX);
        indexed.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        exhaustive.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        var pixels = indexed.CaptureBackBufferPixels();
        Assert.Equal(exhaustive.CaptureBackBufferPixels(), pixels);
        var centerPixel = ((Height / 2) * Width + Width / 2) * 4;
        Assert.True(sourceOnTop
            ? pixels[centerPixel + 2] > 180 && pixels[centerPixel] < 80
            : pixels[centerPixel] > 180 && pixels[centerPixel + 2] < 80,
            "The overlap must retain the source layer and z order.");
        Assert.True(queries > 0, "Inline replacement drawing must use the spatial query.");
        Assert.True(indexed.RenderStatistics.CpuEntitySubmissionMilliseconds > 0,
            "Move visibility and drawing must contribute to CPU submission timing.");
        Assert.Equal(exhaustive.RenderStatistics.EntitySubmissionCount, indexed.RenderStatistics.EntitySubmissionCount);
        Assert.True(indexed.RenderStatistics.SelectionEntityCount > 0,
            "A selection whose source is outside the query must still enter the view.");

        var beforeQueries = queries;
        SetMove(scene, offsetX - 12);
        indexed.Render(CadRenderInvalidation.FromScreenRect(new(40, 25, 80, 70)), baseSceneChanged: true);
        exhaustive.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        Assert.False(indexed.RenderStatistics.IsFullFrame);
        Assert.True(queries > beforeQueries);
        Assert.Equal(exhaustive.CaptureBackBufferPixels(), indexed.CaptureBackBufferPixels());
    }

    [Fact]
    public void MovingOneEntityInLargeOffscreenOwnerUsesCandidatesAndKeepsPixels()
    {
        var small = CreateScene(block: false, sourceOnTop: false, sourceX: 400, offscreenCount: 0);
        var large = CreateScene(block: false, sourceOnTop: false, sourceX: 400, offscreenCount: 20_000);
        var queries = 0;
        using var smallHost = CreateHost(small);
        using var largeHost = CreateHost(large, (_, bounds, destination) =>
        {
            queries++;
            foreach (var entity in large.QueryEntities)
            {
                if (bounds.Intersects(entity.Bounds))
                    destination.Add(entity.Id);
            }
        });
        for (var frame = 0; frame < 4; frame++)
        {
            SetMove(small, -400 + frame);
            SetMove(large, -400 + frame);
            smallHost.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
            var beforeQueries = queries;
            largeHost.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
            Assert.True(queries > beforeQueries);
            Assert.True(largeHost.RenderStatistics.CpuEntitySubmissionMilliseconds > 0);
            Assert.Equal(smallHost.RenderStatistics.EntitySubmissionCount, largeHost.RenderStatistics.EntitySubmissionCount);
            Assert.Equal(smallHost.CaptureBackBufferPixels(), largeHost.CaptureBackBufferPixels());
        }
    }

    [Fact]
    public void UnbufferedSpatialQueryAlsoMergesTranslatedSelectionWithoutMovePreviews()
    {
        var scene = CreateScene(block: false, sourceOnTop: false, sourceX: 0, offscreenCount: 0);
        using var indexed = CreateHost(scene, query: (_, bounds) => scene.QueryEntities
            .Where(entity => bounds.Intersects(entity.Bounds))
            .Reverse().SelectMany(entity => new[] { entity.Id, entity.Id }).ToArray());
        using var exhaustive = CreateHost(scene);
        indexed.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        exhaustive.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        Assert.Equal(exhaustive.CaptureBackBufferPixels(), indexed.CaptureBackBufferPixels());
        Assert.True(indexed.RenderStatistics.SelectionEntityCount > 0);
        Assert.Equal(exhaustive.RenderStatistics.EntitySubmissionCount, indexed.RenderStatistics.EntitySubmissionCount);
    }

    private static TestScene CreateScene(bool block, bool sourceOnTop, double sourceX, int offscreenCount)
    {
        var document = CadDocument.Create("Inline move visibility");
        var red = document.CreateLayer("Moving red", CadColor.Red, new(2));
        var blue = document.CreateLayer("Occluding blue", CadColor.Blue, new(2));
        if (sourceOnTop)
            document.DocumentSettings.LayerDrawingPriority.SetPriority(red, 10);
        var sourceLine = document.AddLine(new(-12 + (block ? 0 : sourceX), 0),
            new(12 + (block ? 0 : sourceX), 0), red);
        CadEntity source = sourceLine;
        if (block)
        {
            var definition = document.CreateBlockDefinition("Moving block", default);
            document.MoveEntityToBlock(sourceLine.Id, definition);
            source = document.AddBlockReference(definition, new(sourceX, 0), red);
        }
        source.SetZIndex(-5);
        var occluder = document.AddLine(new(-5, 0), new(5, 0), blue);
        occluder.SetZIndex(10);
        var selected = document.AddLine(new(390, 15), new(410, 15));
        var handles = new CadHandleScene();
        handles.Replace([new CadSelectionEntityReference(selected.Id, selected.Bounds, new(-400, 0),
            new CadHandleStyle(CadColor.Green, CadColor.Transparent, Size: 0, StrokeWidth: 2))]);
        for (var index = 0; index < offscreenCount; index++)
            document.AddLine(new(20_000 + index, 0), new(20_000 + index, 20));
        return new(document, source, new CadTransientScene(), handles,
            [sourceLine, source, occluder, selected], new HashSet<EntityId> { source.Id });
    }

    private static void SetMove(TestScene scene, double offsetX)
    {
        var style = new CadTransientStyle(CadColor.Red, 2);
        CadTransientItem reference = scene.Source is CadBlockReference block
            ? new CadTransientBlockReference(block.DefinitionBlockId, block.Position, block.RotationRadians,
                block.ScaleX, block.ScaleY, block.LayerId, block.ColorSource, block.GraphicStyleId, style, block.Id)
            : new CadTransientEntityReference(scene.Source.Id, default, style, UseSourceAppearance: true);
        scene.Transients.Replace([new CadTransientGroup([reference], CadMatrixD.CreateTranslation(offsetX, 0))]);
    }

    private static Direct2DImageRenderHost CreateHost(TestScene scene,
        Action<BlockId, CadRectD, List<EntityId>>? bufferedQuery = null,
        Func<BlockId, CadRectD, IReadOnlyList<EntityId>>? query = null)
    {
        var viewport = new CadViewport();
        viewport.SetSize(Width, Height);
        viewport.SetView(1, new(Width / 2, Height / 2));
        var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new ImageSource());
        host.SetSize(Width, Height);
        host.SetRenderOptions(new()
        {
            DrawGrid = false, DrawOrigin = false, DrawGripHandles = false,
            IsAntialiasingEnabled = false, IsLevelOfDetailEnabled = false, EnableGeometryRealizations = false,
            HiddenEntityIds = scene.Hidden, EntityBoundsQueryInto = bufferedQuery, EntityBoundsQuery = query
        });
        host.SetScene(scene.Document, viewport, prepareResourcesInBackground: false);
        host.SetTransientScene(scene.Transients);
        host.SetHandleScene(scene.Handles);
        return host;
    }

    private sealed record TestScene(CadDocument Document, CadEntity Source, CadTransientScene Transients,
        CadHandleScene Handles, CadEntity[] QueryEntities, HashSet<EntityId> Hidden);

    private sealed class ImageSource : ID3D11ImageSource
    {
        public int SurfaceWidth { get; private set; } = Width;
        public int SurfaceHeight { get; private set; } = Height;
        public void SetSize(int width, int height) => (SurfaceWidth, SurfaceHeight) = (width, height);
        public void SetSurface(nint pointer) { }
        public void Present(Action action, IReadOnlyList<IntRect>? rects = null) => action();
        public void Invalidate() { }
        public void Invalidate(IntRect rect) { }
        public void Invalidate(IReadOnlyList<IntRect> rects) { }
    }
}
