using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadImage : CadEntity
{
    private CadRectD _bounds;
    private byte[] _pixels;

    public override CadRectD Bounds => _bounds;

    public int PixelWidth { get; private set; }

    public int PixelHeight { get; private set; }

    public int Stride { get; private set; }

    public string ContentType { get; private set; }

    public string SourceName { get; private set; }

    public IReadOnlyList<byte> Pixels => _pixels;

    internal CadImage(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        CadRectD bounds,
        int pixelWidth,
        int pixelHeight,
        int stride,
        byte[] pixels,
        string contentType = "image/bgra32",
        string sourceName = "",
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        _bounds = GuardBounds(bounds);
        PixelWidth = GuardPixelSize(pixelWidth, nameof(pixelWidth));
        PixelHeight = GuardPixelSize(pixelHeight, nameof(pixelHeight));
        Stride = GuardStride(stride, PixelWidth);
        _pixels = GuardPixels(pixels, Stride, PixelHeight);
        ContentType = NormalizeContentType(contentType);
        SourceName = sourceName ?? string.Empty;
    }

    public void SetBounds(CadRectD bounds)
    {
        _bounds = GuardBounds(bounds);
    }

    public void SetImageData(
        int pixelWidth,
        int pixelHeight,
        int stride,
        byte[] pixels,
        string contentType = "image/bgra32",
        string sourceName = "")
    {
        PixelWidth = GuardPixelSize(pixelWidth, nameof(pixelWidth));
        PixelHeight = GuardPixelSize(pixelHeight, nameof(pixelHeight));
        Stride = GuardStride(stride, PixelWidth);
        _pixels = GuardPixels(pixels, Stride, PixelHeight);
        ContentType = NormalizeContentType(contentType);
        SourceName = sourceName ?? string.Empty;
    }

    public byte[] CopyPixels()
    {
        return (byte[])_pixels.Clone();
    }

    private static CadRectD GuardBounds(CadRectD bounds)
    {
        return bounds.IsEmpty ||
               bounds.Width <= 0 ||
               bounds.Height <= 0 ||
               double.IsNaN(bounds.Width) ||
               double.IsNaN(bounds.Height) ||
               double.IsInfinity(bounds.Width) ||
               double.IsInfinity(bounds.Height)
            ? throw new ArgumentOutOfRangeException(nameof(bounds))
            : bounds;
    }

    private static int GuardPixelSize(int value, string paramName)
    {
        return value <= 0 ? throw new ArgumentOutOfRangeException(paramName) : value;
    }

    private static int GuardStride(int stride, int pixelWidth)
    {
        var minimumStride = checked(pixelWidth * 4);
        return stride < minimumStride ? throw new ArgumentOutOfRangeException(nameof(stride)) : stride;
    }

    private static byte[] GuardPixels(byte[] pixels, int stride, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        var expectedLength = checked(stride * pixelHeight);
        if (pixels.Length < expectedLength)
            throw new ArgumentException("Pixel data is shorter than stride * height.", nameof(pixels));

        return (byte[])pixels.Clone();
    }

    private static string NormalizeContentType(string? contentType)
    {
        return string.IsNullOrWhiteSpace(contentType)
            ? "image/bgra32"
            : contentType.Trim();
    }
}
