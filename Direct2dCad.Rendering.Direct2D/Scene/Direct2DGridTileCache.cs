using System.Numerics;
using System.Runtime.InteropServices;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DXGIFormat = Vortice.DXGI.Format;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal sealed class Direct2DGridTileCache : IDisposable
{
    private const int MaximumEntries = 8;
    private const int MaximumTilePixelSide = 1024;
    private readonly Dictionary<GridTileKey, Entry> _entries = [];
    private ID2D1DeviceContext? _deviceContext;
    private long _usageStamp;
    private bool _disposed;

    public bool TryDraw(
        ID2D1DeviceContext context,
        CadGridType gridType,
        CadRectD bounds,
        CadPointD origin,
        double spacingX,
        double spacingY,
        double majorX,
        double majorY,
        double zoom,
        CadColor minorColor,
        CadColor majorColor,
        float minorScreenStroke,
        float majorScreenStroke,
        double minorDotScreenSize,
        double majorDotScreenSize,
        bool isAliased)
    {
        ThrowIfDisposed();
        if (gridType is not (CadGridType.Dots or CadGridType.Cross) ||
            bounds.IsEmpty ||
            !IsPositiveFinite(spacingX) ||
            !IsPositiveFinite(spacingY) ||
            !IsPositiveFinite(majorX) ||
            !IsPositiveFinite(majorY) ||
            !IsPositiveFinite(zoom))
        {
            return false;
        }

        EnsureDeviceContext(context);
        var columns = ResolvePeriodCount(majorX, spacingX);
        var rows = ResolvePeriodCount(majorY, spacingY);
        if (columns <= 0 || rows <= 0)
            return false;

        var pixelWidth = (int)Math.Ceiling(majorX * zoom);
        var pixelHeight = (int)Math.Ceiling(majorY * zoom);
        if (pixelWidth < 2 ||
            pixelHeight < 2 ||
            pixelWidth > MaximumTilePixelSide ||
            pixelHeight > MaximumTilePixelSide)
        {
            return false;
        }

        var key = new GridTileKey(
            gridType,
            columns,
            rows,
            pixelWidth,
            pixelHeight,
            minorColor,
            majorColor,
            BitConverter.SingleToInt32Bits(minorScreenStroke),
            BitConverter.SingleToInt32Bits(majorScreenStroke),
            BitConverter.DoubleToInt64Bits(minorDotScreenSize),
            BitConverter.DoubleToInt64Bits(majorDotScreenSize),
            isAliased);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = CreateEntry(
                context,
                key,
                minorScreenStroke,
                majorScreenStroke,
                minorDotScreenSize,
                majorDotScreenSize);
            _entries.Add(key, entry);
            TrimEntries(key);
        }

        entry.LastUsed = ++_usageStamp;
        entry.Brush.Transform =
            Matrix3x2.CreateScale(
                (float)(majorX / pixelWidth),
                (float)(majorY / pixelHeight)) *
            Matrix3x2.CreateTranslation((float)origin.X, (float)origin.Y);
        context.FillRectangle(
            new RawRectF(
                (float)bounds.MinX,
                (float)bounds.MinY,
                (float)bounds.MaxX,
                (float)bounds.MaxY),
            entry.Brush);
        return true;
    }

    public void Clear()
    {
        ThrowIfDisposed();
        foreach (var entry in _entries.Values)
            entry.Dispose();
        _entries.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Clear();
        _deviceContext = null;
        _disposed = true;
    }

    private static Entry CreateEntry(
        ID2D1DeviceContext context,
        GridTileKey key,
        float minorScreenStroke,
        float majorScreenStroke,
        double minorDotScreenSize,
        double majorDotScreenSize)
    {
        var pixels = Rasterize(
            key,
            minorScreenStroke,
            majorScreenStroke,
            minorDotScreenSize,
            majorDotScreenSize);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var bitmap = context.CreateBitmap(
                new SizeI(key.PixelWidth, key.PixelHeight),
                handle.AddrOfPinnedObject(),
                (uint)(key.PixelWidth * 4),
                new BitmapProperties1
                {
                    PixelFormat = new PixelFormat(
                        DXGIFormat.B8G8R8A8_UNorm,
                        AlphaMode.Premultiplied),
                    DpiX = 96.0f,
                    DpiY = 96.0f,
                    BitmapOptions = BitmapOptions.None
                });
            try
            {
                var brush = context.CreateBitmapBrush(
                    bitmap,
                    new BitmapBrushProperties1(
                        ExtendMode.Wrap,
                        ExtendMode.Wrap,
                        key.IsAliased ? InterpolationMode.NearestNeighbor : InterpolationMode.Linear),
                    new BrushProperties(1.0f, Matrix3x2.Identity));
                return new Entry(bitmap, brush);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }
        finally
        {
            handle.Free();
        }
    }

    private static byte[] Rasterize(
        GridTileKey key,
        float minorScreenStroke,
        float majorScreenStroke,
        double minorDotScreenSize,
        double majorDotScreenSize)
    {
        var pixels = new byte[checked(key.PixelWidth * key.PixelHeight * 4)];
        var stepX = (double)key.PixelWidth / key.Columns;
        var stepY = (double)key.PixelHeight / key.Rows;
        for (var column = 0; column < key.Columns; column++)
        {
            var centerX = column * stepX;
            for (var row = 0; row < key.Rows; row++)
            {
                var centerY = row * stepY;
                var major = column == 0 && row == 0;
                var color = major ? key.MajorColor : key.MinorColor;
                if (key.GridType == CadGridType.Dots)
                {
                    var size = major ? majorDotScreenSize : minorDotScreenSize;
                    DrawWrappedRectangle(
                        pixels,
                        key.PixelWidth,
                        key.PixelHeight,
                        centerX,
                        centerY,
                        Math.Max(size, double.Epsilon),
                        Math.Max(size, double.Epsilon),
                        color);
                    continue;
                }

                var stroke = Math.Max(
                    major ? majorScreenStroke : minorScreenStroke,
                    float.Epsilon);
                DrawWrappedRectangle(
                    pixels,
                    key.PixelWidth,
                    key.PixelHeight,
                    centerX,
                    centerY,
                    Math.Max(stepX * 0.24, stroke),
                    stroke,
                    color);
                DrawWrappedRectangle(
                    pixels,
                    key.PixelWidth,
                    key.PixelHeight,
                    centerX,
                    centerY,
                    stroke,
                    Math.Max(stepY * 0.24, stroke),
                    color);
            }
        }

        return pixels;
    }

    private static void DrawWrappedRectangle(
        byte[] pixels,
        int width,
        int height,
        double centerX,
        double centerY,
        double rectangleWidth,
        double rectangleHeight,
        CadColor color)
    {
        var left = centerX - rectangleWidth * 0.5;
        var right = centerX + rectangleWidth * 0.5;
        var top = centerY - rectangleHeight * 0.5;
        var bottom = centerY + rectangleHeight * 0.5;
        var minX = (int)Math.Floor(left);
        var maxX = (int)Math.Ceiling(right) - 1;
        var minY = (int)Math.Floor(top);
        var maxY = (int)Math.Ceiling(bottom) - 1;
        for (var y = minY; y <= maxY; y++)
        for (var x = minX; x <= maxX; x++)
        {
            var coverageX = Math.Max(0.0, Math.Min(right, x + 1.0) - Math.Max(left, x));
            var coverageY = Math.Max(0.0, Math.Min(bottom, y + 1.0) - Math.Max(top, y));
            var coverage = Math.Clamp(coverageX * coverageY, 0.0, 1.0);
            if (coverage <= 0.0)
                continue;
            var wrappedX = PositiveModulo(x, width);
            var wrappedY = PositiveModulo(y, height);
            WritePremultipliedPixel(
                pixels,
                (wrappedY * width + wrappedX) * 4,
                color,
                coverage);
        }
    }

    private static void WritePremultipliedPixel(
        byte[] pixels,
        int offset,
        CadColor color,
        double coverage)
    {
        var effectiveAlpha = (byte)Math.Clamp(
            (int)Math.Round(color.A * coverage, MidpointRounding.AwayFromZero),
            0,
            byte.MaxValue);
        if (effectiveAlpha <= pixels[offset + 3])
            return;

        var alpha = effectiveAlpha / 255.0;
        pixels[offset] = (byte)Math.Round(color.B * alpha, MidpointRounding.AwayFromZero);
        pixels[offset + 1] = (byte)Math.Round(color.G * alpha, MidpointRounding.AwayFromZero);
        pixels[offset + 2] = (byte)Math.Round(color.R * alpha, MidpointRounding.AwayFromZero);
        pixels[offset + 3] = effectiveAlpha;
    }

    private void EnsureDeviceContext(ID2D1DeviceContext context)
    {
        if (ReferenceEquals(_deviceContext, context))
            return;
        Clear();
        _deviceContext = context;
    }

    private void TrimEntries(GridTileKey protectedKey)
    {
        while (_entries.Count > MaximumEntries)
        {
            var oldest = _entries
                .Where(pair => !pair.Key.Equals(protectedKey))
                .MinBy(static pair => pair.Value.LastUsed);
            oldest.Value.Dispose();
            _entries.Remove(oldest.Key);
        }
    }

    private static int ResolvePeriodCount(double majorSpacing, double minorSpacing)
    {
        var ratio = majorSpacing / minorSpacing;
        var rounded = (int)Math.Round(ratio, MidpointRounding.AwayFromZero);
        return rounded > 0 && Math.Abs(ratio - rounded) <= 1e-6 ? rounded : 0;
    }

    private static int PositiveModulo(int value, int divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static bool IsPositiveFinite(double value) => value > 0 && double.IsFinite(value);

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DGridTileCache));
    }

    private readonly record struct GridTileKey(
        CadGridType GridType,
        int Columns,
        int Rows,
        int PixelWidth,
        int PixelHeight,
        CadColor MinorColor,
        CadColor MajorColor,
        int MinorScreenStrokeBits,
        int MajorScreenStrokeBits,
        long MinorDotScreenSizeBits,
        long MajorDotScreenSizeBits,
        bool IsAliased);

    private sealed class Entry(ID2D1Bitmap bitmap, ID2D1BitmapBrush brush) : IDisposable
    {
        public ID2D1Bitmap Bitmap { get; } = bitmap;
        public ID2D1BitmapBrush Brush { get; } = brush;
        public long LastUsed { get; set; }

        public void Dispose()
        {
            Brush.Dispose();
            Bitmap.Dispose();
        }
    }
}
