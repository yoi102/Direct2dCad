using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DStyleResourceCache : IDisposable
{
    private readonly Dictionary<CadColor, ID2D1SolidColorBrush> _brushes = [];
    private readonly Dictionary<CadTransientLinePattern, ID2D1StrokeStyle> _transientStrokeStyles = [];
    private readonly Dictionary<CadOriginLinePattern, ID2D1StrokeStyle> _originStrokeStyles = [];
    private readonly HashSet<CadColor> _usedBrushes = [];
    private readonly HashSet<CadTransientLinePattern> _usedTransientStrokeStyles = [];
    private readonly HashSet<CadOriginLinePattern> _usedOriginStrokeStyles = [];
    private ID2D1DeviceContext? _deviceContext;
    private ID2D1Factory? _factory;
    private int _frameDepth;
    private bool _disposed;

    public void Reset(ID2D1Factory? factory, ID2D1DeviceContext? deviceContext)
    {
        ThrowIfDisposed();
        Clear();
        _factory = factory;
        _deviceContext = deviceContext;
    }

    public void BeginFrame()
    {
        ThrowIfDisposed();
        if (_frameDepth++ > 0)
            return;

        _usedBrushes.Clear();
        _usedTransientStrokeStyles.Clear();
        _usedOriginStrokeStyles.Clear();
    }

    public void CompleteFrame()
    {
        ThrowIfDisposed();
        if (_frameDepth == 0 || --_frameDepth > 0)
            return;

        RemoveUnusedResources();
    }

    public ID2D1SolidColorBrush GetBrush(ID2D1DeviceContext context, CadColor color)
    {
        ThrowIfDisposed();
        EnsureDeviceContext(context);
        MarkBrushUsed(color);
        if (_brushes.TryGetValue(color, out var brush))
            return brush;

        brush = context.CreateSolidColorBrush(new Color4(
            color.R / 255.0f,
            color.G / 255.0f,
            color.B / 255.0f,
            color.A / 255.0f));
        _brushes.Add(color, brush);
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
        MarkTransientStrokeStyleUsed(style.LinePattern);
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
        MarkOriginStrokeStyleUsed(pattern);
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

    private void MarkBrushUsed(CadColor color)
    {
        if (_frameDepth > 0)
            _usedBrushes.Add(color);
    }

    private void MarkTransientStrokeStyleUsed(CadTransientLinePattern pattern)
    {
        if (_frameDepth > 0)
            _usedTransientStrokeStyles.Add(pattern);
    }

    private void MarkOriginStrokeStyleUsed(CadOriginLinePattern pattern)
    {
        if (_frameDepth > 0)
            _usedOriginStrokeStyles.Add(pattern);
    }

    private void RemoveUnusedResources()
    {
        foreach (var color in _brushes.Keys.Where(color => !_usedBrushes.Contains(color)).ToArray())
        {
            _brushes[color].Dispose();
            _brushes.Remove(color);
        }

        foreach (var pattern in _transientStrokeStyles.Keys
                     .Where(pattern => !_usedTransientStrokeStyles.Contains(pattern))
                     .ToArray())
        {
            _transientStrokeStyles[pattern].Dispose();
            _transientStrokeStyles.Remove(pattern);
        }

        foreach (var pattern in _originStrokeStyles.Keys
                     .Where(pattern => !_usedOriginStrokeStyles.Contains(pattern))
                     .ToArray())
        {
            _originStrokeStyles[pattern].Dispose();
            _originStrokeStyles.Remove(pattern);
        }
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
        _usedBrushes.Clear();
        _usedTransientStrokeStyles.Clear();
        _usedOriginStrokeStyles.Clear();
        _deviceContext = null;
        _factory = null;
        _frameDepth = 0;
    }

    private void ClearBrushes()
    {
        foreach (var brush in _brushes.Values)
            brush.Dispose();
        _brushes.Clear();
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
}
