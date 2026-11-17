using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Direct2dCad.Benchmarks;

/// <summary>
/// Raw Direct2D cost of drawing many short lines, independent of the CAD renderer, to tell how much of a
/// zoom frame is Direct2D itself and how much would be saved by submitting fewer, larger draw calls.
/// </summary>
internal static class D2DLineProbe
{
    public static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
        D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            [Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0], out ID3D11Device? device, out _, out ID3D11DeviceContext? d3dContext)
            .CheckError();
        using var d3d = device!;
        using var immediate = d3dContext!;
        using var dxgi = d3d.QueryInterface<IDXGIDevice>();
        using var d2dDevice = factory.CreateDevice(dxgi);
        using var context = d2dDevice.CreateDeviceContext(DeviceContextOptions.EnableMultithreadedOptimizations);
        using var target = context.CreateBitmap(new SizeI(1600, 900), IntPtr.Zero, 0, new BitmapProperties1
        {
            PixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            DpiX = 96, DpiY = 96, BitmapOptions = BitmapOptions.Target
        });
        context.Target = target;
        context.AntialiasMode = AntialiasMode.PerPrimitive;
        using var brush = context.CreateSolidColorBrush(new Color4(0.1f, 0.8f, 0.3f, 1f));
        using var fixedStroke = factory.CreateStrokeStyle(new StrokeStyleProperties1
        {
            StartCap = CapStyle.Flat, EndCap = CapStyle.Flat, LineJoin = LineJoin.Miter, MiterLimit = 10,
            TransformType = StrokeTransformType.Fixed
        }, []);

        // World units are millimetres; the view maps them to pixels like a fitted CAD viewport.
        var random = new Random(42);
        var results = new List<object>();
        foreach (var count in new[] { 1_000, 20_000, 100_000 })
        {
            var lines = new (Vector2 A, Vector2 B)[count];
            for (var i = 0; i < count; i++)
            {
                var a = new Vector2(random.NextSingle() * 1000, random.NextSingle() * 560);
                var b = a + new Vector2(random.NextSingle() * 20 - 10, random.NextSingle() * 20 - 10);
                lines[i] = (a, b);
            }
            var view = Matrix3x2.CreateScale(1.5f, -1.5f) * Matrix3x2.CreateTranslation(50, 870);
            const float width = 1.0f / 1.5f; // one device pixel at this zoom

            double Measure(string name, Action draw)
            {
                var samples = new List<double>();
                for (var frame = 0; frame < 12; frame++)
                {
                    var started = Stopwatch.GetTimestamp();
                    context.BeginDraw();
                    context.Clear(new Color4(0, 0, 0, 1));
                    context.Transform = view;
                    draw();
                    context.EndDraw().CheckError();
                    immediate.Flush();
                    samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
                var median = samples.Skip(2).Order().ElementAt(5);
                results.Add(new { count, scenario = name, median_ms = median, per_line_us = median * 1000 / count });
                Console.WriteLine($"{count,7} {name,-34} {median,8:F2} ms  {median * 1000 / count,6:F3} us/line");
                return median;
            }

            Measure("DrawLine per entity", () =>
            {
                foreach (var (a, b) in lines) context.DrawLine(a, b, brush, width);
            });
            Measure("DrawLine + transform per entity", () =>
            {
                foreach (var (a, b) in lines)
                {
                    var current = context.Transform;
                    context.Transform = current;
                    context.DrawLine(a, b, brush, width);
                }
            });
            Measure("DrawLine, scale without Y flip", () =>
            {
                context.Transform = Matrix3x2.CreateScale(1.5f, 1.5f) * Matrix3x2.CreateTranslation(50, 30);
                foreach (var (a, b) in lines) context.DrawLine(a, b, brush, width);
            });
            Measure("DrawLine, pre-transformed, identity", () =>
            {
                context.Transform = Matrix3x2.Identity;
                foreach (var (a, b) in lines)
                    context.DrawLine(Vector2.Transform(a, view), Vector2.Transform(b, view), brush, 1.0f);
            });
            Measure("DrawEllipse world width", () =>
            {
                foreach (var (a, _) in lines) context.DrawEllipse(new Ellipse(a, 4, 4), brush, width);
            });
            Measure("DrawEllipse fixed-width stroke", () =>
            {
                foreach (var (a, _) in lines) context.DrawEllipse(new Ellipse(a, 4, 4), brush, 1.0f, fixedStroke);
            });
            var small = lines.Select(line =>
            {
                var geometry = factory.CreatePathGeometry();
                using var sink = geometry.Open();
                sink.BeginFigure(line.A, FigureBegin.Hollow);
                sink.AddLine(line.B);
                sink.AddLine(line.B + new Vector2(3, 2));
                sink.EndFigure(FigureEnd.Open);
                sink.Close();
                return geometry;
            }).ToArray();
            var circles = lines.Select(line => (ID2D1Geometry)factory.CreateEllipseGeometry(new Ellipse(line.A, 4, 4))).ToArray();
            try
            {
                Measure("DrawGeometry per entity, world width", () =>
                {
                    foreach (var geometry in small) context.DrawGeometry(geometry, brush, width);
                });
                Measure("DrawGeometry per entity, fixed stroke", () =>
                {
                    foreach (var geometry in small) context.DrawGeometry(geometry, brush, 1.0f, fixedStroke);
                });
                Measure("DrawGeometry per entity, no Y flip", () =>
                {
                    context.Transform = Matrix3x2.CreateScale(1.5f, 1.5f) * Matrix3x2.CreateTranslation(50, 30);
                    foreach (var geometry in small) context.DrawGeometry(geometry, brush, width);
                });
                Measure("circle geometry, world width", () =>
                {
                    foreach (var geometry in circles) context.DrawGeometry(geometry, brush, width);
                });
                Measure("circle geometry, fixed stroke", () =>
                {
                    foreach (var geometry in circles) context.DrawGeometry(geometry, brush, 1.0f, fixedStroke);
                });
                Measure("circle geometry, no Y flip", () =>
                {
                    context.Transform = Matrix3x2.CreateScale(1.5f, 1.5f) * Matrix3x2.CreateTranslation(50, 30);
                    foreach (var geometry in circles) context.DrawGeometry(geometry, brush, width);
                });
            }
            finally
            {
                foreach (var geometry in small) geometry.Dispose();
                foreach (var geometry in circles) geometry.Dispose();
            }
            Measure("DrawLine fixed-width stroke", () =>
            {
                foreach (var (a, b) in lines) context.DrawLine(a, b, brush, 1.0f, fixedStroke);
            });
            ID2D1PathGeometry Build()
            {
                var geometry = factory.CreatePathGeometry();
                using var sink = geometry.Open();
                foreach (var (a, b) in lines)
                {
                    sink.BeginFigure(a, FigureBegin.Hollow);
                    sink.AddLine(b);
                    sink.EndFigure(FigureEnd.Open);
                }
                sink.Close();
                return geometry;
            }
            Measure("one path: build + DrawGeometry", () =>
            {
                using var geometry = Build();
                context.DrawGeometry(geometry, brush, width);
            });
            using var prebuilt = Build();
            Measure("one path: DrawGeometry only", () => context.DrawGeometry(prebuilt, brush, width));
            Measure("one path: fixed-width stroke", () => context.DrawGeometry(prebuilt, brush, 1.0f, fixedStroke));
        }

        File.WriteAllText(Path.Combine(outputDirectory, "d2d-line-probe.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
