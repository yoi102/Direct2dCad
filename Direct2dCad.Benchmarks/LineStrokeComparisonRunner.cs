using System.Windows.Media;
using System.Windows.Media.Imaging;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;

namespace Direct2dCad.Benchmarks;

/// <summary>Renders the same lines with world-space and device-space strokes and writes both images.</summary>
internal static class LineStrokeComparisonRunner
{
    public static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var document = CadDocument.Create("line comparison");
        document.GetLayer(LayerId.Default).SetColor(CadColor.White);
        var random = new Random(11);
        for (var index = 0; index < 400; index++)
        {
            var start = new CadPointD(random.NextDouble() * 300, random.NextDouble() * 170);
            var angle = random.NextDouble() * Math.PI;
            var line = document.AddLine(start, start + new CadVectorD(Math.Cos(angle), Math.Sin(angle)) * (5 + random.NextDouble() * 40));
            line.SetLineWeight(new(index % 4 == 0 ? 0.5 : 0));
        }
        var data = BenchmarkDocumentFactory.FromDocument(document);
        byte[] Render(bool deviceSpace)
        {
            using var session = new BenchmarkRenderSession(data, surfaceWidth: 800, surfaceHeight: 450);
            session.RenderHost.DeviceSpaceLineStrokesEnabled = deviceSpace;
            session.WarmUp();
            session.RenderHost.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
            return session.RenderHost.CaptureBackBufferPixels();
        }

        var world = Render(false);
        var device = Render(true);
        var diff = new byte[world.Length];
        for (var i = 0; i < world.Length; i += 4)
        {
            var delta = 0;
            for (var c = 0; c < 3; c++) delta = Math.Max(delta, Math.Abs(world[i + c] - device[i + c]));
            var amplified = (byte)Math.Min(255, delta * 4);
            diff[i] = diff[i + 1] = 0; diff[i + 2] = amplified; diff[i + 3] = 255;
        }
        Save(world, "world-space.png");
        Save(device, "device-space.png");
        Save(diff, "difference-x4.png");

        void Save(byte[] pixels, string name)
        {
            var bitmap = BitmapSource.Create(800, 450, 96, 96, PixelFormats.Bgra32, null, pixels, 800 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(outputDirectory, name));
            encoder.Save(stream);
        }
    }
}
