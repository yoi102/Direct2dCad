using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DStyleResourceCache : IDisposable
{
    private const int MaximumCachedBrushCount = 128;
    private readonly Dictionary<CadColor, BrushCacheEntry> _brushes = [];
    private readonly Dictionary<CadTransientLinePattern, ID2D1StrokeStyle> _transientStrokeStyles = [];
    private readonly Dictionary<CadOriginLinePattern, ID2D1StrokeStyle> _originStrokeStyles = [];
    private ID2D1DeviceContext? _deviceContext;
    private ID2D1Factory? _factory;
    private long _brushUseCounter;
    private bool _disposed;

    public void Reset(ID2D1Factory? factory, ID2D1DeviceContext? deviceContext)
    {
        ThrowIfDisposed();
        Clear();
        _factory = factory;
        _deviceContext = deviceContext;
    }

    public ID2D1SolidColorBrush GetBrush(ID2D1DeviceContext context, CadColor color)
    {
        ThrowIfDisposed();
        EnsureDeviceContext(context);
        if (_brushes.TryGetValue(color, out var cached))
        {
            cached.LastUsed = ++_brushUseCounter;
            return cached.Brush;
        }

        TrimBrushCache();
        var brush = context.CreateSolidColorBrush(new Color4(
            color.R / 255.0f,
            color.G / 255.0f,
            color.B / 255.0f,
            color.A / 255.0f));
        _brushes.Add(color, new BrushCacheEntry(brush, ++_brushUseCounter));
        return brush;
    }

    public ID2D1StrokeStyle? GetStrokeStyle(ID2D1Factory? factory, CadTransientStyle style)
    {
        if (style.LinePattern == CadTransientLinePattern.Solid)
            return null;

        ThrowIfDisposed();
        if (factory is null)
            return null;

        EnsureFactory(factory);
        if (_transientStrokeStyles.TryGetValue(style.LinePattern, out var strokeStyle))
            return strokeStyle;

        strokeStyle = factory.CreateStrokeStyle(new StrokeStyleProperties
        {
            StartCap = CapStyle.Flat,
            EndCap = CapStyle.Flat,
            DashCap = CapStyle.Flat,
            LineJoin = LineJoin.Miter,
            DashStyle = style.LinePattern switch
            {
                CadTransientLinePattern.Dot => DashStyle.Dot,
                CadTransientLinePattern.DashDot => DashStyle.DashDot,
                _ => DashStyle.Dash
            }
        });
        _transientStrokeStyles.Add(style.LinePattern, strokeStyle);
        return strokeStyle;
    }

    public ID2D1StrokeStyle? GetOriginStrokeStyle(
        ID2D1Factory? factory,
        CadOriginLinePattern pattern)
    {
        if (pattern == CadOriginLinePattern.Solid)
            return null;

        ThrowIfDisposed();
        if (factory is null)
            return null;

        EnsureFactory(factory);
        if (_originStrokeStyles.TryGetValue(pattern, out var strokeStyle))
            return strokeStyle;

        strokeStyle = factory.CreateStrokeStyle(new StrokeStyleProperties
        {
            StartCap = CapStyle.Flat,
            EndCap = CapStyle.Flat,
            DashCap = CapStyle.Flat,
            LineJoin = LineJoin.Miter,
            DashStyle = pattern switch
            {
                CadOriginLinePattern.Dot => DashStyle.Dot,
                CadOriginLinePattern.DashDot => DashStyle.DashDot,
                _ => DashStyle.Dash
            }
        });
        _originStrokeStyles.Add(pattern, strokeStyle);
        return strokeStyle;
    }

    public float ResolveStrokeWidth(CadTransientStyle style, CadViewport viewport)
    {
        var zoom = Math.Max(viewport.Zoom, double.Epsilon);
        var width = Math.Max(style.StrokeWidth, 0.1);
        var strokeWidth = style.KeepStrokeWidthScreenConstant
            ? (float)(width / zoom)
            : (float)width;
        var minimumStrokeWidth = (float)(Math.Max(style.MinimumScreenStrokeWidth, 0.0) / zoom);
        return Math.Max(strokeWidth, minimumStrokeWidth);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Clear();
        _disposed = true;
    }

    private void EnsureDeviceContext(ID2D1DeviceContext context)
    {
        if (ReferenceEquals(_deviceContext, context))
            return;

        ClearBrushes();
        _deviceContext = context;
    }

    private void EnsureFactory(ID2D1Factory factory)
    {
        if (ReferenceEquals(_factory, factory))
            return;

        ClearStrokeStyles();
        _factory = factory;
    }

    private void Clear()
    {
        ClearBrushes();
        ClearStrokeStyles();
        _deviceContext = null;
        _factory = null;
    }

    private void ClearBrushes()
    {
        foreach (var entry in _brushes.Values)
            entry.Brush.Dispose();
        _brushes.Clear();
        _brushUseCounter = 0;
    }

    private void TrimBrushCache()
    {
        if (_brushes.Count < MaximumCachedBrushCount)
            return;

        var hasOldest = false;
        var oldestColor = default(CadColor);
        var oldestUse = long.MaxValue;
        foreach (var pair in _brushes)
        {
            if (pair.Value.LastUsed >= oldestUse)
                continue;

            hasOldest = true;
            oldestColor = pair.Key;
            oldestUse = pair.Value.LastUsed;
        }

        if (hasOldest && _brushes.Remove(oldestColor, out var oldest))
            oldest.Brush.Dispose();
    }

    private void ClearStrokeStyles()
    {
        foreach (var strokeStyle in _transientStrokeStyles.Values)
            strokeStyle.Dispose();
        foreach (var strokeStyle in _originStrokeStyles.Values)
            strokeStyle.Dispose();
        _transientStrokeStyles.Clear();
        _originStrokeStyles.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DStyleResourceCache));
    }

    private sealed class BrushCacheEntry(ID2D1SolidColorBrush brush, long lastUsed)
    {
        public ID2D1SolidColorBrush Brush { get; } = brush;
        public long LastUsed { get; set; } = lastUsed;
    }
}
