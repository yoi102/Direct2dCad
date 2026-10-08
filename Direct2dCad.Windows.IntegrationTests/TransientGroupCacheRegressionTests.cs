using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection("Native zoom pixel comparisons")]
public sealed class TransientGroupCacheRegressionTests
{
    private const int Width = 320, Height = 240;

    [Theory]
    [InlineData("image", false, false)]
    [InlineData("image", true, false)]
    [InlineData("text", false, false)]
    [InlineData("text", true, false)]
    [InlineData("rectangle", false, false)]
    [InlineData("rectangle", true, false)]
    [InlineData("image", false, true)]
    [InlineData("image", true, true)]
    [InlineData("text", false, true)]
    [InlineData("text", true, true)]
    [InlineData("rectangle", false, true)]
    [InlineData("rectangle", true, true)]
    public void HighZoomSmallSourcesMatchImmediateRendering(string shape, bool lod, bool transformed)
    {
        var (document, viewport, scene) = CreateSources(shape);
        if (transformed)
        {
            var group = Assert.IsType<CadTransientGroup>(Assert.Single(scene.Items));
            scene.Replace([group with { Transform = CadMatrixD.CreateScale(2, 0.5) }]);
        }
        using var immediate = CreateHost(document, viewport, scene, Options(lod: lod));
        immediate.Render(CadRenderInvalidation.Full);
        var expected = immediate.CaptureBackBufferPixels();
        Assert.True(ColoredPixels(expected) > 0);

        using var cached = CreateHost(document, viewport, scene, Options(lod: lod));
        PrepareAll(cached);
        cached.Render(CadRenderInvalidation.Full);
        Assert.True(cached.RenderStatistics.TransientGroupCacheBytes > 0);
        Assert.Equal(expected, cached.CaptureBackBufferPixels());
    }

    [Fact]
    public void LineWeightWorldScaleChangeRebuildsTheGroupContent()
    {
        var (document, viewport, scene) = CreateSources("line", zoom: 2);
        using var cached = CreateHost(document, viewport, scene, Options(worldScale: 1));
        PrepareAll(cached);
        cached.Render(CadRenderInvalidation.Full);
        var before = cached.CaptureBackBufferPixels();
        Assert.True(cached.RenderStatistics.TransientGroupCacheBytes > 0);

        cached.SetRenderOptions(Options(worldScale: 10));
        PrepareAll(cached);
        cached.Render(CadRenderInvalidation.Full);
        using var immediate = CreateHost(document, viewport, scene, Options(worldScale: 10));
        immediate.Render(CadRenderInvalidation.Full);
        var expected = immediate.CaptureBackBufferPixels();
        Assert.False(before.AsSpan().SequenceEqual(expected));
        Assert.True(cached.RenderStatistics.TransientGroupCacheBytes > 0);
        Assert.Equal(expected, cached.CaptureBackBufferPixels());
    }

    [Theory]
    [InlineData("image")]
    [InlineData("text")]
    public void RecordedScreenCoordinatesReplayAfterGroupTranslationAndViewportPan(string shape)
    {
        var (document, viewport, scene) = CreateSources(shape);
        using var cached = CreateHost(document, viewport, scene, Options(lod: true));
        PrepareAll(cached);
        cached.Render(CadRenderInvalidation.Full);
        Assert.True(cached.RenderStatistics.TransientGroupCacheBytes > 0);

        var group = Assert.IsType<CadTransientGroup>(Assert.Single(scene.Items));
        scene.Replace([group with { Transform = CadMatrixD.CreateTranslation(0.4, -0.8) }]);
        viewport.SetView(20, new(Width / 2 + 9, Height / 2 + 5));
        // Replay the retained commands immediately; this does not prepare a new list.
        cached.Render(CadRenderInvalidation.Full);
        using var immediate = CreateHost(document, viewport, scene, Options(lod: true));
        immediate.Render(CadRenderInvalidation.Full);
        Assert.True(cached.RenderStatistics.TransientGroupCacheBytes > 0);
        Assert.Equal(immediate.CaptureBackBufferPixels(), cached.CaptureBackBufferPixels());
    }

    [Theory]
    [InlineData("image", false, false)]
    [InlineData("image", true, false)]
    [InlineData("text", false, false)]
    [InlineData("text", true, false)]
    [InlineData("image", false, true)]
    [InlineData("image", true, true)]
    [InlineData("text", false, true)]
    [InlineData("text", true, true)]
    public void FractionalTranslationAndSmallZoomChangesMatchCurrentImmediatePixels(string shape, bool lod, bool zoomChanged)
    {
        var (document, viewport, scene) = CreateSources(shape);
        using var cached = CreateHost(document, viewport, scene, Options(lod: lod));
        PrepareAll(cached);
        cached.Render(CadRenderInvalidation.Full);
        var before = cached.CaptureBackBufferPixels();
        Assert.True(cached.RenderStatistics.TransientGroupCacheBytes > 0);

        if (zoomChanged)
        {
            // The outer sources move more than half a pixel at 20.2, while the
            // former quantized cache key still considers both zooms equivalent.
            const double nextZoom = 20.2;
            Assert.Equal(
                Direct2dCad.Rendering.Direct2D.Scene.Direct2DRenderScaleBucket.Quantize(viewport.Zoom),
                Direct2dCad.Rendering.Direct2D.Scene.Direct2DRenderScaleBucket.Quantize(nextZoom));
            viewport.SetView(nextZoom, new(Width / 2, Height / 2));
        }
        else
        {
            var group = Assert.IsType<CadTransientGroup>(Assert.Single(scene.Items));
            scene.Replace([group with { Transform = CadMatrixD.CreateTranslation(0.015, -0.0125) }]);
            viewport.SetView(20, new(Width / 2 + 0.35, Height / 2 + 0.65));
        }
        using var immediate = CreateHost(document, viewport, scene, Options(lod: lod));
        immediate.Render(CadRenderInvalidation.Full);
        var expected = immediate.CaptureBackBufferPixels();
        Assert.False(before.AsSpan().SequenceEqual(expected));

        // A changed profile must use current pixels even before preparation.
        cached.Render(CadRenderInvalidation.Full);
        Assert.Equal(expected, cached.CaptureBackBufferPixels());
        PrepareAll(cached);
        cached.Render(CadRenderInvalidation.Full);
        Assert.True(cached.RenderStatistics.TransientGroupCacheBytes > 0);
        Assert.Equal(expected, cached.CaptureBackBufferPixels());
    }

    [Theory]
    [InlineData("all", false)]
    [InlineData("entity", false)]
    [InlineData("all", true)]
    [InlineData("entity", true)]
    [InlineData("remove", false)]
    public void PublicResourceRebuildsNeverReplayPreviousTransientSourcePixels(string maintenance, bool moveFar)
    {
        var (document, viewport, scene) = CreateSources("image");
        using var cached = CreateHost(document, viewport, scene, Options(lod: true));
        PrepareAll(cached);
        cached.Render(CadRenderInvalidation.Full);
        var before = cached.CaptureBackBufferPixels();
        Assert.True(cached.RenderStatistics.TransientGroupCacheBytes > 0);

        var group = Assert.IsType<CadTransientGroup>(Assert.Single(scene.Items));
        var firstReference = Assert.IsType<CadTransientEntityReference>(group.Items[0]);
        Assert.True(document.TryGetEntity(firstReference.EntityId, out var firstEntity));
        var first = Assert.IsType<CadImage>(firstEntity);
        if (maintenance == "remove")
        {
            Assert.True(document.RemoveEntity(first.Id));
            cached.RemoveEntity(first.Id);
        }
        else
        {
            if (moveFar)
                first.SetBounds(first.FrameBounds.Translate(new(1_000_000, 1_000_000)));
            else
                first.SetImageData(1, 1, 4, [0, 255, 0, 255]);
            if (maintenance == "all") cached.RebuildAll(document);
            else cached.RebuildEntity(document, first.Id);
        }
        using var immediate = CreateHost(document, viewport, scene, Options(lod: true));
        immediate.Render(CadRenderInvalidation.Full);
        var expected = immediate.CaptureBackBufferPixels();
        Assert.False(before.AsSpan().SequenceEqual(expected));

        cached.Render(CadRenderInvalidation.Full);
        Assert.Equal(expected, cached.CaptureBackBufferPixels());
        PrepareAll(cached);
        cached.Render(CadRenderInvalidation.Full);
        Assert.Equal(expected, cached.CaptureBackBufferPixels());
        if (moveFar || maintenance == "remove")
            Assert.Equal(0, cached.RenderStatistics.TransientGroupCacheBytes);
        else
            Assert.True(cached.RenderStatistics.TransientGroupCacheBytes > 0);
    }

    [Fact]
    public void UnrelatedFarGeometryDoesNotDisableANearGroupCache()
    {
        var (document, viewport, scene) = CreateSources("line", zoom: 2);
        using var near = CreateHost(document, viewport, scene, Options());
        PrepareAll(near);
        near.Render(CadRenderInvalidation.Full);
        var expected = near.CaptureBackBufferPixels();
        Assert.True(near.RenderStatistics.TransientGroupCacheBytes > 0);

        document.AddLine(new(1_000_000, 1_000_000), new(1_000_003, 1_000_003));
        var unrelatedBlock = document.CreateBlockDefinition("Far unused definition", default);
        var unrelatedLine = document.AddLine(new(2_000_000, 2_000_000), new(2_000_003, 2_000_003));
        document.MoveEntityToBlock(unrelatedLine.Id, unrelatedBlock);
        using var cached = CreateHost(document, viewport, scene, Options());
        PrepareAll(cached);
        cached.Render(CadRenderInvalidation.Full);
        Assert.True(cached.RenderStatistics.TransientGroupCacheBytes > 0);
        Assert.Equal(expected, cached.CaptureBackBufferPixels());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FarSourceCoordinatesAndOffsetsKeepThePreciseImmediatePath(bool farSource)
    {
        var (document, viewport, scene) = CreateSources("image", origin: farSource ? 1_000_000 : 0);
        var group = Assert.IsType<CadTransientGroup>(Assert.Single(scene.Items));
        IReadOnlyList<CadTransientItem> items = farSource
            ? group.Items
            : group.Items.Select(item => (CadTransientItem)(Assert.IsType<CadTransientEntityReference>(item)
                with { Offset = new(1_000_000, 1_000_000) })).ToArray();
        scene.Replace([group with { Items = items, Transform = CadMatrixD.CreateTranslation(-1_000_000, -1_000_000) }]);
        using var immediate = CreateHost(document, viewport, scene, Options(lod: true));
        immediate.Render(CadRenderInvalidation.Full);
        var expected = immediate.CaptureBackBufferPixels();
        Assert.True(ColoredPixels(expected) > 0);

        using var prepared = CreateHost(document, viewport, scene, Options(lod: true));
        PrepareAll(prepared);
        prepared.Render(CadRenderInvalidation.Full);
        Assert.Equal(0, prepared.RenderStatistics.TransientGroupCacheBytes);
        Assert.Equal(expected, prepared.CaptureBackBufferPixels());
    }

    [Fact]
    public void ZoomAcrossALodThresholdWithinOneScaleBucketRebuildsTheGroup()
    {
        var (document, viewport, scene) = CreateSources("image", imageSize: 0.025, zoom: 19.99);
        using var cached = CreateHost(document, viewport, scene, Options(lod: true));
        PrepareAll(cached);
        cached.Render(CadRenderInvalidation.Full);
        var before = cached.CaptureBackBufferPixels();
        Assert.True(cached.RenderStatistics.TransientGroupCacheBytes > 0);

        viewport.SetView(20.01, new(Width / 2, Height / 2));
        PrepareAll(cached);
        cached.Render(CadRenderInvalidation.Full);
        using var immediate = CreateHost(document, viewport, scene, Options(lod: true));
        immediate.Render(CadRenderInvalidation.Full);
        var expected = immediate.CaptureBackBufferPixels();
        Assert.False(before.AsSpan().SequenceEqual(expected));
        Assert.True(cached.RenderStatistics.TransientGroupCacheBytes > 0);
        Assert.Equal(expected, cached.CaptureBackBufferPixels());
    }

    [Fact]
    public void ChangingGroupScaleRebuildsLodContentWhileTranslationRemainsReusable()
    {
        var (document, viewport, scene) = CreateSources("image");
        var group = Assert.IsType<CadTransientGroup>(Assert.Single(scene.Items));
        scene.Replace([group with { Transform = CadMatrixD.CreateScale(0.1, 0.1) }]);
        using var cached = CreateHost(document, viewport, scene, Options(lod: true));
        PrepareAll(cached);
        cached.Render(CadRenderInvalidation.Full);
        var before = cached.CaptureBackBufferPixels();

        scene.Replace([group with { Transform = CadMatrixD.CreateScale(0.2, 0.2) * CadMatrixD.CreateTranslation(1, -1) }]);
        PrepareAll(cached);
        cached.Render(CadRenderInvalidation.Full);
        using var immediate = CreateHost(document, viewport, scene, Options(lod: true));
        immediate.Render(CadRenderInvalidation.Full);
        var expected = immediate.CaptureBackBufferPixels();
        Assert.False(before.AsSpan().SequenceEqual(expected));
        Assert.True(cached.RenderStatistics.TransientGroupCacheBytes > 0);
        Assert.Equal(expected, cached.CaptureBackBufferPixels());
    }

    [Fact]
    public void GroupsBelow256ReferencesRemainImmediate()
    {
        var (document, viewport, scene) = CreateSources("image", count: 255);
        using var immediate = CreateHost(document, viewport, scene, Options(lod: true));
        immediate.Render(CadRenderInvalidation.Full);
        using var prepared = CreateHost(document, viewport, scene, Options(lod: true));
        PrepareAll(prepared);
        prepared.Render(CadRenderInvalidation.Full);
        Assert.Equal(0, prepared.RenderStatistics.TransientGroupCacheBytes);
        Assert.Equal(immediate.CaptureBackBufferPixels(), prepared.CaptureBackBufferPixels());
    }

    private static (CadDocument Document, CadViewport Viewport, CadTransientScene Scene) CreateSources(
        string shape, double imageSize = 0.2, double zoom = 20, double origin = 0, int count = 256)
    {
        var document = CadDocument.Create("Transient cache regression");
        document.GetLayer(LayerId.Default).SetColor(CadColor.Red);
        var sourceBlock = document.CreateBlockDefinition("Preview sources", default);
        var fill = document.CreateSolidFillStyle("Small filled geometry", CadColor.Red);
        var references = new List<CadTransientItem>(count);
        for (var index = 0; index < count; index++)
        {
            var x = origin - 3 + index % 16 * 0.4;
            var y = origin - 3 + index / 16 * 0.4;
            CadEntity entity = shape switch
            {
                "image" => document.AddImage(CadRectD.FromXYWH(x, y, imageSize, imageSize),
                    1, 1, 4, [0, 0, 255, 255]),
                "text" => document.AddText("E", new(x, y), 0.2),
                "rectangle" => document.AddRectangle(CadRectD.FromXYWH(x, y, 0.2, 0.2), fillStyleId: fill),
                _ => document.AddLine(new((x - origin) * 10, (y - origin) * 10),
                    new((x - origin) * 10 + 1.5, (y - origin) * 10))
            };
            entity.SetLineWeight(new(0.5));
            document.MoveEntityToBlock(entity.Id, sourceBlock);
            references.Add(new CadTransientEntityReference(entity.Id, default, CadTransientStyle.PastePreview,
                UseSourceAppearance: true));
        }
        var viewport = new CadViewport();
        viewport.SetSize(Width, Height);
        viewport.SetView(zoom, new(Width / 2, Height / 2));
        var scene = new CadTransientScene();
        scene.Replace([new CadTransientGroup(references, CadMatrixD.Identity)]);
        return (document, viewport, scene);
    }

    private static CadRenderOptions Options(double worldScale = 1, bool lod = false) => new()
    {
        DrawGrid = false,
        DrawOrigin = false,
        DrawGripHandles = false,
        IsAntialiasingEnabled = false,
        IsTextAntialiasingEnabled = false,
        EnableGeometryRealizations = false,
        IsLevelOfDetailEnabled = lod,
        KeepStrokeWidthScreenConstant = false,
        EntityLineWeightWorldScale = worldScale
    };

    private static Direct2DImageRenderHost CreateHost(CadDocument document, CadViewport viewport,
        CadTransientScene scene, CadRenderOptions options)
    {
        var host = new Direct2DImageRenderHost();
        host.AttachImageSource(new ImageSource());
        host.SetSize(Width, Height);
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        host.SetTransientScene(scene);
        host.SetRenderOptions(options);
        return host;
    }

    private static void PrepareAll(Direct2DImageRenderHost host)
    {
        var pending = true;
        for (var step = 0; step < 500 && pending; step++)
        {
            pending = host.PrepareRenderCacheStep();
            if (pending) Thread.Sleep(1);
        }
        Assert.False(pending);
    }

    private static int ColoredPixels(byte[] pixels)
    {
        var count = 0;
        for (var index = 0; index < pixels.Length; index += 4)
            if (pixels[index + 2] > pixels[index] + 20 && pixels[index + 2] > pixels[index + 1] + 20)
                count++;
        return count;
    }

    private sealed class ImageSource : ID3D11ImageSource
    {
        public int SurfaceWidth => Width;
        public int SurfaceHeight => Height;
        public void SetSize(int width, int height) { }
        public void SetSurface(nint surface9Ptr) { }
        public void Present(Action presentAction, IReadOnlyList<IntRect>? dirtyRects = null) => presentAction();
        public void Invalidate() { }
        public void Invalidate(IntRect dirtyRect) { }
        public void Invalidate(IReadOnlyList<IntRect> dirtyRects) { }
    }
}
