using Avalonia.Media.Imaging;

namespace Direct2dCad.Avalonia;

/// <summary>Smoke-test captures are PNG evidence files; uses the Avalonia 12.1 encoder-options overload.</summary>
internal static class NativeSmokeImaging
{
    public static void SavePng(this Bitmap bitmap, string fileName) =>
        bitmap.Save(fileName, new PngBitmapEncoderOptions());
}
