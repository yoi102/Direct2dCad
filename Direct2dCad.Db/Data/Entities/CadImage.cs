using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadImage : CadEntity
{
    private CadRectD _bounds;
    private CadRectD _rotatedBounds;
    private byte[] _pixels;

    public override CadRectD Bounds => _rotatedBounds;

    public CadRectD FrameBounds => _bounds;

    public int PixelWidth { get; private set; }

    public int PixelHeight { get; private set; }

    public int Stride { get; private set; }

    public string ContentType { get; private set; }

    public string SourceName { get; private set; }

    public double Opacity { get; private set; }

    public double RotationRadians { get; private set; }

    public IReadOnlyList<byte> Pixels => _pixels;

    public ReadOnlyMemory<byte> PixelMemory => _pixels;

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
        string name = "",
        double opacity = 1.0,
        double rotationRadians = 0.0)
        : base(id, layerId, ownerBlockId, name)
    {
        _bounds = GuardBounds(bounds);
        PixelWidth = GuardPixelSize(pixelWidth, nameof(pixelWidth));
        PixelHeight = GuardPixelSize(pixelHeight, nameof(pixelHeight));
        Stride = GuardStride(stride, PixelWidth);
        _pixels = GuardPixels(pixels, Stride, PixelHeight);
        ContentType = NormalizeContentType(contentType);
        SourceName = sourceName ?? string.Empty;
        Opacity = GuardOpacity(opacity);
        RotationRadians = GuardFinite(rotationRadians, nameof(rotationRadians));
        _rotatedBounds = CalculateRotatedBounds();
    }

    public void SetBounds(CadRectD bounds)
    {
        _bounds = GuardBounds(bounds);
        _rotatedBounds = CalculateRotatedBounds();
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

    public void SetOpacity(double opacity)
    {
        Opacity = GuardOpacity(opacity);
    }

    public void SetRotation(double rotationRadians)
    {
        RotationRadians = GuardFinite(rotationRadians, nameof(rotationRadians));
        _rotatedBounds = CalculateRotatedBounds();
    }

    public CadPointD FrameToWorld(CadPointD point)
    {
        return RotateAround(point, _bounds.Center, RotationRadians);
    }

    public CadPointD WorldToFrame(CadPointD point)
    {
        return RotateAround(point, _bounds.Center, -RotationRadians);
    }

    public IReadOnlyList<CadPointD> GetFrameCorners()
    {
        return
        [
            FrameToWorld(new CadPointD(_bounds.MinX, _bounds.MinY)),
            FrameToWorld(new CadPointD(_bounds.MaxX, _bounds.MinY)),
            FrameToWorld(new CadPointD(_bounds.MaxX, _bounds.MaxY)),
            FrameToWorld(new CadPointD(_bounds.MinX, _bounds.MaxY))
        ];
    }

    private static double GuardOpacity(double opacity)
    {
        if (double.IsNaN(opacity) || double.IsInfinity(opacity))
            throw new ArgumentOutOfRangeException(nameof(opacity));

        return Math.Clamp(opacity, 0.0, 1.0);
    }

    private CadRectD CalculateRotatedBounds()
    {
        if (Math.Abs(RotationRadians) <= 1e-12)
            return _bounds;

        var bounds = CadRectD.Empty;
        foreach (var corner in GetFrameCorners())
            bounds = bounds.ExpandToInclude(corner);

        return bounds;
    }

    private static CadPointD RotateAround(CadPointD point, CadPointD center, double angleRadians)
    {
        if (Math.Abs(angleRadians) <= 1e-12)
            return point;

        var cos = Math.Cos(angleRadians);
        var sin = Math.Sin(angleRadians);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        return new CadPointD(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }

    private static double GuardFinite(double value, string paramName)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? throw new ArgumentOutOfRangeException(paramName)
            : value;
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
