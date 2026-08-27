using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Ole;
using Direct2dCad.ViewModels.Services.Platform.Printing;

namespace Direct2dCad.wpf.Services.Printing.Vector;

internal static class CadVectorPrintRenderer
{
    private const int OleTilePixelSide = 1024;

    public static DrawingVisual CreateVisual(
        CadPrintRequest request,
        CadLayout layout,
        Rect outputBounds,
        int embeddedRasterDpi)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(layout);
        if (outputBounds.IsEmpty || outputBounds.Width <= 0 || outputBounds.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(outputBounds));

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using var context = visual.RenderOpen();
        var paperBounds = layout.PaperBounds;
        var paperScale = Math.Min(
            outputBounds.Width / paperBounds.Width,
            outputBounds.Height / paperBounds.Height);
        var paperToOutput = new Matrix(
            paperScale,
            0,
            0,
            -paperScale,
            outputBounds.X - paperBounds.MinX * paperScale,
            outputBounds.Y + paperBounds.MaxY * paperScale);

        context.PushClip(new RectangleGeometry(outputBounds));
        context.PushTransform(new MatrixTransform(paperToOutput));
        try
        {
            context.DrawRectangle(
                CadVectorPrintStyleResolver.CreateBrush(layout.PaperColor),
                null,
                CadVectorPrintGeometryFactory.ToRect(paperBounds));

            foreach (var layoutViewport in layout.Viewports)
            {
                if (!layoutViewport.IsVisible)
                    continue;

                context.PushClip(new RectangleGeometry(
                    CadVectorPrintGeometryFactory.ToRect(layoutViewport.Bounds)));
                try
                {
                    RenderBlock(
                        context,
                        request,
                        BlockId.ModelSpace,
                        CreateModelToPaperTransform(layoutViewport),
                        paperScale,
                        embeddedRasterDpi,
                        layout.PaperColor,
                        blockStyle: null,
                        visitedBlocks: []);
                }
                finally
                {
                    context.Pop();
                }
            }

            RenderBlock(
                context,
                request,
                layout.PaperSpaceBlockId,
                CadMatrixD.Identity,
                paperScale,
                embeddedRasterDpi,
                layout.PaperColor,
                blockStyle: null,
                visitedBlocks: []);
        }
        finally
        {
            context.Pop();
            context.Pop();
        }

        return visual;
    }

    private static void RenderBlock(
        DrawingContext context,
        CadPrintRequest request,
        BlockId blockId,
        CadMatrixD ownerToPaper,
        double paperScale,
        int embeddedRasterDpi,
        CadColor backgroundColor,
        CadVectorPrintBlockStyle? blockStyle,
        HashSet<BlockId> visitedBlocks)
    {
        var document = request.Document;
        if (!visitedBlocks.Add(blockId) ||
            !document.TryGetBlock(blockId, out var block) ||
            block is null)
        {
            return;
        }

        try
        {
            foreach (var entity in GetOrderedEntities(document, block))
            {
                if (entity is CadBlockReference reference)
                {
                    DrawBlockReference(
                        context,
                        request,
                        reference,
                        ownerToPaper,
                        paperScale,
                        embeddedRasterDpi,
                        backgroundColor,
                        blockStyle,
                        visitedBlocks);
                    continue;
                }

                if (!CadVectorPrintStyleResolver.TryResolve(
                        document,
                        entity,
                        blockStyle,
                        out var style))
                {
                    continue;
                }

                switch (entity)
                {
                    case CadText text:
                        DrawText(
                            context,
                            document,
                            text,
                            style,
                            ownerToPaper,
                            backgroundColor);
                        break;
                    case CadImage image:
                        DrawImage(context, image, ownerToPaper);
                        break;
                    case CadOleObject ole:
                        DrawOle(
                            context,
                            request.OleDrawCallback,
                            ole,
                            ownerToPaper,
                            paperScale,
                            embeddedRasterDpi);
                        break;
                    case CadShapeText shapeText:
                        DrawShapeText(
                            context,
                            shapeText,
                            style,
                            ownerToPaper,
                            paperScale,
                            backgroundColor);
                        break;
                    default:
                        DrawGeometryEntity(
                            context,
                            document,
                            entity,
                            style,
                            ownerToPaper,
                            paperScale);
                        break;
                }
            }
        }
        finally
        {
            visitedBlocks.Remove(blockId);
        }
    }

    private static void DrawBlockReference(
        DrawingContext context,
        CadPrintRequest request,
        CadBlockReference reference,
        CadMatrixD ownerToPaper,
        double paperScale,
        int embeddedRasterDpi,
        CadColor backgroundColor,
        CadVectorPrintBlockStyle? parentStyle,
        HashSet<BlockId> visitedBlocks)
    {
        var document = request.Document;
        if (!CadVectorPrintStyleResolver.TryResolveBlockStyle(
                document,
                reference,
                parentStyle,
                out var referenceStyle) ||
            !document.TryGetBlock(reference.DefinitionBlockId, out var definition) ||
            definition is null)
        {
            return;
        }

        var transform = CadBlockTransform.Create(definition, reference) * ownerToPaper;
        RenderBlock(
            context,
            request,
            reference.DefinitionBlockId,
            transform,
            paperScale,
            embeddedRasterDpi,
            backgroundColor,
            referenceStyle,
            visitedBlocks);
    }

    private static void DrawGeometryEntity(
        DrawingContext context,
        CadDocument document,
        CadEntity entity,
        CadVectorPrintEntityStyle style,
        CadMatrixD ownerToPaper,
        double paperScale)
    {
        var sourceGeometry = CadVectorPrintGeometryFactory.Create(entity);
        if (sourceGeometry is null || sourceGeometry.IsEmpty())
            return;

        var geometry = CadVectorPrintGeometryFactory.Transform(sourceGeometry, ownerToPaper);
        switch (style.FillStyle)
        {
            case CadHatchFillStyle hatch
                when document.TryGetHatchPattern(hatch.PatternId, out var pattern) &&
                     pattern is not null:
                CadVectorPrintHatchRenderer.Draw(
                    context,
                    geometry,
                    entity.Bounds,
                    ownerToPaper,
                    hatch,
                    pattern,
                    paperScale);
                break;
            case { } fill:
                var fillBrush = CadVectorPrintStyleResolver.CreateFillBrush(fill);
                if (fillBrush is not null)
                    context.DrawGeometry(fillBrush, null, geometry);
                break;
        }

        var pen = CadVectorPrintStyleResolver.CreatePen(style, paperScale, ownerToPaper);
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawShapeText(
        DrawingContext context,
        CadShapeText text,
        CadVectorPrintEntityStyle style,
        CadMatrixD ownerToPaper,
        double paperScale,
        CadColor backgroundColor)
    {
        var sourceGeometry = CadVectorPrintGeometryFactory.Create(text);
        if (sourceGeometry is null)
            return;

        var geometry = CadVectorPrintGeometryFactory.Transform(sourceGeometry, ownerToPaper);
        var penStyle = style;
        if (text.IsInverted)
        {
            var background = CadVectorPrintGeometryFactory.Transform(
                new RectangleGeometry(CadVectorPrintGeometryFactory.ToRect(text.InvertedBackgroundBounds)),
                ownerToPaper);
            context.DrawGeometry(
                CadVectorPrintStyleResolver.CreateBrush(style.StrokeColor),
                null,
                background);
            penStyle = style with { StrokeColor = backgroundColor };
        }

        context.DrawGeometry(
            null,
            CadVectorPrintStyleResolver.CreatePen(penStyle, paperScale, ownerToPaper),
            geometry);
    }

    private static void DrawText(
        DrawingContext context,
        CadDocument document,
        CadText text,
        CadVectorPrintEntityStyle style,
        CadMatrixD ownerToPaper,
        CadColor backgroundColor)
    {
        var textStyle = text.TextStyleId is { } styleId &&
                        document.TryGetStyle(styleId, out var resolvedStyle) &&
                        resolvedStyle is CadTextStyle resolvedTextStyle
            ? resolvedTextStyle
            : null;
        var typeface = new Typeface(
            new FontFamily(textStyle?.FontFamily ?? "Meiryo"),
            textStyle?.IsItalic == true ? FontStyles.Italic : FontStyles.Normal,
            textStyle?.IsBold == true ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        var localBounds = text.LocalBounds;
        var textToOwner = CadMatrixD.CreateScale(1, -1) *
                          CadMatrixD.CreateTranslation(
                              text.Position.X,
                              text.Position.Y + localBounds.MinY + localBounds.MaxY) *
                          CadMatrixD.CreateRotation(text.RotationRadians, text.Position);
        var transform = textToOwner * ownerToPaper;

        var textColor = style.StrokeColor;
        if (text.IsInverted)
        {
            var rotation = CadMatrixD.CreateRotation(text.RotationRadians, text.Position) * ownerToPaper;
            var background = CadVectorPrintGeometryFactory.Transform(
                new RectangleGeometry(CadVectorPrintGeometryFactory.ToRect(text.InvertedBackgroundBounds)),
                rotation);
            context.DrawGeometry(
                CadVectorPrintStyleResolver.CreateBrush(style.StrokeColor),
                null,
                background);
            textColor = backgroundColor;
        }

        var formatted = new FormattedText(
            string.IsNullOrEmpty(text.Text) ? " " : text.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            text.Height * CadText.FontSizeScale,
            CadVectorPrintStyleResolver.CreateBrush(textColor),
            1.0);
        context.PushTransform(new MatrixTransform(
            CadVectorPrintGeometryFactory.ToMatrix(transform)));
        try
        {
            context.DrawText(formatted, new Point(0, 0));
        }
        finally
        {
            context.Pop();
        }
    }

    private static void DrawImage(
        DrawingContext context,
        CadImage image,
        CadMatrixD ownerToPaper)
    {
        var bitmap = BitmapSource.Create(
            image.PixelWidth,
            image.PixelHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            image.PixelMemory.ToArray(),
            image.Stride);
        bitmap.Freeze();
        var center = image.FrameBounds.Center;
        var imageToPaper = CadMatrixD.CreateScale(1, -1, center) *
                           CadMatrixD.CreateRotation(image.RotationRadians, center) *
                           ownerToPaper;
        context.PushTransform(new MatrixTransform(
            CadVectorPrintGeometryFactory.ToMatrix(imageToPaper)));
        context.PushOpacity(image.Opacity);
        try
        {
            context.DrawImage(bitmap, CadVectorPrintGeometryFactory.ToRect(image.FrameBounds));
        }
        finally
        {
            context.Pop();
            context.Pop();
        }
    }

    private static void DrawOle(
        DrawingContext context,
        Direct2DOleDrawCallback? drawCallback,
        CadOleObject ole,
        CadMatrixD ownerToPaper,
        double paperScale,
        int rasterDpi)
    {
        if (drawCallback is null)
            return;

        var paperBounds = ole.Bounds.Transform(ownerToPaper);
        var pixelScale = Math.Max(rasterDpi, 96) / 96.0;
        var pixelWidth = ResolvePixelSize(paperBounds.Width * paperScale * pixelScale);
        var pixelHeight = ResolvePixelSize(paperBounds.Height * paperScale * pixelScale);
        var center = ole.Bounds.Center;
        var oleToPaper = CadMatrixD.CreateScale(1, -1, center) * ownerToPaper;
        context.PushTransform(new MatrixTransform(
            CadVectorPrintGeometryFactory.ToMatrix(oleToPaper)));
        context.PushOpacity(ole.Opacity);
        try
        {
            for (var regionY = 0; regionY < pixelHeight; regionY += OleTilePixelSide)
            {
                var tileHeight = Math.Min(OleTilePixelSide, pixelHeight - regionY);
                for (var regionX = 0; regionX < pixelWidth; regionX += OleTilePixelSide)
                {
                    var tileWidth = Math.Min(OleTilePixelSide, pixelWidth - regionX);
                    var data = drawCallback(new Direct2DOleDrawRequest(
                        Direct2DOleRenderKey.ForEntity(ole.Id),
                        ole.OleMemory,
                        pixelWidth,
                        pixelHeight,
                        regionX,
                        regionY,
                        tileWidth,
                        tileHeight));
                    if (!IsValidOleTile(data, tileWidth, tileHeight))
                        continue;

                    var bitmap = BitmapSource.Create(
                        data!.PixelWidth,
                        data.PixelHeight,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        null,
                        data.Pixels,
                        data.Stride);
                    bitmap.Freeze();
                    context.DrawImage(
                        bitmap,
                        CreateOleTileBounds(
                            ole.Bounds,
                            regionX,
                            regionY,
                            tileWidth,
                            tileHeight,
                            pixelWidth,
                            pixelHeight));
                }
            }
        }
        finally
        {
            context.Pop();
            context.Pop();
        }
    }

    private static bool IsValidOleTile(
        Direct2DOleDrawData? data,
        int expectedWidth,
        int expectedHeight) =>
        data is not null &&
        data.PixelWidth == expectedWidth &&
        data.PixelHeight == expectedHeight &&
        data.Stride >= checked(expectedWidth * 4) &&
        data.Pixels.Length >= checked(data.Stride * data.PixelHeight);

    private static Rect CreateOleTileBounds(
        CadRectD bounds,
        int regionX,
        int regionY,
        int regionWidth,
        int regionHeight,
        int fullWidth,
        int fullHeight)
    {
        var x = bounds.MinX + bounds.Width * regionX / fullWidth;
        var y = bounds.MinY + bounds.Height * regionY / fullHeight;
        var width = bounds.Width * regionWidth / fullWidth;
        var height = bounds.Height * regionHeight / fullHeight;
        return new Rect(x, y, width, height);
    }

    private static IReadOnlyList<CadEntity> GetOrderedEntities(
        CadDocument document,
        CadBlockDefinition block)
    {
        var entities = new List<(CadEntity Entity, int Index)>(block.EntityIds.Count);
        for (var index = 0; index < block.EntityIds.Count; index++)
        {
            if (document.TryGetEntity(block.EntityIds[index], out var entity) && entity is not null)
                entities.Add((entity, index));
        }

        return entities
            .OrderBy(item =>
                document.DocumentSettings.LayerDrawingPriority.GetPriority(item.Entity.LayerId))
            .ThenBy(item => item.Entity.ZIndex)
            .ThenBy(item => item.Index)
            .ThenBy(item => item.Entity.Id.Value)
            .Select(item => item.Entity)
            .ToArray();
    }

    private static CadMatrixD CreateModelToPaperTransform(CadLayoutViewport viewport) =>
        CadMatrixD.CreateTranslation(-viewport.ModelCenter.X, -viewport.ModelCenter.Y) *
        CadMatrixD.CreateRotation(viewport.RotationRadians) *
        CadMatrixD.CreateScale(viewport.Scale) *
        CadMatrixD.CreateTranslation(viewport.Bounds.Center.X, viewport.Bounds.Center.Y);

    private static int ResolvePixelSize(double value)
    {
        if (!double.IsFinite(value) || value <= 1)
            return 1;
        return value >= int.MaxValue ? int.MaxValue : (int)Math.Ceiling(value);
    }
}
