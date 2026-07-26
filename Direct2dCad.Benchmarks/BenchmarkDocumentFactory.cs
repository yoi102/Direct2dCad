using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;

namespace Direct2dCad.Benchmarks;

public enum BenchmarkDocumentKind
{
    Lines,
    Mixed
}

public enum BenchmarkComplexDocumentKind
{
    Text,
    Hatch,
    Blocks,
    Images
}

internal sealed record BenchmarkDocumentData(
    CadDocument Document,
    EntityId[] EntityIds,
    CadRectD Bounds)
{
    public double Width => Math.Max(1.0, Bounds.Width);
    public double Height => Math.Max(1.0, Bounds.Height);
}

internal sealed record BenchmarkLayoutDocumentData(
    BenchmarkDocumentData Data,
    LayoutId LayoutId,
    LayoutViewportId ViewportId,
    BlockId PaperSpaceBlockId);

internal static class BenchmarkDocumentFactory
{
    private const double CellWidth = 6.0;
    private const double CellHeight = 5.0;

    public static BenchmarkDocumentData Create(
        int entityCount,
        BenchmarkDocumentKind kind)
    {
        if (entityCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(entityCount));

        var document = CadDocument.Create($"{kind} benchmark {entityCount:N0}");
        var columns = Math.Max(
            1,
            (int)Math.Ceiling(Math.Sqrt(entityCount * 16.0 / 9.0)));
        var rows = (entityCount + columns - 1) / columns;

        for (var index = 0; index < entityCount; index++)
        {
            var x = index % columns * CellWidth;
            var y = index / columns * CellHeight;
            if (kind == BenchmarkDocumentKind.Lines)
            {
                document.AddLine(
                    new CadPointD(x, y),
                    new CadPointD(x + 4.5, y + 1.0 + (index & 1)));
                continue;
            }

            AddMixedEntity(document, index, x, y);
        }

        return new BenchmarkDocumentData(
            document,
            [.. document.Entities.Keys],
            CadRectD.FromXYWH(0, 0, columns * CellWidth, rows * CellHeight));
    }

    public static BenchmarkDocumentData CreateComplex(BenchmarkComplexDocumentKind kind)
    {
        return kind switch
        {
            BenchmarkComplexDocumentKind.Text => CreateTextDocument(5_000),
            BenchmarkComplexDocumentKind.Hatch => CreateHatchDocument(2_000),
            BenchmarkComplexDocumentKind.Blocks => CreateBlockDocument(2_000, 12),
            BenchmarkComplexDocumentKind.Images => CreateImageDocument(512),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    public static BenchmarkLayoutDocumentData CreateLayoutDocument()
    {
        var modelData = Create(20_000, BenchmarkDocumentKind.Mixed);
        var document = modelData.Document;
        var layout = document.Layouts.Values.First();
        foreach (var viewport in layout.Viewports.ToArray())
            document.RemoveLayoutViewport(layout.Id, viewport.Id);

        var viewportBounds = CadRectD.FromLTRB(20, 42, 400, 277);
        var viewportScale = Math.Min(
            viewportBounds.Width / modelData.Width,
            viewportBounds.Height / modelData.Height) * 0.92;
        var viewportId = document.AddLayoutViewport(
            layout.Id,
            viewportBounds,
            modelData.Bounds.Center,
            viewportScale);

        var border = document.AddPolyline(
        [
            new CadPointD(10, 10),
            new CadPointD(410, 10),
            new CadPointD(410, 287),
            new CadPointD(10, 287)
        ], isClosed: true);
        document.MoveEntityToBlock(border.Id, layout.PaperSpaceBlockId);

        var titleDivider = document.AddLine(
            new CadPointD(10, 36),
            new CadPointD(410, 36));
        document.MoveEntityToBlock(titleDivider.Id, layout.PaperSpaceBlockId);

        var title = document.AddText(
            "DIRECT2DCAD BENCHMARK LAYOUT",
            new CadPointD(18, 19),
            8.0);
        document.MoveEntityToBlock(title.Id, layout.PaperSpaceBlockId);

        var data = new BenchmarkDocumentData(
            document,
            [.. document.Entities.Keys],
            layout.PaperBounds);
        return new BenchmarkLayoutDocumentData(
            data,
            layout.Id,
            viewportId,
            layout.PaperSpaceBlockId);
    }

    public static BenchmarkDocumentData FromDocument(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.RefreshBlockReferenceBounds();

        var bounds = CadRectD.Empty;
        foreach (var entity in document.GetEntitiesInBlock(BlockId.ModelSpace))
        {
            if (!entity.IsErased && entity.IsVisible && !entity.Bounds.IsEmpty)
                bounds = bounds.Union(entity.Bounds);
        }

        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            bounds = CadRectD.FromXYWH(0, 0, 100, 100);

        return new BenchmarkDocumentData(
            document,
            [.. document.Entities.Keys],
            bounds);
    }

    public static CadViewport CreateFittedViewport(
        BenchmarkDocumentData data,
        int surfaceWidth,
        int surfaceHeight,
        double marginPixels = 32.0)
    {
        ArgumentNullException.ThrowIfNull(data);
        var usableWidth = Math.Max(1.0, surfaceWidth - marginPixels * 2.0);
        var usableHeight = Math.Max(1.0, surfaceHeight - marginPixels * 2.0);
        var zoom = Math.Min(usableWidth / data.Width, usableHeight / data.Height);

        var viewport = new CadViewport();
        viewport.SetSize(surfaceWidth, surfaceHeight);
        var horizontalPadding = (surfaceWidth - data.Width * zoom) / 2.0;
        var verticalPadding = (surfaceHeight - data.Height * zoom) / 2.0;
        viewport.SetView(
            zoom,
            new CadPointD(
                horizontalPadding - data.Bounds.MinX * zoom,
                verticalPadding + data.Bounds.MaxY * zoom));
        return viewport;
    }

    private static CadEntity AddMixedEntity(
        CadDocument document,
        int index,
        double x,
        double y)
    {
        return (index % 20) switch
        {
            0 or 1 => document.AddCircle(new CadPointD(x + 2.0, y + 2.0), 1.5),
            2 or 3 => document.AddRectangle(CadRectD.FromXYWH(x, y, 4.0, 3.0)),
            4 => document.AddArcDegrees(new CadPointD(x + 2.0, y + 2.0), 1.7, 20, 245),
            5 => document.AddPolyline(
            [
                new CadPointD(x, y),
                new CadPointD(x + 2.0, y + 3.0),
                new CadPointD(x + 4.0, y + 0.5)
            ]),
            6 => document.AddSpline(
            [
                new CadPointD(x, y),
                new CadPointD(x + 1.5, y + 3.0),
                new CadPointD(x + 3.0, y + 1.0),
                new CadPointD(x + 4.5, y + 2.5)
            ]),
            7 => document.AddText($"T{index % 100}", new CadPointD(x, y + 2.5), 1.8),
            _ => document.AddLine(
                new CadPointD(x, y),
                new CadPointD(x + 4.5, y + 1.0 + (index & 1)))
        };
    }

    private static BenchmarkDocumentData CreateTextDocument(int entityCount)
    {
        var document = CadDocument.Create("Text benchmark");
        var columns = 100;
        var rows = (entityCount + columns - 1) / columns;
        for (var index = 0; index < entityCount; index++)
        {
            var x = index % columns * 12.0;
            var y = index / columns * 6.0;
            document.AddText(
                index % 4 == 0 ? $"CAD 日本語 {index}" : $"Label {index}",
                new CadPointD(x, y + 3.5),
                2.4,
                rotationRadians: index % 9 == 0 ? 0.15 : 0);
        }

        return new BenchmarkDocumentData(
            document,
            [.. document.Entities.Keys],
            CadRectD.FromXYWH(0, 0, columns * 12.0, rows * 6.0));
    }

    private static BenchmarkDocumentData CreateHatchDocument(int entityCount)
    {
        var document = CadDocument.Create("Hatch benchmark");
        var patternId = document.CreateHatchPattern(
            "Benchmark Brick",
            CadHatchPatternLines.Brick(4.0, 2.0));
        var fillStyleId = document.CreateHatchFillStyle(
            "Benchmark Hatch",
            patternId,
            CadColor.FromRgb(80, 210, 145),
            hatchScale: 1.0);
        var columns = 50;
        var rows = (entityCount + columns - 1) / columns;
        for (var index = 0; index < entityCount; index++)
        {
            var x = index % columns * 16.0;
            var y = index / columns * 12.0;
            document.AddRectangle(
                CadRectD.FromXYWH(x, y, 12.0, 8.0),
                cornerRadiusX: index % 5 == 0 ? 1.0 : 0,
                cornerRadiusY: index % 5 == 0 ? 1.0 : 0,
                fillStyleId: fillStyleId);
        }

        return new BenchmarkDocumentData(
            document,
            [.. document.Entities.Keys],
            CadRectD.FromXYWH(0, 0, columns * 16.0, rows * 12.0));
    }

    private static BenchmarkDocumentData CreateBlockDocument(
        int referenceCount,
        int entitiesPerDefinition)
    {
        var document = CadDocument.Create("Block benchmark");
        var blockId = document.CreateBlockDefinition("Benchmark Symbol", CadPointD.Origin);
        for (var index = 0; index < entitiesPerDefinition; index++)
        {
            var entity = AddMixedEntity(
                document,
                index,
                index % 4 * CellWidth,
                index / 4 * CellHeight);
            document.MoveEntityToBlock(entity.Id, blockId);
        }

        var columns = 50;
        var rows = (referenceCount + columns - 1) / columns;
        for (var index = 0; index < referenceCount; index++)
        {
            var x = index % columns * 32.0;
            var y = index / columns * 24.0;
            document.AddBlockReference(
                blockId,
                new CadPointD(x, y),
                rotationRadians: index % 8 * Math.PI / 16.0,
                scaleX: 0.8 + index % 3 * 0.2,
                scaleY: 0.8 + index % 3 * 0.2);
        }

        document.RefreshBlockReferenceBounds();
        return new BenchmarkDocumentData(
            document,
            [.. document.Entities.Keys],
            CadRectD.FromXYWH(-12, -12, columns * 32.0 + 24, rows * 24.0 + 24));
    }

    private static BenchmarkDocumentData CreateImageDocument(int entityCount)
    {
        const int pixelWidth = 32;
        const int pixelHeight = 32;
        const int stride = pixelWidth * 4;
        var pixels = CreateCheckerboardPixels(pixelWidth, pixelHeight);
        var document = CadDocument.Create("Image benchmark");
        var columns = 32;
        var rows = (entityCount + columns - 1) / columns;
        for (var index = 0; index < entityCount; index++)
        {
            var x = index % columns * 18.0;
            var y = index / columns * 14.0;
            document.AddImage(
                CadRectD.FromXYWH(x, y, 14.0, 10.0),
                pixelWidth,
                pixelHeight,
                stride,
                pixels,
                rotationRadians: index % 7 == 0 ? 0.2 : 0);
        }

        return new BenchmarkDocumentData(
            document,
            [.. document.Entities.Keys],
            CadRectD.FromXYWH(-2, -2, columns * 18.0 + 4, rows * 14.0 + 4));
    }

    private static byte[] CreateCheckerboardPixels(int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                var bright = ((x / 4) + (y / 4)) % 2 == 0;
                pixels[offset] = bright ? (byte)0xD0 : (byte)0x30;
                pixels[offset + 1] = bright ? (byte)0xA0 : (byte)0x60;
                pixels[offset + 2] = bright ? (byte)0x40 : (byte)0xC0;
                pixels[offset + 3] = 0xFF;
            }
        }

        return pixels;
    }
}
