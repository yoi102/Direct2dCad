using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Direct2dCad.Application.Tools;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Services.Importing;

internal sealed class CadViewCaptureService : ICadViewCaptureService
{
    public Task<CadToolImage> CaptureAsync(CadViewCaptureRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Width is < 1 or > 1024 || request.Height is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(request));
        var completion = new TaskCompletionSource<CadToolImage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = Direct2DOffscreenRenderer.Render(request.Document, request.Viewport, request.Options,
                    request.Width, request.Height, request.OleDrawCallback);
                cancellationToken.ThrowIfCancellationRequested();
                BitmapSource bitmap = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96,
                    PixelFormats.Pbgra32, null, frame.Pixels, frame.Stride);
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = new MemoryStream();
                    encoder.Save(stream);
                    if (stream.Length <= AiToolResultContent.MaximumImageBytes)
                    {
                        completion.TrySetResult(new(stream.ToArray(), "image/png", bitmap.PixelWidth, bitmap.PixelHeight));
                        break;
                    }
                    bitmap = new TransformedBitmap(bitmap, new ScaleTransform(0.75, 0.75));
                }
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }) { IsBackground = true, Name = "CAD view capture" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
