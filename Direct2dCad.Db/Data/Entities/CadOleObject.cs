using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadOleObject : CadEntity
{
    private CadRectD _bounds;
    private byte[] _pixels;
    private byte[] _oleBytes;

    public override CadRectD Bounds => _bounds;

    public int PixelWidth { get; private set; }

    public int PixelHeight { get; private set; }

    public int Stride { get; private set; }

    public string ContentType { get; private set; }

    public string SourceName { get; private set; }

    public IReadOnlyList<byte> Pixels => _pixels;

    public IReadOnlyList<byte> OleBytes => _oleBytes;

    internal CadOleObject(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        CadRectD bounds,
        int pixelWidth,
        int pixelHeight,
        int stride,
        byte[] pixels,
        byte[] oleBytes,
        string contentType = "application/x-ole-storage",
        string sourceName = "",
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        _bounds = GuardBounds(bounds);
        PixelWidth = GuardPixelSize(pixelWidth, nameof(pixelWidth));
        PixelHeight = GuardPixelSize(pixelHeight, nameof(pixelHeight));
        Stride = GuardStride(stride, PixelWidth);
        _pixels = GuardBytes(pixels, checked(Stride * PixelHeight), nameof(pixels));
        _oleBytes = GuardBytes(oleBytes, 1, nameof(oleBytes));
        ContentType = NormalizeContentType(contentType);
        SourceName = sourceName ?? string.Empty;
    }

    public void SetBounds(CadRectD bounds)
    {
        _bounds = GuardBounds(bounds);
    }

    public void SetOleData(
        int pixelWidth,
        int pixelHeight,
        int stride,
        byte[] pixels,
        byte[] oleBytes,
        string contentType = "application/x-ole-storage",
        string sourceName = "")
    {
        PixelWidth = GuardPixelSize(pixelWidth, nameof(pixelWidth));
        PixelHeight = GuardPixelSize(pixelHeight, nameof(pixelHeight));
        Stride = GuardStride(stride, PixelWidth);
        _pixels = GuardBytes(pixels, checked(Stride * PixelHeight), nameof(pixels));
        _oleBytes = GuardBytes(oleBytes, 1, nameof(oleBytes));
        ContentType = NormalizeContentType(contentType);
        SourceName = sourceName ?? string.Empty;
    }

    public byte[] CopyPixels()
    {
        return (byte[])_pixels.Clone();
    }

    public byte[] CopyOleBytes()
    {
        return (byte[])_oleBytes.Clone();
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

    private static byte[] GuardBytes(byte[] bytes, int minimumLength, string paramName)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < minimumLength)
            throw new ArgumentException("Data is shorter than expected.", paramName);

        return (byte[])bytes.Clone();
    }

    private static string NormalizeContentType(string? contentType)
    {
        return string.IsNullOrWhiteSpace(contentType)
            ? "application/x-ole-storage"
            : contentType.Trim();
    }
}
