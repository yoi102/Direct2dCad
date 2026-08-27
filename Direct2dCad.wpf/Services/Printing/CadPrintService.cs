using System.Printing;
using System.Windows;
using System.Windows.Documents.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang.Strings;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using Direct2dCad.wpf.Views.Dialogs;

namespace Direct2dCad.wpf.Services.Printing;

public sealed class CadPrintService : ICadPrintService
{
    private const double DefaultRenderDpi = 300.0;
    private const int MaximumRenderPixelSide = 4096;
    private const int MaximumPreviewPixelSide = 1600;
    private const double DefaultPageWidth = 816.0;
    private const double DefaultPageHeight = 1056.0;

    public async Task<bool> PrintAsync(
        CadPrintRequest request,
        Action? onPrintStarted = null,
        Action<bool>? onBusyChanged = null,
        Action? onPrintCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        var renderBounds = ResolveRenderBounds(request);
        var preparation = await RunWithBusyIndicatorAsync(
            () => PreparePreview(request, renderBounds),
            onBusyChanged);
        var initialOrientation = renderBounds.Width > renderBounds.Height
            ? PageOrientation.Landscape
            : PageOrientation.Portrait;
        var previewDialog = new CadPrintPreviewDialog(
            preparation.Preview,
            request.DocumentName,
            preparation.Printers,
            initialOrientation)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (previewDialog.ShowDialog() != true || previewDialog.Selection is null)
            return false;

        onPrintStarted?.Invoke();
        var submission = await RunTaskWithBusyIndicatorAsync(
            () => StartPrintJobAsync(
                request,
                renderBounds,
                previewDialog.Selection),
            onBusyChanged);
        _ = NotifyWhenPrintCompletesAsync(
            submission,
            onPrintCompleted,
            System.Windows.Application.Current?.Dispatcher);

        return true;
    }

    private static CadPrintPreparation PreparePreview(
        CadPrintRequest request,
        CadRectD renderBounds)
    {
        var printers = GetInstalledPrinters();
        if (printers.Count == 0)
        {
            throw new InvalidOperationException(Strings.NoPrintersAvailable);
        }

        return new CadPrintPreparation(
            printers,
            CreatePreviewBitmap(request, renderBounds));
    }

    private static async Task<T> RunWithBusyIndicatorAsync<T>(
        Func<T> operation,
        Action<bool>? onBusyChanged)
    {
        onBusyChanged?.Invoke(true);
        try
        {
            // Give the existing progress dialog a render pass before starting the
            // printer/Direct2D work on a dedicated STA worker.
            await Dispatcher.Yield(DispatcherPriority.Background);
            return await RunOnStaThreadAsync(operation);
        }
        finally
        {
            onBusyChanged?.Invoke(false);
        }
    }

    private static async Task<T> RunTaskWithBusyIndicatorAsync<T>(
        Func<Task<T>> operation,
        Action<bool>? onBusyChanged)
    {
        onBusyChanged?.Invoke(true);
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            return await operation();
        }
        finally
        {
            onBusyChanged?.Invoke(false);
        }
    }

    internal static Task<T> RunOnStaThreadAsync<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(operation());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Direct2dCad print worker"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static Task<CadPrintSubmission> StartPrintJobAsync(
        CadPrintRequest request,
        CadRectD renderBounds,
        CadPrintPreviewSelection selection)
    {
        var started = new TaskCompletionSource<CadPrintSubmission>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var writingCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            Dispatcher? workerDispatcher = null;
            try
            {
                using var printServer = new LocalPrintServer();
                using var printQueue = printServer.GetPrintQueue(selection.QueueName);
                var baseTicket = printQueue.DefaultPrintTicket ??
                                 printQueue.UserPrintTicket ??
                                 new PrintTicket();
                var requestedTicket = baseTicket.Clone();
                requestedTicket.PageMediaSize = selection.MediaSize;
                requestedTicket.PageOrientation = selection.Orientation;
                requestedTicket.CopyCount = selection.Copies;

                var printTicket = printQueue
                    .MergeAndValidatePrintTicket(baseTicket, requestedTicket)
                    .ValidatedPrintTicket;
                var page = ResolvePageMetrics(printQueue, printTicket, renderBounds);
                var viewport = CreateViewport(renderBounds, page.PixelWidth, page.PixelHeight);
                var frame = Direct2DOffscreenRenderer.Render(
                    request.Document,
                    viewport,
                    request.RenderOptions,
                    page.PixelWidth,
                    page.PixelHeight,
                    request.OleDrawCallback);
                var bitmap = CreateBitmap(frame, page.OutputWidth, page.OutputHeight);
                var visual = CreatePrintVisual(bitmap, page);
                printQueue.CurrentJobSettings.Description = request.DocumentName;
                printQueue.CurrentJobSettings.CurrentPrintTicket = printTicket;

                var writer = PrintQueue.CreateXpsDocumentWriter(printQueue);
                workerDispatcher = Dispatcher.CurrentDispatcher;
                void HandleWritingCompleted(object? sender, WritingCompletedEventArgs args)
                {
                    writer.WritingCompleted -= HandleWritingCompleted;
                    if (args.Cancelled)
                        writingCompletion.TrySetCanceled();
                    else if (args.Error is not null)
                        writingCompletion.TrySetException(args.Error);
                    else
                        writingCompletion.TrySetResult();

                    workerDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }

                writer.WritingCompleted += HandleWritingCompleted;
                writer.WriteAsync(visual, printTicket);
                started.TrySetResult(new CadPrintSubmission(writingCompletion.Task));

                // Keep the owning STA and its WPF objects alive until the native
                // WritingCompleted callback is delivered. No timer or polling is used.
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                if (!started.TrySetException(ex))
                    writingCompletion.TrySetException(ex);
                workerDispatcher?.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        })
        {
            IsBackground = true,
            Name = "Direct2dCad XPS print writer"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return started.Task;
    }

    private static async Task NotifyWhenPrintCompletesAsync(
        CadPrintSubmission submission,
        Action? onPrintCompleted,
        Dispatcher? dispatcher)
    {
        try
        {
            await submission.WritingCompletion.ConfigureAwait(false);
            if (onPrintCompleted is null ||
                dispatcher is null ||
                dispatcher.HasShutdownStarted)
                return;

            await dispatcher.InvokeAsync(onPrintCompleted);
        }
        catch (Exception)
        {
            // A canceled or failed asynchronous write must not produce a successful
            // completion notification or an unobserved application exception.
        }
    }

    private static IReadOnlyList<CadPrinterChoice> GetInstalledPrinters()
    {
        using var printServer = new LocalPrintServer();
        string? defaultQueueName = null;
        try
        {
            using var defaultQueue = LocalPrintServer.GetDefaultPrintQueue();
            defaultQueueName = defaultQueue.Name;
        }
        catch (PrintSystemException)
        {
            // A default printer is optional; the first available queue is used.
        }

        var printers = new List<CadPrinterChoice>();
        using var queues = printServer.GetPrintQueues();
        foreach (var queue in queues)
        {
            using (queue)
            {
                try
                {
                    var defaultTicket = queue.DefaultPrintTicket ?? queue.UserPrintTicket ?? new PrintTicket();
                    var capabilities = queue.GetPrintCapabilities(defaultTicket);
                    var paperSizes = CreatePaperSizeChoices(capabilities, defaultTicket);
                    if (paperSizes.Count == 0)
                        continue;

                    printers.Add(new CadPrinterChoice(
                        queue.Name,
                        queue.FullName,
                        string.Equals(queue.Name, defaultQueueName, StringComparison.OrdinalIgnoreCase),
                        paperSizes));
                }
                catch (PrintSystemException)
                {
                    // An unavailable/offline queue must not block the other printers.
                }
            }
        }

        return printers
            .OrderByDescending(printer => printer.IsDefault)
            .ThenBy(printer => printer.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<CadPaperSizeChoice> CreatePaperSizeChoices(
        PrintCapabilities capabilities,
        PrintTicket defaultTicket)
    {
        var defaultSize = defaultTicket.PageMediaSize;
        return capabilities.PageMediaSizeCapability
            .Where(size => size.Width is > 0 && size.Height is > 0)
            .Select(size => new CadPaperSizeChoice(
                size,
                FormatPaperSize(size),
                size.Width!.Value,
                size.Height!.Value,
                IsSamePaperSize(size, defaultSize)))
            .DistinctBy(size => (size.MediaSize.PageMediaSizeName, size.Width, size.Height))
            .ToArray();
    }

    private static bool IsSamePaperSize(PageMediaSize candidate, PageMediaSize? expected)
    {
        if (expected is null)
            return false;
        if (candidate.PageMediaSizeName is not null && candidate.PageMediaSizeName == expected.PageMediaSizeName)
            return true;

        return candidate.Width is { } candidateWidth &&
               candidate.Height is { } candidateHeight &&
               expected.Width is { } expectedWidth &&
               expected.Height is { } expectedHeight &&
               Math.Abs(candidateWidth - expectedWidth) < 0.5 &&
               Math.Abs(candidateHeight - expectedHeight) < 0.5;
    }

    private static string FormatPaperSize(PageMediaSize size)
    {
        var name = size.PageMediaSizeName?.ToString() ?? Strings.CustomPaperSize;
        if (size.Width is not { } width || size.Height is not { } height)
            return name;

        const double millimetersPerDip = 25.4 / 96.0;
        return $"{name}  ({width * millimetersPerDip:0.#} × {height * millimetersPerDip:0.#} mm)";
    }

    private static BitmapSource CreatePreviewBitmap(
        CadPrintRequest request,
        CadRectD renderBounds)
    {
        var (width, height) = ResolvePreviewPixelSize(renderBounds);
        var viewport = CreateViewport(renderBounds, width, height);
        var frame = Direct2DOffscreenRenderer.Render(
            request.Document,
            viewport,
            request.RenderOptions,
            width,
            height,
            request.OleDrawCallback);
        return CreateBitmap(frame, width, height);
    }

    internal static (int Width, int Height) ResolvePreviewPixelSize(CadRectD bounds)
    {
        var maximumSide = Math.Max(bounds.Width, bounds.Height);
        if (!(maximumSide > 0) || !double.IsFinite(maximumSide))
            return (1, 1);

        var scale = MaximumPreviewPixelSide / maximumSide;
        return (
            Math.Max(1, (int)Math.Round(bounds.Width * scale)),
            Math.Max(1, (int)Math.Round(bounds.Height * scale)));
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
        PrintQueue printQueue,
        PrintTicket printTicket,
        CadRectD renderBounds)
    {
        var capabilities = printQueue.GetPrintCapabilities(printTicket);
        var imageableArea = capabilities.PageImageableArea;
        var pageWidth = PositiveOrFallback(
            printTicket.PageMediaSize?.Width ?? double.NaN,
            DefaultPageWidth);
        var pageHeight = PositiveOrFallback(
            printTicket.PageMediaSize?.Height ?? double.NaN,
            DefaultPageHeight);
        if (printTicket.PageOrientation == PageOrientation.Landscape && pageHeight > pageWidth ||
            printTicket.PageOrientation == PageOrientation.Portrait && pageWidth > pageHeight)
        {
            (pageWidth, pageHeight) = (pageHeight, pageWidth);
        }

        var printableX = PositiveOrZero(imageableArea?.OriginWidth ?? 0.0);
        var printableY = PositiveOrZero(imageableArea?.OriginHeight ?? 0.0);
        var printableWidth = PositiveOrFallback(imageableArea?.ExtentWidth ?? double.NaN, pageWidth);
        var printableHeight = PositiveOrFallback(imageableArea?.ExtentHeight ?? double.NaN, pageHeight);

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
            printableX + (printableWidth - outputWidth) * 0.5,
            printableY + (printableHeight - outputHeight) * 0.5,
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

    private static double PositiveOrZero(double value) =>
        value >= 0 && double.IsFinite(value) ? value : 0.0;

    private sealed record CadPrintPreparation(
        IReadOnlyList<CadPrinterChoice> Printers,
        BitmapSource Preview);

    private sealed record CadPrintSubmission(Task WritingCompletion);

    private sealed record CadPrintPageMetrics(
        double OutputX,
        double OutputY,
        double OutputWidth,
        double OutputHeight,
        int PixelWidth,
        int PixelHeight);
}
