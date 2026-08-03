using System.Numerics;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D.Resources;

internal sealed class Direct2DStyleResourceCache : IDisposable
{
    private const float DefaultMiterLimit = 10.0f;
    private const float LevelOfDetailMiterLimit = 2.0f;
    private readonly Dictionary<CadColor, ResourceEntry<ID2D1SolidColorBrush>> _brushes = [];
    private readonly Dictionary<StrokeStyleKey, ResourceEntry<ID2D1StrokeStyle>> _strokeStyles = [];
    private readonly HashSet<CadColor> _usedBrushes = [];
    private readonly HashSet<StrokeStyleKey> _usedStrokeStyles = [];
    private readonly HashSet<CadColor> _unleasedBrushes = [];
    private readonly HashSet<StrokeStyleKey> _unleasedStrokeStyles = [];
    private readonly Action<CadColor> _releaseBrush;
    private readonly Action<StrokeStyleKey> _releaseStrokeStyle;
    private ID2D1DeviceContext? _deviceContext;
    private ID2D1Factory? _factory;
    private ID2D1PathGeometry? _unitDiamondGeometry;
    private int _frameDepth;
    private bool _disposed;

    public Direct2DStyleResourceCache()
    {
        _releaseBrush = ReleaseBrush;
        _releaseStrokeStyle = ReleaseStrokeStyle;
    }

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
        _usedStrokeStyles.Clear();
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
        if (_frameDepth > 0)
            _usedBrushes.Add(color);
        return GetOrCreateBrush(color).Resource;
    }

    public KeyedResourceLease<ID2D1SolidColorBrush, CadColor> AcquireBrush(CadColor color)
    {
        ThrowIfDisposed();
        if (_deviceContext is null)
            throw new InvalidOperationException("The Direct2D device context is not available.");

        var entry = GetOrCreateBrush(color);
        entry.ReferenceCount++;
        _unleasedBrushes.Remove(color);
        return new KeyedResourceLease<ID2D1SolidColorBrush, CadColor>(
            entry.Resource,
            color,
            _releaseBrush);
    }

    public ID2D1StrokeStyle? GetStrokeStyle(ID2D1Factory? factory, CadTransientStyle style)
    {
        if (style.StrokeStyle is { } strokeStyle)
            return GetEntityStrokeStyle(factory, strokeStyle);

        if (style.LineType is { } lineType)
            return GetLineTypeStrokeStyle(factory, lineType);

        if (style.LinePattern == CadTransientLinePattern.Solid)
            return null;

        var dashStyle = style.LinePattern switch
        {
            CadTransientLinePattern.Dot => DashStyle.Dot,
            CadTransientLinePattern.DashDot => DashStyle.DashDot,
            _ => DashStyle.Dash
        };
        return GetStrokeStyleForFrame(factory, StrokeStyleKey.CreateDefault(dashStyle));
    }

    private ID2D1StrokeStyle? GetEntityStrokeStyle(
        ID2D1Factory? factory,
        CadStrokeStyle style)
    {
        if (factory is null || style == CadStrokeStyle.Default)
            return null;

        var key = new StrokeStyleKey(
            ToD2DCapStyle(style.StartCap),
            ToD2DCapStyle(style.EndCap),
            ToD2DCapStyle(style.DashCap),
            ToD2DLineJoin(style.LineJoin),
            ToD2DDashStyle(style.DashStyle),
            DefaultMiterLimit);
        return GetStrokeStyleForFrame(factory, key);
    }

    private ID2D1StrokeStyle? GetLineTypeStrokeStyle(
        ID2D1Factory? factory,
        CadLineTypeDefinition lineType)
    {
        if (factory is null || lineType.IsContinuous)
            return null;

        var dashes = lineType.DashPattern
            .Select(value => (float)Math.Max(Math.Abs(value), 0.001))
            .ToArray();
        return GetStrokeStyleForFrame(factory, StrokeStyleKey.CreateCustom(dashes));
    }

    public ID2D1StrokeStyle? GetOriginStrokeStyle(
        ID2D1Factory? factory,
        CadOriginLinePattern pattern)
    {
        if (pattern == CadOriginLinePattern.Solid)
            return null;

        var dashStyle = pattern switch
        {
            CadOriginLinePattern.Dot => DashStyle.Dot,
            CadOriginLinePattern.DashDot => DashStyle.DashDot,
            _ => DashStyle.Dash
        };
        return GetStrokeStyleForFrame(factory, StrokeStyleKey.CreateDefault(dashStyle));
    }

    public KeyedResourceLease<ID2D1StrokeStyle, StrokeStyleKey>? AcquireStrokeStyle(CadStrokeStyle style)
    {
        ThrowIfDisposed();
        if (style == CadStrokeStyle.Default || _factory is null)
            return null;

        var key = new StrokeStyleKey(
            ToD2DCapStyle(style.StartCap),
            ToD2DCapStyle(style.EndCap),
            ToD2DCapStyle(style.DashCap),
            ToD2DLineJoin(style.LineJoin),
            ToD2DDashStyle(style.DashStyle),
            DefaultMiterLimit);
        var entry = GetOrCreateStrokeStyle(key);
        entry.ReferenceCount++;
        _unleasedStrokeStyles.Remove(key);
        return new KeyedResourceLease<ID2D1StrokeStyle, StrokeStyleKey>(
            entry.Resource,
            key,
            _releaseStrokeStyle);
    }

    public KeyedResourceLease<ID2D1StrokeStyle, StrokeStyleKey>? AcquireLineTypeStrokeStyle(
        CadLineTypeDefinition lineType)
    {
        ThrowIfDisposed();
        if (lineType.IsContinuous || _factory is null)
            return null;

        var dashes = lineType.DashPattern.Select(value => (float)Math.Max(Math.Abs(value), 0.001)).ToArray();
        var key = StrokeStyleKey.CreateCustom(dashes);
        var entry = GetOrCreateStrokeStyle(key);
        entry.ReferenceCount++;
        _unleasedStrokeStyles.Remove(key);
        return new KeyedResourceLease<ID2D1StrokeStyle, StrokeStyleKey>(
            entry.Resource,
            key,
            _releaseStrokeStyle);
    }

    public ID2D1StrokeStyle? GetLevelOfDetailStrokeStyle(
        ID2D1Factory? factory,
        CadStrokeStyle style)
    {
        if (factory is null)
            return null;

        var lineJoin = ToD2DLineJoin(style.LineJoin);
        if (lineJoin == LineJoin.Miter)
            lineJoin = LineJoin.MiterOrBevel;

        var key = new StrokeStyleKey(
            ToD2DCapStyle(style.StartCap),
            ToD2DCapStyle(style.EndCap),
            CapStyle.Flat,
            lineJoin,
            DashStyle.Solid,
            LevelOfDetailMiterLimit);
        return GetStrokeStyleForFrame(factory, key);
    }

    public ID2D1PathGeometry? GetUnitDiamondGeometry(ID2D1Factory? factory)
    {
        ThrowIfDisposed();
        if (factory is null)
            return null;

        EnsureFactory(factory);
        if (_unitDiamondGeometry is not null)
            return _unitDiamondGeometry;

        var geometry = factory.CreatePathGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(new Vector2(0, -1), FigureBegin.Filled);
            sink.AddLine(new Vector2(1, 0));
            sink.AddLine(new Vector2(0, 1));
            sink.AddLine(new Vector2(-1, 0));
            sink.EndFigure(FigureEnd.Closed);
            sink.Close();
        }

        _unitDiamondGeometry = geometry;
        return geometry;
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

    private ID2D1StrokeStyle? GetStrokeStyleForFrame(ID2D1Factory? factory, StrokeStyleKey key)
    {
        ThrowIfDisposed();
        if (factory is null)
            return null;

        EnsureFactory(factory);
        if (_frameDepth > 0)
            _usedStrokeStyles.Add(key);
        return GetOrCreateStrokeStyle(key).Resource;
    }

    private ResourceEntry<ID2D1SolidColorBrush> GetOrCreateBrush(CadColor color)
    {
        if (_brushes.TryGetValue(color, out var entry))
            return entry;

        var brush = _deviceContext!.CreateSolidColorBrush(new Color4(
            color.R / 255.0f,
            color.G / 255.0f,
            color.B / 255.0f,
            color.A / 255.0f));
        entry = new ResourceEntry<ID2D1SolidColorBrush>(brush);
        _brushes.Add(color, entry);
        _unleasedBrushes.Add(color);
        return entry;
    }

    private ResourceEntry<ID2D1StrokeStyle> GetOrCreateStrokeStyle(StrokeStyleKey key)
    {
        if (_strokeStyles.TryGetValue(key, out var entry))
            return entry;

        var properties = new StrokeStyleProperties
        {
            StartCap = key.StartCap,
            EndCap = key.EndCap,
            DashCap = key.DashCap,
            LineJoin = key.LineJoin,
            MiterLimit = key.MiterLimit,
            DashStyle = key.DashStyle
        };
        var dashes = key.Dashes ?? [];
        var strokeStyle = _factory!.CreateStrokeStyle(properties, dashes);
        entry = new ResourceEntry<ID2D1StrokeStyle>(strokeStyle);
        _strokeStyles.Add(key, entry);
        _unleasedStrokeStyles.Add(key);
        return entry;
    }

    private void ReleaseBrush(CadColor color)
    {
        if (!_brushes.TryGetValue(color, out var entry))
            return;

        if (entry.ReferenceCount > 0)
            entry.ReferenceCount--;
        if (entry.ReferenceCount != 0)
            return;

        if (_frameDepth == 0)
            RemoveBrush(color);
        else
            _unleasedBrushes.Add(color);
    }

    private void ReleaseStrokeStyle(StrokeStyleKey key)
    {
        if (!_strokeStyles.TryGetValue(key, out var entry))
            return;

        if (entry.ReferenceCount > 0)
            entry.ReferenceCount--;
        if (entry.ReferenceCount != 0)
            return;

        if (_frameDepth == 0)
            RemoveStrokeStyle(key);
        else
            _unleasedStrokeStyles.Add(key);
    }

    private void RemoveUnusedResources()
    {
        _unleasedBrushes.RemoveWhere(color =>
        {
            if (_usedBrushes.Contains(color))
                return false;

            if (_brushes.Remove(color, out var entry))
                entry.Resource.Dispose();
            return true;
        });

        _unleasedStrokeStyles.RemoveWhere(key =>
        {
            if (_usedStrokeStyles.Contains(key))
                return false;

            if (_strokeStyles.Remove(key, out var entry))
                entry.Resource.Dispose();
            return true;
        });
    }

    private void RemoveBrush(CadColor color)
    {
        _unleasedBrushes.Remove(color);
        _usedBrushes.Remove(color);
        if (_brushes.Remove(color, out var entry))
            entry.Resource.Dispose();
    }

    private void RemoveStrokeStyle(StrokeStyleKey key)
    {
        _unleasedStrokeStyles.Remove(key);
        _usedStrokeStyles.Remove(key);
        if (_strokeStyles.Remove(key, out var entry))
            entry.Resource.Dispose();
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

        ClearFactoryResources();
        _factory = factory;
    }

    private void Clear()
    {
        ClearBrushes();
        ClearFactoryResources();
        _usedBrushes.Clear();
        _usedStrokeStyles.Clear();
        _unleasedBrushes.Clear();
        _unleasedStrokeStyles.Clear();
        _deviceContext = null;
        _factory = null;
        _frameDepth = 0;
    }

    private void ClearBrushes()
    {
        foreach (var entry in _brushes.Values)
            entry.Resource.Dispose();
        _brushes.Clear();
        _unleasedBrushes.Clear();
    }

    private void ClearFactoryResources()
    {
        foreach (var entry in _strokeStyles.Values)
            entry.Resource.Dispose();
        _strokeStyles.Clear();
        _unleasedStrokeStyles.Clear();
        _unitDiamondGeometry?.Dispose();
        _unitDiamondGeometry = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DStyleResourceCache));
    }

    private static CapStyle ToD2DCapStyle(CadStrokeCap cap)
    {
        return cap switch
        {
            CadStrokeCap.Square => CapStyle.Square,
            CadStrokeCap.Round => CapStyle.Round,
            CadStrokeCap.Triangle => CapStyle.Triangle,
            _ => CapStyle.Flat
        };
    }

    private static DashStyle ToD2DDashStyle(CadStrokeDashStyle dashStyle)
    {
        return dashStyle switch
        {
            CadStrokeDashStyle.Dash => DashStyle.Dash,
            CadStrokeDashStyle.Dot => DashStyle.Dot,
            CadStrokeDashStyle.DashDot => DashStyle.DashDot,
            CadStrokeDashStyle.DashDotDot => DashStyle.DashDotDot,
            _ => DashStyle.Solid
        };
    }

    private static LineJoin ToD2DLineJoin(CadStrokeLineJoin lineJoin)
    {
        return lineJoin switch
        {
            CadStrokeLineJoin.Bevel => LineJoin.Bevel,
            CadStrokeLineJoin.Round => LineJoin.Round,
            CadStrokeLineJoin.MiterOrBevel => LineJoin.MiterOrBevel,
            _ => LineJoin.Miter
        };
    }

    internal readonly struct StrokeStyleKey : IEquatable<StrokeStyleKey>
    {
        public CapStyle StartCap { get; }
        public CapStyle EndCap { get; }
        public CapStyle DashCap { get; }
        public LineJoin LineJoin { get; }
        public DashStyle DashStyle { get; }
        public float MiterLimit { get; }
        public float[]? Dashes { get; }

        public StrokeStyleKey(
            CapStyle startCap,
            CapStyle endCap,
            CapStyle dashCap,
            LineJoin lineJoin,
            DashStyle dashStyle,
            float miterLimit,
            float[]? dashes = null)
        {
            StartCap = startCap;
            EndCap = endCap;
            DashCap = dashCap;
            LineJoin = lineJoin;
            DashStyle = dashStyle;
            MiterLimit = miterLimit;
            Dashes = dashes;
        }

        public static StrokeStyleKey CreateDefault(DashStyle dashStyle) => new(
            CapStyle.Flat,
            CapStyle.Flat,
            CapStyle.Flat,
            LineJoin.Miter,
            dashStyle,
            DefaultMiterLimit);

        public static StrokeStyleKey CreateCustom(float[] dashes) => new(
            CapStyle.Flat,
            CapStyle.Flat,
            CapStyle.Flat,
            LineJoin.Miter,
            DashStyle.Custom,
            DefaultMiterLimit,
            dashes);

        public bool Equals(StrokeStyleKey other) =>
            StartCap == other.StartCap &&
            EndCap == other.EndCap &&
            DashCap == other.DashCap &&
            LineJoin == other.LineJoin &&
            DashStyle == other.DashStyle &&
            MiterLimit.Equals(other.MiterLimit) &&
            (Dashes ?? []).SequenceEqual(other.Dashes ?? []);

        public override bool Equals(object? obj) => obj is StrokeStyleKey other && Equals(other);

        public override int GetHashCode()
        {
            var hash = HashCode.Combine(StartCap, EndCap, DashCap, LineJoin, DashStyle, MiterLimit);
            foreach (var dash in Dashes ?? [])
                hash = HashCode.Combine(hash, dash);
            return hash;
        }
    }

    private sealed class ResourceEntry<T>(T resource) where T : IDisposable
    {
        public T Resource { get; } = resource;
        public int ReferenceCount { get; set; }
    }
}
