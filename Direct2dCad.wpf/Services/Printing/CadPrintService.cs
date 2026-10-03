using System.Printing;
using System.IO;
using System.Windows;
using System.Windows.Documents.Serialization;
using System.Windows.Media;
using System.Windows.Threading;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang.Strings;
using Direct2dCad.ViewModels.Services.Platform.Printing;
using Direct2dCad.wpf.Services.Printing.Vector;
using Direct2dCad.wpf.Views.Dialogs;

namespace Direct2dCad.wpf.Services.Printing;

public sealed class CadPrintService : ICadPrintService
{
    internal const int DefaultRenderDpi = 300;
    internal const int MinimumRenderDpi = 72;
    internal const int MaximumRenderDpi = 1200;
    private const int PreviewEmbeddedRasterDpi = 150;
    private const double DefaultPageWidth = 816.0;
    private const double DefaultPageHeight = 1056.0;

    public async Task<bool> PrintAsync(
        CadPrintRequest request,
        Action? onPrintStarted = null,
        Action<bool>? onBusyChanged = null,
        Action? onPrintCompleted = null,
        Action<CadPrintCompletion>? onPrintFinished = null)
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
            initialOrientation,
            request)
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
            onPrintFinished,
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
            CreatePreviewImage(request));
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
                using var ticketStream=new MemoryStream(selection.ValidatedTicket ?? throw new InvalidOperationException("Print preview must be validated first."));
                var printTicket=new PrintTicket(ticketStream);
                var visual=CreatePageVisual(ResolveSelectedRequest(request,selection),selection,false);
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
        Action<CadPrintCompletion>? onPrintFinished,
        Dispatcher? dispatcher)
    {
        try
        {
            var result = await CadPrintCompletion.ObserveAsync(submission.WritingCompletion).ConfigureAwait(false);
            if (dispatcher is null || dispatcher.HasShutdownStarted)
                return;
            await dispatcher.InvokeAsync(() =>
            {
                if (result.Status == CadPrintCompletionStatus.Completed)
                    onPrintCompleted?.Invoke();
                onPrintFinished?.Invoke(result);
            });
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

    internal static ImageSource CreatePreviewImage(CadPrintRequest request)
    {
        var layout = request.Document.GetLayout(request.ActiveLayoutId);
        var visual = CadVectorPrintRenderer.CreateVisual(
            request,
            layout,
            new Rect(0, 0, request.PaperBounds.Width, request.PaperBounds.Height),
            PreviewEmbeddedRasterDpi);
        var drawing = VisualTreeHelper.GetDrawing(visual)?.CloneCurrentValue() ??
                      throw new InvalidOperationException("The print preview contains no drawing.");
        drawing.Freeze();
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    internal static CadPrintRequest ResolveSelectedRequest(CadPrintRequest request,CadPrintPreviewSelection selection) =>
        request.IsModelSpace && selection.UseCurrentView && request.CurrentViewBounds is { } bounds
            ? request with {PaperBounds=bounds} : request;

    internal static Task<CadValidatedPrintPreview> ValidatePreviewAsync(CadPrintRequest request,CadPrintPreviewSelection selection) => RunOnStaThreadAsync(()=>
    {
        using var server=new LocalPrintServer();using var queue=server.GetPrintQueue(selection.QueueName);
        var baseline=queue.DefaultPrintTicket??queue.UserPrintTicket??new PrintTicket();var requested=baseline.Clone();
        requested.PageMediaSize=selection.MediaSize;requested.PageOrientation=selection.Orientation;requested.CopyCount=selection.Copies;
        requested.PageResolution=new PageResolution(selection.RenderDpi,selection.RenderDpi);
        var ticket=queue.MergeAndValidatePrintTicket(baseline,requested).ValidatedPrintTicket;
        var caps=queue.GetPrintCapabilities(ticket);var area=caps.PageImageableArea;
        var width=PositiveOrFallback(ticket.PageMediaSize?.Width??double.NaN,DefaultPageWidth);
        var height=PositiveOrFallback(ticket.PageMediaSize?.Height??double.NaN,DefaultPageHeight);
        if(ticket.PageOrientation==PageOrientation.Landscape && height>width || ticket.PageOrientation==PageOrientation.Portrait && width>height)(width,height)=(height,width);
        var printable=CadRectD.FromXYWH(PositiveOrZero(area?.OriginWidth??0),PositiveOrZero(area?.OriginHeight??0),
            PositiveOrFallback(area?.ExtentWidth??double.NaN,width),PositiveOrFallback(area?.ExtentHeight??double.NaN,height));
        var placement=CadPrintPlacement.Calculate(ResolveSelectedRequest(request,selection).PaperBounds,printable,selection.Scaling,selection.Percent);
        using var bytes=new MemoryStream();ticket.GetXmlStream().CopyTo(bytes);
        var validated=selection with {ValidatedTicket=bytes.ToArray(),Placement=placement,PageWidth=width,PageHeight=height};
        var visual=CreatePageVisual(ResolveSelectedRequest(request,validated),validated,true);
        var drawing=VisualTreeHelper.GetDrawing(visual)!.CloneCurrentValue();drawing.Freeze();var image=new DrawingImage(drawing);image.Freeze();
        return new CadValidatedPrintPreview(validated,image);
    });

    private static DrawingVisual CreatePageVisual(CadPrintRequest request,CadPrintPreviewSelection selection,bool preview)
    {
        var placement=selection.Placement??throw new InvalidOperationException("Missing print placement.");
        var source=CadVectorPrintRenderer.CreateVisual(request,request.Document.GetLayout(request.ActiveLayoutId),
            new Rect(placement.Output.MinX,placement.Output.MinY,placement.Output.Width,placement.Output.Height),selection.RenderDpi);
        var visual=new DrawingVisual();using var context=visual.RenderOpen();
        context.DrawRectangle(Brushes.White,null,new Rect(0,0,selection.PageWidth,selection.PageHeight));
        var clip=new Rect(placement.Printable.MinX,placement.Printable.MinY,placement.Printable.Width,placement.Printable.Height);
        if(preview)
        {
            context.DrawDrawing(VisualTreeHelper.GetDrawing(source));
            var page=new Rect(0,0,selection.PageWidth,selection.PageHeight);
            var previewBounds=Rect.Union(page,new Rect(placement.Output.MinX,placement.Output.MinY,placement.Output.Width,placement.Output.Height));
            var outside=Geometry.Combine(new RectangleGeometry(previewBounds),new RectangleGeometry(clip),GeometryCombineMode.Exclude,null);
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(45,205,92,92)),null,outside);
            context.DrawRectangle(null,new Pen(Brushes.SlateGray,.6),page);
            context.DrawRectangle(null,new Pen(Brushes.IndianRed,.8){DashStyle=DashStyles.Dash},clip);
        }
        else {context.PushClip(new RectangleGeometry(clip));context.DrawDrawing(VisualTreeHelper.GetDrawing(source));context.Pop();}
        return visual;
    }

    private static CadRectD ResolveRenderBounds(CadPrintRequest request)
    {
        request.Document.GetLayout(request.ActiveLayoutId);
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

        return new CadPrintPageMetrics(
            printableX + (printableWidth - outputWidth) * 0.5,
            printableY + (printableHeight - outputHeight) * 0.5,
            outputWidth,
            outputHeight);
    }

    private static double PositiveOrFallback(double value, double fallback) =>
        value > 0 && double.IsFinite(value) ? value : fallback;

    private static double PositiveOrZero(double value) =>
        value >= 0 && double.IsFinite(value) ? value : 0.0;

    private sealed record CadPrintPreparation(
        IReadOnlyList<CadPrinterChoice> Printers,
        ImageSource Preview);

    private sealed record CadPrintSubmission(Task WritingCompletion);

    private sealed record CadPrintPageMetrics(
        double OutputX,
        double OutputY,
        double OutputWidth,
        double OutputHeight);
}
