using System.Diagnostics;
using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Direct2D.Resources;
using Xunit.Abstractions;

namespace Direct2dCad.Windows.IntegrationTests;

/// <summary>
/// Lines under the mirrored CAD view are stroked in device space because Direct2D's mirrored DrawLine path is
/// several times slower. The raster must match the world-space reference it replaces.
/// </summary>
[Collection("Native zoom pixel comparisons")]
public sealed class DeviceSpaceLineStrokeTests(ITestOutputHelper output)
{
    private const int Width = 640, Height = 480;

    [Theory]
    [InlineData(false, 2.0)]
    [InlineData(true, 2.0)]
    [InlineData(true, 0.37)]
    [Trait("Category", "WindowsIntegration")]
    public void DeviceSpaceLinesMatchTheWorldSpaceRaster(bool antialiasing, double zoom)
    {
        var document = Drawing();
        var reference = Render(document, zoom, antialiasing, deviceSpace: false);
        var actual = Render(document, zoom, antialiasing, deviceSpace: true);

        var (differentPixels, strongPixels, maxChannel, meanChannel) = Compare(reference, actual);
        var inkPixels = CountInk(reference);
        output.WriteLine($"aa={antialiasing} zoom={zoom}: ink {inkPixels}, different {differentPixels}, " +
                         $">32 {strongPixels}, max {maxChannel}, mean {meanChannel:F5}");
        Assert.True(inkPixels > 2_000, "The fixture must draw a meaningful amount of line work.");
        // Measured on Direct2D hardware: aliased lines differ in a few rounding pixels, antialiased edges by
        // at most about 56/255 with a mean near 1/255. Lines never move, disappear or change width.
        AssertSameLineWork(reference, actual, Width, antialiasing ? 80 : 255);
        Assert.True(meanChannel < (antialiasing ? 2.0 : 0.05), $"Mean channel difference {meanChannel:F4} is too large.");
        if (!antialiasing)
            Assert.True(differentPixels <= inkPixels / 100, $"{differentPixels} of {inkPixels} aliased pixels changed.");
    }

    /// <summary>
    /// Asserts both images contain the same line work: every inked pixel of one has ink within one pixel in the
    /// other (no moved, missing or widened lines) and no channel differs by more than <paramref name="maximumChannel"/>.
    /// </summary>
    internal static void AssertSameLineWork(byte[] expected, byte[] actual, int width, int maximumChannel)
    {
        Assert.Equal(expected.Length, actual.Length);
        var height = expected.Length / 4 / width;
        var largest = 0;
        for (var i = 0; i < expected.Length; i += 4)
            for (var c = 0; c < 3; c++)
                largest = Math.Max(largest, Math.Abs(expected[i + c] - actual[i + c]));
        Assert.True(largest <= maximumChannel, $"A channel differs by {largest}, above {maximumChannel}.");
        Assert.Equal(0, CountUnmatchedInk(expected, actual, width, height));
        Assert.Equal(0, CountUnmatchedInk(actual, expected, width, height));
    }

    private static int CountUnmatchedInk(byte[] source, byte[] other, int width, int height)
    {
        static bool Ink(byte[] pixels, int index) => pixels[index] + pixels[index + 1] + pixels[index + 2] > 192;
        var unmatched = 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            if (!Ink(source, (y * width + x) * 4))
                continue;
            var found = false;
            for (var dy = -1; dy <= 1 && !found; dy++)
            for (var dx = -1; dx <= 1 && !found; dx++)
            {
                var nx = x + dx;
                var ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < width && ny < height &&
                    other[(ny * width + nx) * 4] + other[(ny * width + nx) * 4 + 1] + other[(ny * width + nx) * 4 + 2] > 0)
                {
                    found = true;
                }
            }
            if (!found)
                unmatched++;
        }
        return unmatched;
    }

    [Fact]
    [Trait("Category", "WindowsIntegration")]
    public void OnlyMirroredUniformTransformsUseDeviceSpace()
    {
        Assert.False(Direct2DLineSubmission.TryGetMirroredSimilarityScale(Matrix3x2.Identity, out _));
        Assert.False(Direct2DLineSubmission.TryGetMirroredSimilarityScale(Matrix3x2.CreateScale(3, 3), out _));
        Assert.False(Direct2DLineSubmission.TryGetMirroredSimilarityScale(Matrix3x2.CreateScale(2, -1), out _));
        Assert.False(Direct2DLineSubmission.TryGetMirroredSimilarityScale(
            Matrix3x2.CreateSkew(0.3f, 0) * Matrix3x2.CreateScale(2, -2), out _));

        Assert.True(Direct2DLineSubmission.TryGetMirroredSimilarityScale(
            Matrix3x2.CreateScale(2.5f, -2.5f) * Matrix3x2.CreateTranslation(40, 900), out var flipped));
        Assert.Equal(2.5f, flipped, 4);
        Assert.True(Direct2DLineSubmission.TryGetMirroredSimilarityScale(
            Matrix3x2.CreateRotation(0.7f) * Matrix3x2.CreateScale(0.25f, -0.25f), out var rotated));
        Assert.Equal(0.25f, rotated, 4);
    }

    private static CadDocument Drawing()
    {
        var document = CadDocument.Create("device-space lines");
        document.GetLayer(LayerId.Default).SetColor(CadColor.White);
        var random = new Random(7);
        CadStrokeCap[] caps = [CadStrokeCap.Flat, CadStrokeCap.Round, CadStrokeCap.Square];
        double[] weights = [0, 0.25, 0.5, 1.0];
        for (var index = 0; index < 240; index++)
        {
            var start = new CadPointD(random.NextDouble() * 280 - 140, random.NextDouble() * 200 - 100);
            var angle = random.NextDouble() * Math.PI * 2;
            var length = 4 + random.NextDouble() * 60;
            var line = document.AddLine(start, start + new CadVectorD(Math.Cos(angle), Math.Sin(angle)) * length);
            line.SetLineWeight(new(weights[index % weights.Length]));
            var cap = caps[index % caps.Length];
            line.SetStrokeStyle(line.StrokeStyle with { StartCap = cap, EndCap = cap });
        }

        // Dashed lines keep the world-space path and must stay identical.
        var dashed = document.AddLine(new(-130, -95), new(130, 95));
        dashed.SetStrokeStyle(dashed.StrokeStyle with { DashStyle = CadStrokeDashStyle.Dash });

        var pattern = document.CreateHatchPattern("diagonal", [new CadHatchLineDefinition(45, default, new(0, 4))]);
        var hatch = document.CreateHatchFillStyle("hatch", pattern, CadColor.Green, hatchScale: 1);
        document.AddPolyline([new(20, 20), new(120, 20), new(120, 90), new(20, 90)], true, fillStyleId: hatch);
        return document;
    }

    private static byte[] Render(CadDocument document, double zoom, bool antialiasing, bool deviceSpace)
    {
        var viewport = new CadViewport();
        viewport.SetSize(Width, Height);
        viewport.SetView(zoom, new(Width / 2.0, Height / 2.0));
        using var host = new Direct2DImageRenderHost();
        host.DeviceSpaceLineStrokesEnabled = deviceSpace;
        host.AttachImageSource(new ImageSource());
        host.SetSize(Width, Height);
        host.SetRenderOptions(new()
        {
            DrawGrid = false, DrawOrigin = false, DrawGripHandles = false,
            IsLevelOfDetailEnabled = false, EnableGeometryRealizations = true, IsAntialiasingEnabled = antialiasing
        });
        host.SetScene(document, viewport, prepareResourcesInBackground: false);
        var started = Stopwatch.GetTimestamp();
        while (host.PrepareRenderCacheStep())
        {
            Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(15), "Cache preparation must finish.");
            Thread.Yield();
        }
        host.Render(CadRenderInvalidation.Full);
        return host.CaptureBackBufferPixels();
    }

    private static (int Different, int Strong, int MaxChannel, double MeanChannel) Compare(byte[] expected, byte[] actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        int different = 0, strong = 0, max = 0;
        long sum = 0;
        for (var i = 0; i < expected.Length; i += 4)
        {
            var pixelMax = 0;
            for (var c = 0; c < 3; c++)
            {
                var delta = Math.Abs(expected[i + c] - actual[i + c]);
                sum += delta;
                pixelMax = Math.Max(pixelMax, delta);
            }
            if (pixelMax > 0) different++;
            if (pixelMax > 32) strong++;
            max = Math.Max(max, pixelMax);
        }
        return (different, strong, max, sum / (double)(expected.Length / 4 * 3));
    }

    private static int CountInk(byte[] pixels)
    {
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i] + pixels[i + 1] + pixels[i + 2] > 96) count++;
        return count;
    }

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
