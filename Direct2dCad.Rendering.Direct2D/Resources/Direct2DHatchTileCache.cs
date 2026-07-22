using System.Numerics;
using System.Runtime.InteropServices;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Scene;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Mathematics;
using DXGIFormat = Vortice.DXGI.Format;

namespace Direct2dCad.Rendering.Direct2D.Resources;

internal sealed class Direct2DHatchTileCache : IDisposable
{
    internal const long CacheBudgetBytes = 32L * 1024 * 1024;
    private const int MaximumTilePixelSide = 512;
    private const int MaximumPeriodMultiple = 16;
    private const double AxisTolerance = 1e-6;

    private readonly Dictionary<HatchTileKey, Entry> _entries = [];
    private readonly Direct2DRenderStatisticsCollector _statistics;
    private ID2D1DeviceContext? _deviceContext;
    private long _usageStamp;
    private long _estimatedBytes;
    private bool _disposed;

    public Direct2DHatchTileCache(
        Direct2DRenderStatisticsCollector statistics,
        ID2D1DeviceContext? deviceContext = null)
    {
        _statistics = statistics;
        _deviceContext = deviceContext;
    }

    public long EstimatedBytes => Math.Max(0, _estimatedBytes);

    public void Reset(ID2D1DeviceContext? deviceContext)
    {
        ThrowIfDisposed();
        Clear();
        _deviceContext = deviceContext;
    }

    public bool TryFill(
        ID2D1DeviceContext context,
        ID2D1Geometry geometry,
        CadRectD geometryBounds,
        CadTransientHatchFill hatchFill,
        double transformScaleMultiplier)
    {
        ThrowIfDisposed();
        if (_deviceContext is null ||
            !ReferenceEquals(_deviceContext, context) ||
            geometryBounds.IsEmpty ||
            hatchFill.Lines.Count == 0 ||
            !TryResolveTilePeriod(hatchFill, out var period))
        {
            return false;
        }

        var screenScale = Direct2DEntityLevelOfDetail.ResolveMaximumScreenScale(context.Transform) *
                          ResolveScaleMultiplier(transformScaleMultiplier);
        var scaleBucket = Direct2DRenderScaleBucket.Quantize(
            Math.Max(screenScale, double.Epsilon));
        var pixelWidth = (int)Math.Ceiling(period.X * scaleBucket);
        var pixelHeight = (int)Math.Ceiling(period.Y * scaleBucket);
        if (pixelWidth <= 0 ||
            pixelHeight <= 0 ||
            pixelWidth > MaximumTilePixelSide ||
            pixelHeight > MaximumTilePixelSide)
        {
            return false;
        }

        pixelWidth = Math.Max(pixelWidth, 2);
        pixelHeight = Math.Max(pixelHeight, 2);
        var key = new HatchTileKey(
            hatchFill.Lines,
            hatchFill.ForegroundColor,
            BitConverter.DoubleToInt64Bits(hatchFill.HatchScale),
            BitConverter.DoubleToInt64Bits(NormalizeDegrees(hatchFill.HatchAngle)),
            BitConverter.DoubleToInt64Bits(scaleBucket),
            pixelWidth,
            pixelHeight);
        if (!_entries.TryGetValue(key, out var entry))
        {
            entry = CreateEntry(context, hatchFill, period, scaleBucket, pixelWidth, pixelHeight);
            if (entry is null)
                return false;
            _entries.Add(key, entry);
            _estimatedBytes += entry.EstimatedBytes;
            TrimEntries(key);
        }

        entry.LastUsed = ++_usageStamp;
        var origin = new CadPointD(
            geometryBounds.MinX + hatchFill.HatchOrigin.X,
            geometryBounds.MaxY + hatchFill.HatchOrigin.Y);
        entry.Brush.Transform =
            Matrix3x2.CreateScale(
                (float)(period.X / pixelWidth),
                (float)(period.Y / pixelHeight)) *
            Matrix3x2.CreateTranslation((float)origin.X, (float)origin.Y);
        context.FillGeometry(geometry, entry.Brush);
        return true;
    }

    public void Clear()
    {
        ThrowIfDisposed();
        foreach (var entry in _entries.Values)
            entry.Dispose();
        _entries.Clear();
        _estimatedBytes = 0;
    }

    private static Entry? CreateEntry(
        ID2D1DeviceContext context,
        CadTransientHatchFill hatchFill,
        Period period,
        double pixelScale,
        int pixelWidth,
        int pixelHeight)
    {
        var pixels = Rasterize(hatchFill, period, pixelScale, pixelWidth, pixelHeight);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var bitmap = context.CreateBitmap(
                new SizeI(pixelWidth, pixelHeight),
                handle.AddrOfPinnedObject(),
                (uint)(pixelWidth * 4),
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
                        InterpolationMode.Linear),
                    new BrushProperties(1.0f, Matrix3x2.Identity));
                return new Entry(bitmap, brush, checked(pixelWidth * pixelHeight * 4L));
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
        CadTransientHatchFill hatchFill,
        Period period,
        double pixelScale,
        int pixelWidth,
        int pixelHeight)
    {
        var pixels = new byte[checked(pixelWidth * pixelHeight * 4)];
        var rotation = DegreesToRadians(hatchFill.HatchAngle);
        for (var y = 0; y < pixelHeight; y++)
        {
            var worldY = (y + 0.5) * period.Y / pixelHeight;
            for (var x = 0; x < pixelWidth; x++)
            {
                var worldX = (x + 0.5) * period.X / pixelWidth;
                var point = new CadPointD(worldX, worldY);
                var coverage = 0.0;
                foreach (var line in hatchFill.Lines)
                {
                    coverage = Math.Max(
                        coverage,
                        ResolveCoverage(point, line, hatchFill.HatchScale, rotation, pixelScale));
                    if (coverage >= 1.0)
                        break;
                }

                if (coverage <= 0)
                    continue;
                WritePremultipliedPixel(
                    pixels,
                    (y * pixelWidth + x) * 4,
                    hatchFill.ForegroundColor,
                    coverage);
            }
        }

        return pixels;
    }

    private static double ResolveCoverage(
        CadPointD point,
        CadHatchLineDefinition line,
        double hatchScale,
        double rotation,
        double pixelScale)
    {
        var angle = DegreesToRadians(line.Angle) + rotation;
        var direction = new CadVectorD(Math.Cos(angle), Math.Sin(angle)).Normalize();
        var normal = new CadVectorD(-direction.Y, direction.X);
        var offset = Rotate(line.Offset, rotation) * hatchScale;
        var normalStep = offset.Dot(normal);
        if (Math.Abs(normalStep) <= 1e-9)
            return 0;

        var origin = CadPointD.Origin +
                     Rotate(line.Origin - CadPointD.Origin, rotation) * hatchScale;
        var relative = point - origin;
        var lineIndex = Math.Round(relative.Dot(normal) / normalStep);
        var basePoint = origin + offset * lineIndex;
        var fromLine = point - basePoint;
        var distancePixels = Math.Abs(fromLine.Dot(normal)) * pixelScale;
        var coverage = Math.Clamp(1.0 - distancePixels, 0.0, 1.0);
        if (coverage <= 0 || line.IsSolidLine)
            return coverage;

        var patternLength = ResolveDashPatternLength(line.DashPattern, hatchScale);
        if (patternLength <= 1e-9)
            return 0;
        var along = fromLine.Dot(direction);
        var phase = PositiveModulo(along, patternLength);
        var consumed = 0.0;
        foreach (var dash in line.DashPattern)
        {
            var segmentLength = Math.Abs(dash * hatchScale);
            if (segmentLength <= 1e-9)
            {
                if (dash >= 0 &&
                    Math.Min(phase, patternLength - phase) * pixelScale <= 0.75)
                {
                    return coverage;
                }

                continue;
            }

            if (phase < consumed + segmentLength)
                return dash > 0 ? coverage : 0;
            consumed += segmentLength;
        }

        return 0;
    }

    private static void WritePremultipliedPixel(
        byte[] pixels,
        int offset,
        CadColor color,
        double coverage)
    {
        var alpha = (byte)Math.Clamp(
            (int)Math.Round(color.A * coverage),
            0,
            byte.MaxValue);
        pixels[offset] = (byte)(color.B * alpha / byte.MaxValue);
        pixels[offset + 1] = (byte)(color.G * alpha / byte.MaxValue);
        pixels[offset + 2] = (byte)(color.R * alpha / byte.MaxValue);
        pixels[offset + 3] = alpha;
    }

    private static bool TryResolveTilePeriod(
        CadTransientHatchFill hatchFill,
        out Period period)
    {
        var width = 0.0;
        var height = 0.0;
        var rotation = DegreesToRadians(hatchFill.HatchAngle);
        foreach (var line in hatchFill.Lines)
        {
            var angle = DegreesToRadians(line.Angle) + rotation;
            var direction = new CadVectorD(Math.Cos(angle), Math.Sin(angle)).Normalize();
            var offset = Rotate(line.Offset, rotation) * hatchFill.HatchScale;
            var dashPeriod = line.IsSolidLine
                ? 0.0
                : ResolveDashPatternLength(line.DashPattern, hatchFill.HatchScale);

            if (Math.Abs(direction.Y) <= AxisTolerance)
            {
                if (!TryMergePeriod(ref height, Math.Abs(offset.Y)) ||
                    dashPeriod > 1e-9 &&
                    (!IsPeriodMultiple(offset.X, dashPeriod) ||
                     !TryMergePeriod(ref width, dashPeriod)))
                {
                    period = default;
                    return false;
                }
            }
            else if (Math.Abs(direction.X) <= AxisTolerance)
            {
                if (!TryMergePeriod(ref width, Math.Abs(offset.X)) ||
                    dashPeriod > 1e-9 &&
                    (!IsPeriodMultiple(offset.Y, dashPeriod) ||
                     !TryMergePeriod(ref height, dashPeriod)))
                {
                    period = default;
                    return false;
                }
            }
            else
            {
                period = default;
                return false;
            }
        }

        if (width <= 1e-9)
            width = height;
        if (height <= 1e-9)
            height = width;
        if (!double.IsFinite(width) ||
            !double.IsFinite(height) ||
            width <= 1e-9 ||
            height <= 1e-9)
        {
            period = default;
            return false;
        }

        period = new Period(width, height);
        return true;
    }

    private static bool TryMergePeriod(ref double current, double next)
    {
        if (!double.IsFinite(next) || next <= 1e-9)
            return false;
        if (current <= 1e-9)
        {
            current = next;
            return true;
        }

        var best = double.PositiveInfinity;
        for (var currentMultiple = 1; currentMultiple <= MaximumPeriodMultiple; currentMultiple++)
        {
            var currentValue = current * currentMultiple;
            for (var nextMultiple = 1; nextMultiple <= MaximumPeriodMultiple; nextMultiple++)
            {
                var nextValue = next * nextMultiple;
                var tolerance = Math.Max(currentValue, nextValue) * 1e-6;
                if (Math.Abs(currentValue - nextValue) <= tolerance)
                    best = Math.Min(best, (currentValue + nextValue) * 0.5);
            }
        }

        if (!double.IsFinite(best))
            return false;
        current = best;
        return true;
    }

    private static bool IsPeriodMultiple(double value, double period)
    {
        if (Math.Abs(value) <= 1e-9)
            return true;
        var quotient = value / period;
        return Math.Abs(quotient - Math.Round(quotient)) <= 1e-6;
    }

    private static double ResolveDashPatternLength(
        IReadOnlyList<double> dashPattern,
        double scale)
    {
        var length = 0.0;
        foreach (var dash in dashPattern)
            length += Math.Abs(dash * scale);
        return length;
    }

    private void TrimEntries(HatchTileKey protectedKey)
    {
        while (EstimatedBytes > CacheBudgetBytes)
        {
            var candidate = _entries
                .Where(pair => !pair.Key.Equals(protectedKey))
                .OrderBy(pair => pair.Value.LastUsed)
                .FirstOrDefault();
            if (candidate.Value is null)
                return;
            _estimatedBytes -= candidate.Value.EstimatedBytes;
            candidate.Value.Dispose();
            _entries.Remove(candidate.Key);
            _statistics.RecordGpuCacheEviction();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Clear();
        _deviceContext = null;
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DHatchTileCache));
    }

    private static double ResolveScaleMultiplier(double multiplier) =>
        double.IsFinite(multiplier) && multiplier > double.Epsilon ? multiplier : 1.0;

    private static double NormalizeDegrees(double degrees)
    {
        var normalized = degrees % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static CadVectorD Rotate(CadVectorD vector, double angle)
    {
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        return new CadVectorD(
            vector.X * cos - vector.Y * sin,
            vector.X * sin + vector.Y * cos);
    }

    private static double PositiveModulo(double value, double divisor)
    {
        var result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private readonly record struct Period(double X, double Y);

    private readonly record struct HatchTileKey(
        IReadOnlyList<CadHatchLineDefinition> Lines,
        CadColor Color,
        long HatchScaleBits,
        long HatchAngleBits,
        long ScreenScaleBits,
        int PixelWidth,
        int PixelHeight);

    private sealed class Entry(
        ID2D1Bitmap bitmap,
        ID2D1BitmapBrush brush,
        long estimatedBytes) : IDisposable
    {
        public ID2D1Bitmap Bitmap { get; } = bitmap;
        public ID2D1BitmapBrush Brush { get; } = brush;
        public long EstimatedBytes { get; } = estimatedBytes;
        public long LastUsed { get; set; }

        public void Dispose()
        {
            Brush.Dispose();
            Bitmap.Dispose();
        }
    }
}
