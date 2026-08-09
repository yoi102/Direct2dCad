using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.wpf.Services.Importing;

internal sealed class ImageImportService : IImageImportService
{
    public CadImageImportData LoadFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));

        using var stream = File.OpenRead(filePath);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.Count > 0
            ? decoder.Frames[0]
            : throw new InvalidDataException("Image file contains no frames.");

        return CreateImportData(
            frame,
            ResolveRawImageContentType(filePath),
            Path.GetFileName(filePath));
    }

    public CadImageImportData? LoadFromClipboard()
    {
        if (!Clipboard.ContainsImage())
            return null;

        var image = Clipboard.GetImage();
        return image is null
            ? null
            : CreateImportData(image, "image/bgra32", "Clipboard Image");
    }

    public string CreatePngDataUrl(CadImageImportData image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var topDownPixels = FlipRows(image.Pixels, image.Stride, image.PixelHeight);
        var bitmap = BitmapSource.Create(
            image.PixelWidth,
            image.PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32,
            null,
            topDownPixels,
            image.Stride);
        bitmap.Freeze();

        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
    }

    private static CadImageImportData CreateImportData(
        BitmapSource source,
        string contentType,
        string sourceName)
    {
        var converted = source.Format == PixelFormats.Pbgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);

        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        converted.CopyPixels(pixels, stride, 0);

        return new CadImageImportData(
            width,
            height,
            stride,
            FlipRows(pixels, stride, height),
            contentType,
            sourceName);
    }

    private static byte[] FlipRows(byte[] pixels, int stride, int height)
    {
        var flipped = new byte[pixels.Length];
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(
                pixels,
                y * stride,
                flipped,
                (height - 1 - y) * stride,
                stride);
        }

        return flipped;
    }

    private static string ResolveRawImageContentType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/bgra32;source=image/jpeg",
            ".bmp" => "image/bgra32;source=image/bmp",
            ".gif" => "image/bgra32;source=image/gif",
            ".tif" or ".tiff" => "image/bgra32;source=image/tiff",
            ".webp" => "image/bgra32;source=image/webp",
            _ => "image/bgra32;source=image/png"
        };
    }
}
