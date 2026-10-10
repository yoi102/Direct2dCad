using System.Runtime.InteropServices;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.ViewModels.Services.Platform.Printing;

namespace Direct2dCad.Avalonia.Services;

internal sealed class WindowsPrintService : ICadPrintService
{
    private sealed record ScaleOption(CadPaperScaling Value, string Label) { public override string ToString() => Label; }
    internal static Direct2DRenderedFrame RenderPage(CadPrintRequest request, CadViewport viewport, CadRenderOptions options, int width, int height)
    {
        var background = request.Document.ViewSettings.BackgroundColor;
        try { request.Document.ViewSettings.BackgroundColor = Direct2dCad.Db.Cad.CadColor.White; return Direct2DOffscreenRenderer.Render(request.Document, viewport, options, width, height, request.OleDrawCallback); }
        finally { request.Document.ViewSettings.BackgroundColor = background; }
    }
    private static void Fit(CadViewport viewport, CadRectD bounds)
    {
        var zoom = Math.Min(viewport.ViewWidth / bounds.Width, viewport.ViewHeight / bounds.Height);
        viewport.SetView(zoom, new CadPointD(viewport.ViewWidth / 2 - bounds.Center.X * zoom, viewport.ViewHeight / 2 + bounds.Center.Y * zoom));
    }
    internal static Direct2DVectorPage CreateVectorPage(CadPrintRequest request, CadRectD bounds, double width, double height, CadRectD printable, CadPaperScaling scaling, double percent = 100)
    {
        var placement = CadPrintPlacement.Calculate(bounds, printable, scaling, percent);
        var viewport = new CadViewport(); viewport.SetSize(width, height);
        viewport.SetView(placement.Scale, new CadPointD(placement.Output.MinX - bounds.MinX * placement.Scale, placement.Output.MinY + bounds.MaxY * placement.Scale));
        var options = new CadRenderOptions { ActiveLayoutId = request.IsModelSpace ? null : request.ActiveLayoutId, DrawGrid = false, DrawOrigin = false, DrawGripHandles = false,
            DrawLayoutGuides = false, KeepStrokeWidthScreenConstant = false, MinimumScreenStrokeWidth = 0, EnableGeometryRealizations = false, IsLevelOfDetailEnabled = false };
        var clip = new CadScreenRect((int)Math.Ceiling(printable.MinX), (int)Math.Ceiling(printable.MinY), (int)Math.Floor(printable.Width), (int)Math.Floor(printable.Height));
        return new(request.Document, viewport, options, request.OleDrawCallback, clip);
    }
    public async Task<bool> PrintAsync(CadPrintRequest request, Action? onPrintStarted = null, Action<bool>? onBusyChanged = null, Action? onPrintCompleted = null, Action<CadPrintCompletion>? onPrintFinished = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var preview = new Views.WindowsPrintPreview(request);
            using var registration = cancellationToken.Register(() => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => preview.Window.Close(false)));
            if (!await preview.Window.ShowDialog<bool>(App.Window!)) return false;
            cancellationToken.ThrowIfCancellationRequested(); onPrintStarted?.Invoke(); onBusyChanged?.Invoke(true);
            using var page = CreateVectorPage(request,preview.Bounds,preview.PageWidth,preview.PageHeight,preview.Printable,preview.Scaling,preview.Percent);
            VectorPrintBridge.Submit(page,preview.Printer.Name,request.DocumentName,preview.Printer.Mode,preview.PageWidth,preview.PageHeight,preview.Dpi);
            onPrintCompleted?.Invoke(); onPrintFinished?.Invoke(new(CadPrintCompletionStatus.Completed)); return true;
        }
        catch(OperationCanceledException) { onPrintFinished?.Invoke(new(CadPrintCompletionStatus.Cancelled)); throw; }
        catch(Exception ex) { onPrintFinished?.Invoke(new(CadPrintCompletionStatus.Failed,ex.Message)); throw; }
        finally { onBusyChanged?.Invoke(false); }
    }
}
