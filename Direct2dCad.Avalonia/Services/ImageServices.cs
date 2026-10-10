using System.Runtime.InteropServices;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Application.Tools;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.ViewModels.Services.Platform;
using SkiaSharp;
using Direct2dCad.Application.Platform;

namespace Direct2dCad.Avalonia.Services;

internal sealed class ImageImportService : IImageImportService
{
    public CadImageImportData LoadFromFile(string path)
    {
        using var decoded = SKBitmap.Decode(path) ?? throw new InvalidDataException("Unsupported or damaged image.");
        using var bitmap = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap)) canvas.DrawBitmap(decoded, 0, 0);
        var stride = bitmap.Width * 4; var pixels = new byte[stride * bitmap.Height]; Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return new(bitmap.Width, bitmap.Height, stride, Flip(pixels, stride, bitmap.Height), "image/bgra32", Path.GetFileName(path));
    }
    public CadImageImportData? LoadFromClipboard()
    {
        if (!ClipboardTextService.OpenClipboard(App.OwnerHandle)) return null;
        try
        {
            var handle = ClipboardTextService.GetClipboardData(8); if (handle == 0) return null;
            var data = ClipboardTextService.GlobalLock(handle); if (data == 0) return null;
            try
            {
                var headerSize = Marshal.ReadInt32(data); var width = Marshal.ReadInt32(data, 4); var signedHeight = Marshal.ReadInt32(data, 8);
                var bits = Marshal.ReadInt16(data, 14); var compression = Marshal.ReadInt32(data, 16);
                if (width <= 0 || signedHeight == 0 || bits is not (24 or 32) || compression != 0) throw new InvalidDataException("Unsupported clipboard bitmap format.");
                var height = Math.Abs(signedHeight); if ((long)width * height > 64_000_000) throw new InvalidDataException("Clipboard image exceeds its budget.");
                var sourceStride = ((width * bits + 31) / 32) * 4; var source = new byte[sourceStride * height]; Marshal.Copy(data + headerSize, source, 0, source.Length);
                var pixels = new byte[width * height * 4];
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++) { var s = (signedHeight < 0 ? height - 1 - y : y) * sourceStride + x * bits / 8; var d = (y * width + x) * 4; pixels[d] = source[s]; pixels[d + 1] = source[s + 1]; pixels[d + 2] = source[s + 2]; pixels[d + 3] = 255; }
                return new(width, height, width * 4, pixels, "image/bgra32", "Clipboard image");
            }
            finally { ClipboardTextService.GlobalUnlock(handle); }
        }
        finally { ClipboardTextService.CloseClipboard(); }
    }
    internal static byte[] Flip(byte[] pixels, int stride, int height) { var result = new byte[pixels.Length]; for (var y = 0; y < height; y++) Buffer.BlockCopy(pixels, y * stride, result, (height - y - 1) * stride, stride); return result; }
    internal static byte[] Png(byte[] pixels, int width, int height, int stride)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        for (var y = 0; y < height; y++) Marshal.Copy(pixels, y * stride, bitmap.GetPixels() + y * bitmap.RowBytes, width * 4);
        using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100); return encoded.ToArray();
    }
    public string CreatePngDataUrl(CadImageImportData image) => "data:image/png;base64," + Convert.ToBase64String(Png(Flip(image.Pixels, image.Stride, image.PixelHeight), image.PixelWidth, image.PixelHeight, image.Stride));
}
internal sealed class ViewCaptureService : ICadViewCaptureService
{
    public Task<CadToolImage> CaptureAsync(CadViewCaptureRequest request, CancellationToken token)
    {
        if (request.Width is < 1 or > 1024 || request.Height is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(request));
        token.ThrowIfCancellationRequested();
        var frame = Direct2DOffscreenRenderer.Render(request.Document, request.Viewport, request.Options, request.Width, request.Height, request.OleDrawCallback);
        var png = ImageImportService.Png(frame.Pixels, frame.PixelWidth, frame.PixelHeight, frame.Stride);
        if (png.Length > AiToolResultContent.MaximumImageBytes) throw new InvalidOperationException("Captured image exceeds the provider image budget.");
        return Task.FromResult(new CadToolImage(png, "image/png", frame.PixelWidth, frame.PixelHeight));
    }
}
internal sealed class AiFileImportService(IImageImportService images) : IAiFileImportService
{
    public AiFileImportData Load(string path) { if (new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" }.Contains(Path.GetExtension(path).ToLowerInvariant())) { var image = images.LoadFromFile(path); return new(image.SourceName, image.ContentType, DataUrl: images.CreatePngDataUrl(image)); } return AiTextFileReader.Read(path); }
    public IReadOnlyList<AiFileImportData> LoadFilesFromClipboard()
    {
        var files = new List<AiFileImportData>(); if (!ClipboardTextService.OpenClipboard(App.OwnerHandle)) return files;
        try { var drop = ClipboardTextService.GetClipboardData(15); if (drop == 0) return files; var count = DragQueryFileW(drop, uint.MaxValue, 0, 0); for (uint i = 0; i < count; i++) { var length = DragQueryFileW(drop, i, 0, 0); var buffer = Marshal.AllocHGlobal(checked((int)(length + 1) * 2)); try { DragQueryFileW(drop, i, buffer, length + 1); files.Add(Load(Marshal.PtrToStringUni(buffer)!)); } finally { Marshal.FreeHGlobal(buffer); } } }
        finally { ClipboardTextService.CloseClipboard(); } return files;
    }
    [DllImport("shell32.dll", ExactSpelling = true)] private static extern uint DragQueryFileW(nint drop, uint index, nint buffer, uint length);
}

