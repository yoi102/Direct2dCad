using System.Numerics;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Entities;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;
using Direct2dCad.IO.Dxf;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Windows.IntegrationTests;

[Collection("Native zoom pixel comparisons")]
public sealed class VisiblePolylineStrokeIntegrationTests
{
    public static IEnumerable<object[]> StrokeCases()
    {
        foreach (var closed in new[] { false, true })
        foreach (var alpha in new[] { 1f, .4f })
        foreach (var zoom in new[] { 1d, 5d, 100d })
        foreach (var style in Enumerable.Range(0, 4))
            yield return [closed, alpha, zoom, style];
    }

    [Theory, MemberData(nameof(StrokeCases)), Trait("Category", "WindowsIntegration")]
    public void VisibleRunsMatchFullStrokeIncludingClosedSeamCapsJoinsAndAlpha(bool closed, float alpha, double zoom, int style)
    {
        const int width = 320, height = 240;
        using var target = new ImageSourceDirect2DResource();
        target.SetTarget(new ImageSource(width, height));
        var document = CadDocument.Create("Visible stroke");
        var points = new List<CadPointD> { new(0, 0), new(8, 8), new(20, -8), new(2000, -8) };
        points.AddRange(Enumerable.Range(0, 600).Select(i => new CadPointD(2000 + i, i % 2 * 50)));
        points.AddRange([new(2000, 30), new(-2000, 30), new(-20, 12), new(-8, -8)]);
        var path = document.AddPolyline(points, closed);
        using var original = new Direct2DGeometryFactory().CreatePolyline(target.Factory!, path.Points, path.Closed);
        var viewport = new CadViewport();
        viewport.SetSize(width, height);
        viewport.SetView(zoom, new(width / 2d + .3, height / 2d + .7));
        var strokeWidth = (float)((zoom == 100 ? 14 : 1.3) / zoom);
        Assert.True(Direct2DVisiblePolylineStroke.TryCreate(target.Factory!, path, viewport.VisibleWorldBounds,
            strokeWidth, zoom, out var visible));
        using (visible)
        {
            Assert.NotNull(visible);
            Assert.True(visible.SegmentCount < original.SegmentCount);
            using var brush = target.Context!.CreateSolidColorBrush(new Color4(1, .7f, .2f, alpha));
            using var stroke = target.Factory!.CreateStrokeStyle(new StrokeStyleProperties
            {
                StartCap = (CapStyle)style, EndCap = (CapStyle)style,
                LineJoin = (LineJoin)style, MiterLimit = 10, DashStyle = DashStyle.Solid
            });
            Draw(original);
            var expected = target.CaptureBackBufferPixels();
            Draw(visible!);
            Assert.Equal(expected, target.CaptureBackBufferPixels());

            void Draw(ID2D1Geometry geometry) => target.DrawFrame(context =>
            {
                context.Clear(new Color4(0, 0, 0, 1));
                context.Transform = Matrix3x2.CreateScale((float)zoom, (float)-zoom) *
                    Matrix3x2.CreateTranslation((float)viewport.Offset.X, (float)viewport.Offset.Y);
                context.DrawGeometry(geometry, brush, strokeWidth, stroke);
            });
        }
    }

    [Fact, Trait("Category", "WindowsIntegration")]
    public void FullyOffscreenOutlineCreatesNoStrokeEvenWhenEntityBoundsEncloseView()
    {
        using var target = new ImageSourceDirect2DResource();
        var document = CadDocument.Create("Surrounding outline");
        var path = document.AddPolyline([new(-1000, -1000), new(1000, -1000), new(1000, 1000), new(-1000, 1000)], true);
        Assert.True(Direct2DVisiblePolylineStroke.TryCreate(target.Factory!, path, CadRectD.FromXYWH(-10, -10, 20, 20),
            1, 1, out var geometry));
        Assert.Null(geometry);
        Assert.False(Direct2DVisiblePolylineStroke.TryCreate(target.Factory!, path, path.Bounds.Inflate(100), 1, 1, out geometry));
        Assert.Null(geometry);
    }

    [DxfStrokeSampleFact, Trait("Category", "WindowsIntegration")]
    public void SuppliedDxfLongStrokesPreserveFullPathRasterCoverageAcrossZoomAndPan()
    {
        const int width = 516, height = 384;
        var source = Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_DXF_SAMPLE")!;
        var document = new CadDxfStorage().Import(source, CadUnit.Millimeter).Document;
        using var target = new ImageSourceDirect2DResource(); target.SetTarget(new ImageSource(width, height));
        using var styles = new Direct2DStyleResourceCache(); styles.Reset(target.Factory, target.Context);
        using var text = new Direct2DTextFormatResourceCache(); text.Reset(target.DwriteFactory);
        var stats = new Direct2DRenderStatisticsCollector();
        using var resources = new Direct2DResourceCache(styles, text, stats, target.Factory, target.DwriteFactory, target.Context);
        resources.RebuildAll(document);
        var renderer = new Direct2DEntityRenderer(resources, new Direct2DGeometryFactory(), styles, stats);
        var viewport = new CadViewport(); viewport.SetSize(width, height);
        var bounds = document.Entities.Values.Aggregate(CadRectD.Empty, (b, e) => b.Union(e.Bounds));
        var zoom = Math.Min((width - 64) / bounds.Width, (height - 64) / bounds.Height);
        viewport.SetView(zoom, new(width / 2d - bounds.Center.X * zoom, height / 2d + bounds.Center.Y * zoom));
        var anchor = viewport.WorldToScreen(new(64, 40));
        var options = new CadRenderOptions { DrawGrid = false, DrawOrigin = false, IsLevelOfDetailEnabled = false, EnableGeometryRealizations = false };
        var polylines = document.Entities.Values.OfType<CadPolyline>().Where(p => p.Points.Count >= 512).ToArray();
        CadPolyline? only = null;
        var frame = 0;
        var comparisons = new List<object>();
        Assert.Contains(polylines, p => p.Points.Count > 3000);
        foreach (var direction in new[] { 1, -1 })
        for (var step = 0; step < 32; step++)
        {
            viewport.ZoomAt(anchor, direction > 0 ? 1.1 : 1 / 1.1);
            Compare();
        }
        foreach (var delta in new[] { new CadVectorD(130, -75), new CadVectorD(-280, 125), new CadVectorD(150, -50) })
        {
            viewport.PanScreen(delta); Compare();
        }
        var traceDirectory = Environment.GetEnvironmentVariable("DIRECT2DCAD_NATIVE_DXF_TRACE");
        if (!string.IsNullOrWhiteSpace(traceDirectory))
        {
            Directory.CreateDirectory(traceDirectory);
            File.WriteAllText(Path.Combine(traceDirectory, "stroke-pixels.json"),
                System.Text.Json.JsonSerializer.Serialize(new { source, frames = frame, paths = polylines.Length, comparisons },
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        void Compare()
        {
            frame++;
            // Compare each affected stroke separately. Drawing unrelated overlapping
            // antialiased entities in a single frame can change native batch coverage
            // when the number of submitted edges changes, even if each stroke agrees.
            foreach (var p in polylines)
            {
                only = p;
                Draw(false); var a = target.CaptureBackBufferPixels();
                Draw(true); var b = target.CaptureBackBufferPixels();
                var changedPixels = 0; var maxDifference = 0;
                for (var i = 0; i < a.Length; i += 4)
                {
                    var difference = Math.Max(Math.Max(Math.Abs(a[i] - b[i]), Math.Abs(a[i + 1] - b[i + 1])), Math.Abs(a[i + 2] - b[i + 2]));
                    if (difference > 0) changedPixels++;
                    maxDifference = Math.Max(maxDifference, difference);
                    Assert.Equal(a[i + 3], b[i + 3]);
                }
                // Direct2D can quantize a corner's antialias coverage differently
                // after offscreen figures are removed. Bound that rounding tightly;
                // missing edges or a changed line width exceed these limits.
                Assert.True(changedPixels <= 16 && maxDifference <= 32,
                    $"Stroke {p.Id}, frame {frame}, zoom {viewport.Zoom}: {changedPixels} pixels, max channel difference {maxDifference}");
                comparisons.Add(new { frame, entity = p.Id.Value, zoom = viewport.Zoom, changed_pixels = changedPixels, max_channel_difference = maxDifference });
            }
        }

        void Draw(bool optimized) => target.DrawFrame(context =>
        {
            context.Clear(new Color4(0, 0, 0, 1));
            context.Transform = Matrix3x2.CreateScale((float)viewport.Zoom, (float)-viewport.Zoom) *
                Matrix3x2.CreateTranslation((float)viewport.Offset.X, (float)viewport.Offset.Y);
            foreach (var path in polylines)
            {
                if (only is not null && path != only) continue;
                var bucket = resources.EntityResources[path.Id];
                Assert.Null(bucket.FillBrush); Assert.Null(bucket.HatchBrush);
                if (optimized) renderer.Draw(context, document, path, bucket, viewport, options);
                else
                {
                    var previous = context.Transform;
                    context.DrawGeometry(bucket.Geometry!, bucket.StrokeBrush!,
                        Direct2DEntityRenderer.ResolveStrokeWidth(bucket.StrokeWidth, viewport, options),
                        bucket.GraphicLineTypeStrokeStyle ?? bucket.StrokeStyle);
                    // Match the entity renderer's state restoration and resulting
                    // native batching boundaries as well as its stroke geometry.
                    context.Transform = previous;
                }
            }
        });
    }

    [Fact, Trait("Category", "WindowsIntegration")]
    public void DashedAndTransformedPathsKeepCompleteGeometryAndPhase()
    {
        using var target = new ImageSourceDirect2DResource();
        var document = CadDocument.Create("Fallbacks");
        var path = document.AddPolyline(Enumerable.Range(0, 600).Select(i => new CadPointD(i, i % 2)));
        using var bucket = new Direct2DResourceCache.EntityResourceBucket(path.Id)
        { Geometry = new Direct2DGeometryFactory().CreatePolyline(target.Factory!, path.Points, false) };
        var viewport = new CadViewport(); viewport.SetSize(100, 100); viewport.SetView(1, new(50, 50));
        using var brush = target.Context!.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        target.Context.Transform = Matrix3x2.CreateScale(1, -1) * Matrix3x2.CreateTranslation(50, 50);
        path.SetStrokeStyle(CadStrokeStyle.Default with { DashStyle = CadStrokeDashStyle.Dash });
        Assert.False(Direct2DVisiblePolylineStroke.TryDraw(target.Context, target.Factory, path, bucket.Geometry!, bucket, viewport, brush, 1, null));
        path.SetStrokeStyle(CadStrokeStyle.Default);
        target.Context.Transform = Matrix3x2.Identity; // Reusable command-list recording / block-local coordinates.
        Assert.False(Direct2DVisiblePolylineStroke.TryDraw(target.Context, target.Factory, path, bucket.Geometry!, bucket, viewport, brush, 1, null));
    }

    [Fact, Trait("Category", "WindowsIntegration")]
    public void LongScreenConstantStrokeDoesNotBuildPerZoomRealizations()
    {
        using var host = new Direct2DImageRenderHost(); host.AttachImageSource(new ImageSource(640, 480)); host.SetSize(640, 480);
        var document = CadDocument.Create("Long stroke budget");
        document.AddPolyline(Enumerable.Range(0, 600).Select(i => new CadPointD(i - 300, Math.Sin(i * .1) * 10)));
        var viewport = new CadViewport(); viewport.SetSize(640, 480); viewport.SetView(.5, new(320, 240));
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        host.SetRenderOptions(new() { DrawGrid = false, DrawOrigin = false, DrawGripHandles = false, EnableGeometryRealizations = true });
        for (var i = 0; i < 4; i++)
        {
            viewport.ZoomAt(new(320, 240), 1.1);
            host.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
            Assert.Equal(1, host.RenderStatistics.EntitySubmissionCount);
            Assert.Equal(0, host.RenderStatistics.GeometryRealizationBuildCount);
        }
    }

    private sealed class ImageSource(int width, int height) : ID3D11ImageSource
    {
        public int SurfaceWidth => width;
        public int SurfaceHeight => height;
        public void SetSize(int width, int height) { }
        public void SetSurface(nint surface) { }
        public void Present(Action presentAction, IReadOnlyList<IntRect>? dirtyRects = null) => presentAction();
        public void Invalidate() { }
        public void Invalidate(IntRect rect) { }
        public void Invalidate(IReadOnlyList<IntRect> rects) { }
    }
}

public sealed class DxfStrokeSampleFactAttribute : FactAttribute
{
    public DxfStrokeSampleFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DIRECT2DCAD_UI_DXF_SAMPLE")))
            Skip = "Set DIRECT2DCAD_UI_DXF_SAMPLE to the external Arduino DXF for a full-path pixel reference.";
    }
}

// Keep the hundreds of hardware pixel captures from competing with native
// cache tests whose realization preparation intentionally uses a time budget.
[CollectionDefinition("Native zoom pixel comparisons", DisableParallelization = true)]
public sealed class NativeZoomPixelComparisonCollection;
