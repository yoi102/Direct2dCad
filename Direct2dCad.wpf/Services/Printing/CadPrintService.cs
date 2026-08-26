using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.ViewModels.Services.Platform.Printing;

namespace Direct2dCad.wpf.Services.Printing;

public sealed class CadPrintService : ICadPrintService
{
    private const double DefaultRenderDpi = 300.0;
    private const int MaximumRenderPixelSide = 4096;

    public bool Print(CadPrintRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var renderBounds = ResolveRenderBounds(request);
        var printDialog = CreatePrintDialog(renderBounds);
        if (printDialog.ShowDialog() != true)
            return false;

        var page = ResolvePageMetrics(printDialog, renderBounds);
        var viewport = CreateViewport(renderBounds, page.PixelWidth, page.PixelHeight);
        var frame = Direct2DOffscreenRenderer.Render(
            request.Document,
            viewport,
            request.RenderOptions,
            page.PixelWidth,
            page.PixelHeight,
            request.OleDrawCallback);
        var bitmap = CreateBitmap(frame, page.OutputWidth, page.OutputHeight);

        printDialog.PrintVisual(CreatePrintVisual(bitmap, page), request.DocumentName);
        return true;
    }

    private static PrintDialog CreatePrintDialog(CadRectD renderBounds)
    {
        var dialog = new PrintDialog();
        dialog.PrintTicket.PageOrientation = renderBounds.Width > renderBounds.Height
            ? PageOrientation.Landscape
            : PageOrientation.Portrait;
        return dialog;
    }

    private static CadRectD ResolveRenderBounds(CadPrintRequest request)
    {
        if (request.RenderOptions.ActiveLayoutId is null)
            throw new InvalidOperationException("Printing is only available in layout space.");
        if (request.PaperBounds.IsEmpty ||
            !double.IsFinite(request.PaperBounds.Width) ||
            !double.IsFinite(request.PaperBounds.Height))
        {
            throw new InvalidOperationException("The active layout has invalid paper bounds.");
        }

        return request.PaperBounds;
    }

    private static CadPrintPageMetrics ResolvePageMetrics(
        PrintDialog dialog,
        CadRectD renderBounds)
    {
        var printableWidth = PositiveOrFallback(dialog.PrintableAreaWidth, 816);
        var printableHeight = PositiveOrFallback(dialog.PrintableAreaHeight, 1056);

        var contentAspect = renderBounds.Width / Math.Max(renderBounds.Height, double.Epsilon);
        var outputWidth = printableWidth;
        var outputHeight = outputWidth / contentAspect;
        if (outputHeight > printableHeight)
        {
            outputHeight = printableHeight;
            outputWidth = outputHeight * contentAspect;
        }

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(outputWidth / 96.0 * DefaultRenderDpi));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(outputHeight / 96.0 * DefaultRenderDpi));
        var maximumSide = Math.Max(pixelWidth, pixelHeight);
        if (maximumSide > MaximumRenderPixelSide)
        {
            var scale = MaximumRenderPixelSide / (double)maximumSide;
            pixelWidth = Math.Max(1, (int)Math.Round(pixelWidth * scale));
            pixelHeight = Math.Max(1, (int)Math.Round(pixelHeight * scale));
        }

        return new CadPrintPageMetrics(
            (printableWidth - outputWidth) * 0.5,
            (printableHeight - outputHeight) * 0.5,
            outputWidth,
            outputHeight,
            pixelWidth,
            pixelHeight);
    }

    private static CadViewport CreateViewport(CadRectD bounds, int width, int height)
    {
        var viewport = new CadViewport();
        viewport.SetSize(width, height);
        var zoom = Math.Min(
            width / Math.Max(bounds.Width, double.Epsilon),
            height / Math.Max(bounds.Height, double.Epsilon));
        viewport.SetView(
            zoom,
            new CadPointD(
                width * 0.5 - bounds.Center.X * zoom,
                height * 0.5 + bounds.Center.Y * zoom));
        return viewport;
    }

    private static BitmapSource CreateBitmap(
        Direct2DRenderedFrame frame,
        double outputWidth,
        double outputHeight)
    {
        var dpiX = frame.PixelWidth / outputWidth * 96.0;
        var dpiY = frame.PixelHeight / outputHeight * 96.0;
        var bitmap = BitmapSource.Create(
            frame.PixelWidth,
            frame.PixelHeight,
            dpiX,
            dpiY,
            PixelFormats.Bgra32,
            null,
            frame.Pixels,
            frame.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    private static DrawingVisual CreatePrintVisual(
        BitmapSource bitmap,
        CadPrintPageMetrics metrics)
    {
        var visual = new DrawingVisual();
        using var context = visual.RenderOpen();
        context.DrawImage(
            bitmap,
            new Rect(
                metrics.OutputX,
                metrics.OutputY,
                metrics.OutputWidth,
                metrics.OutputHeight));
        return visual;
    }

    private static double PositiveOrFallback(double value, double fallback) =>
        value > 0 && double.IsFinite(value) ? value : fallback;

    private sealed record CadPrintPageMetrics(
        double OutputX,
        double OutputY,
        double OutputWidth,
        double OutputHeight,
        int PixelWidth,
        int PixelHeight);
}
