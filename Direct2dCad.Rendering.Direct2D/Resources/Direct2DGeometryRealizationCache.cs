using System.Diagnostics;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering.Direct2D.Scene;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Resources;

/// <summary>
/// Caches tessellated representations for complex, stable geometries. Realizations are
/// grouped into 2x screen-scale profiles as recommended by the Direct2D documentation.
/// </summary>
internal sealed class Direct2DGeometryRealizationCache : IDisposable
{
    private const float DefaultFlatteningTolerance = 0.25f;
    private const float ClosedSplineFillFlatteningTolerance = 0.10f;
    private const double MaximumScalePerProfile = 2.0;
    private const int StrokeScaleProfilesPerOctave = 16;
    private const double BuildBudgetMilliseconds = 2.0;
    private const int MaximumBuildsPerBatch = 128;
    private const int MinimumPolylinePointCount = 16;
    private const int MinimumSplineFitPointCount = 4;
    private const int MinimumShapeTextSegmentCount = 8;

    private ID2D1DeviceContext1? _deviceContext;
    private double _buildElapsedMilliseconds;
    private int _remainingBuilds;
    private double _scaleMultiplier = 1.0;
    private int _fillDrawCount;
    private int _strokeDrawCount;
    private int _buildCount;
    private int _fallbackCount;
    private bool _disposed;

    public void Reset(ID2D1DeviceContext? deviceContext)
    {
        ThrowIfDisposed();
        _deviceContext?.Dispose();
        _deviceContext = deviceContext?.QueryInterface<ID2D1DeviceContext1>();
        _scaleMultiplier = 1.0;
        _fillDrawCount = 0;
        _strokeDrawCount = 0;
        _buildCount = 0;
        _fallbackCount = 0;
        BeginFrame();
    }

    public IDisposable PushScaleMultiplier(double scaleMultiplier)
    {
        ThrowIfDisposed();
        var previous = _scaleMultiplier;
        _scaleMultiplier = double.IsFinite(scaleMultiplier) && scaleMultiplier > double.Epsilon
            ? scaleMultiplier
            : 1.0;
        return new ScaleMultiplierScope(this, previous);
    }

    public void BeginFrame()
    {
        ThrowIfDisposed();
        BeginBuildBatch();
    }

    public void BeginBuildBatch()
    {
        ThrowIfDisposed();
        _buildElapsedMilliseconds = 0.0;
        _remainingBuilds = MaximumBuildsPerBatch;
    }

    public Direct2DGeometryRealizationStatistics CaptureStatistics()
    {
        ThrowIfDisposed();
        var result = new Direct2DGeometryRealizationStatistics(
            _fillDrawCount,
            _strokeDrawCount,
            _buildCount,
            _fallbackCount);
        _fillDrawCount = 0;
        _strokeDrawCount = 0;
        _buildCount = 0;
        _fallbackCount = 0;
        return result;
    }

    public bool TryDrawFill(
        ID2D1DeviceContext context,
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources,
        ID2D1Geometry geometry,
        ID2D1Brush brush)
    {
        ThrowIfDisposed();
        if (_deviceContext is null || !CanRealize(entity, resources, geometry))
            return false;

        var scaleProfile = ResolveFillScaleProfile(context.Transform, _scaleMultiplier);
        var entityCache = resources.GeometryRealizations;
        if (entityCache is null ||
            !entityCache.TryGetFill(geometry, scaleProfile, out var profile))
        {
            if (!TryReserveBuild())
            {
                _fallbackCount++;
                return false;
            }

            entityCache ??= new EntityCache();
            resources.GeometryRealizations = entityCache;
            profile = entityCache.GetOrCreateFill(geometry, scaleProfile);
            CreateFillRealization(profile, geometry, entity);
        }
        else if (profile.Fill is null && TryReserveBuild())
        {
            CreateFillRealization(profile, geometry, entity);
        }

        if (profile.Fill is null)
        {
            _fallbackCount++;
            return false;
        }

        _deviceContext.DrawGeometryRealization(profile.Fill, brush);
        _fillDrawCount++;
        return true;
    }

    public bool TryDrawStroke(
        ID2D1DeviceContext context,
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources,
        ID2D1Geometry geometry,
        ID2D1Brush brush,
        float strokeWidth,
        ID2D1StrokeStyle? strokeStyle,
        Direct2DStrokeRealizationStyleKey strokeStyleKey,
        bool strokeWidthChangesWithScale)
    {
        ThrowIfDisposed();
        if (_deviceContext is null ||
            strokeWidth <= 0.0f ||
            !CanRealize(entity, resources, geometry))
        {
            return false;
        }

        var screenScale = ResolveScreenScale(context.Transform, _scaleMultiplier);
        var scaleProfile = ResolveStrokeScaleProfile(
            screenScale,
            strokeWidthChangesWithScale);
        var realizationStrokeWidth = strokeWidthChangesWithScale
            ? (float)(strokeWidth * screenScale / scaleProfile.AnchorScale)
            : strokeWidth;
        var entityCache = resources.GeometryRealizations;
        var buildReserved = false;
        if (entityCache is null ||
            !entityCache.TryGetStroke(geometry, scaleProfile, out var profile))
        {
            if (!TryReserveBuild())
            {
                _fallbackCount++;
                return false;
            }

            buildReserved = true;
            entityCache ??= new EntityCache();
            resources.GeometryRealizations = entityCache;
            profile = entityCache.GetOrCreateStroke(geometry, scaleProfile);
        }

        if (!profile.MatchesStroke(realizationStrokeWidth, strokeStyleKey))
            profile.ClearStroke();

        if (profile.Stroke is null && (buildReserved || TryReserveBuild()))
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                profile.Stroke = _deviceContext.CreateStrokedGeometryRealization(
                    geometry,
                    ResolveFlatteningTolerance(profile.AnchorScale),
                    realizationStrokeWidth,
                    strokeStyle);
                profile.StrokeWidth = realizationStrokeWidth;
                profile.StrokeStyleKey = strokeStyleKey;
                _buildCount++;
            }
            finally
            {
                RecordBuildDuration(started);
            }
        }

        if (profile.Stroke is null)
        {
            _fallbackCount++;
            return false;
        }

        _deviceContext.DrawGeometryRealization(profile.Stroke, brush);
        _strokeDrawCount++;
        return true;
    }

    private bool TryReserveBuild()
    {
        if (_remainingBuilds <= 0 ||
            _buildElapsedMilliseconds >= BuildBudgetMilliseconds)
        {
            return false;
        }

        _remainingBuilds--;
        return true;
    }

    private void CreateFillRealization(
        Profile profile,
        ID2D1Geometry geometry,
        CadEntity entity)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            profile.Fill = _deviceContext!.CreateFilledGeometryRealization(
                geometry,
                ResolveFlatteningTolerance(
                    profile.AnchorScale,
                    entity is CadSpline { Closed: true }
                        ? ClosedSplineFillFlatteningTolerance
                        : DefaultFlatteningTolerance));
            _buildCount++;
        }
        finally
        {
            RecordBuildDuration(started);
        }
    }

    private void RecordBuildDuration(long started)
    {
        _buildElapsedMilliseconds += Stopwatch
            .GetElapsedTime(started)
            .TotalMilliseconds;
    }

    private static bool CanRealize(
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources,
        ID2D1Geometry geometry)
    {
        if (!ReferenceEquals(resources.Geometry, geometry))
            return false;

        return entity switch
        {
            CadPolyline polyline => polyline.Points.Count >= MinimumPolylinePointCount,
            CadSpline spline => spline.FitPoints.Count >= MinimumSplineFitPointCount,
            CadShapeText => resources.GeometryComplexity >= MinimumShapeTextSegmentCount,
            _ => false
        };
    }

    private static ScaleProfile ResolveFillScaleProfile(
        System.Numerics.Matrix3x2 transform,
        double scaleMultiplier)
    {
        var screenScale = ResolveScreenScale(transform, scaleMultiplier);
        var exponent = (int)Math.Floor(Math.Log2(screenScale));
        return new ScaleProfile(exponent, Math.Pow(2.0, exponent));
    }

    private static ScaleProfile ResolveStrokeScaleProfile(
        double screenScale,
        bool strokeWidthChangesWithScale)
    {
        if (!strokeWidthChangesWithScale)
        {
            var exponent = (int)Math.Floor(Math.Log2(screenScale));
            return new ScaleProfile(exponent, Math.Pow(2.0, exponent));
        }

        var bucket = (int)Math.Round(
            Math.Log2(screenScale) * StrokeScaleProfilesPerOctave,
            MidpointRounding.AwayFromZero);
        return new ScaleProfile(
            bucket,
            Math.Pow(2.0, (double)bucket / StrokeScaleProfilesPerOctave));
    }

    private static double ResolveScreenScale(
        System.Numerics.Matrix3x2 transform,
        double scaleMultiplier)
    {
        var screenScale = Direct2DEntityLevelOfDetail.ResolveMaximumScreenScale(transform) *
                          scaleMultiplier;
        return Math.Clamp(screenScale, Math.Pow(2.0, -40.0), Math.Pow(2.0, 40.0));
    }

    private static float ResolveFlatteningTolerance(
        double anchorScale,
        float screenTolerance = DefaultFlatteningTolerance)
    {
        var tolerance = screenTolerance /
                        (anchorScale * MaximumScalePerProfile);
        return (float)Math.Clamp(tolerance, 1e-7, 1e7);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _deviceContext?.Dispose();
        _deviceContext = null;
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Direct2DGeometryRealizationCache));
    }

    private sealed class ScaleMultiplierScope(
        Direct2DGeometryRealizationCache owner,
        double previous) : IDisposable
    {
        private Direct2DGeometryRealizationCache? _owner = owner;

        public void Dispose()
        {
            if (_owner is not { } current)
                return;

            current._scaleMultiplier = previous;
            _owner = null;
        }
    }

    internal readonly record struct ScaleProfile(int Key, double AnchorScale);

    internal sealed class EntityCache : IDisposable
    {
        private const int MaximumProfiles = 3;
        private readonly Dictionary<int, Profile> _fillProfiles = [];
        private readonly Dictionary<int, Profile> _strokeProfiles = [];
        private long _usageStamp;

        public bool TryGetFill(
            ID2D1Geometry geometry,
            ScaleProfile scaleProfile,
            out Profile profile) =>
            TryGet(_fillProfiles, geometry, scaleProfile, out profile);

        public bool TryGetStroke(
            ID2D1Geometry geometry,
            ScaleProfile scaleProfile,
            out Profile profile) =>
            TryGet(_strokeProfiles, geometry, scaleProfile, out profile);

        public Profile GetOrCreateFill(
            ID2D1Geometry geometry,
            ScaleProfile scaleProfile) =>
            GetOrCreate(_fillProfiles, geometry, scaleProfile);

        public Profile GetOrCreateStroke(
            ID2D1Geometry geometry,
            ScaleProfile scaleProfile) =>
            GetOrCreate(_strokeProfiles, geometry, scaleProfile);

        private bool TryGet(
            Dictionary<int, Profile> profiles,
            ID2D1Geometry geometry,
            ScaleProfile scaleProfile,
            out Profile profile)
        {
            if (profiles.TryGetValue(scaleProfile.Key, out profile!) &&
                ReferenceEquals(profile.Geometry, geometry))
            {
                profile.LastUsed = ++_usageStamp;
                return true;
            }

            profile = null!;
            return false;
        }

        private Profile GetOrCreate(
            Dictionary<int, Profile> profiles,
            ID2D1Geometry geometry,
            ScaleProfile scaleProfile)
        {
            if (profiles.TryGetValue(scaleProfile.Key, out var profile))
            {
                if (ReferenceEquals(profile.Geometry, geometry))
                {
                    profile.LastUsed = ++_usageStamp;
                    return profile;
                }

                profile.Dispose();
                profiles.Remove(scaleProfile.Key);
            }

            profile = new Profile(geometry, scaleProfile.AnchorScale)
            {
                LastUsed = ++_usageStamp
            };
            profiles.Add(scaleProfile.Key, profile);
            TrimProfiles(profiles, scaleProfile.Key);
            return profile;
        }

        public void ClearStroke()
        {
            DisposeProfiles(_strokeProfiles);
        }

        public void Clear()
        {
            DisposeProfiles(_fillProfiles);
            DisposeProfiles(_strokeProfiles);
        }

        private static void TrimProfiles(
            Dictionary<int, Profile> profiles,
            int currentKey)
        {
            while (profiles.Count > MaximumProfiles)
            {
                var oldest = profiles
                    .Where(pair => pair.Key != currentKey)
                    .MinBy(static pair => pair.Value.LastUsed);
                oldest.Value.Dispose();
                profiles.Remove(oldest.Key);
            }
        }

        private static void DisposeProfiles(Dictionary<int, Profile> profiles)
        {
            foreach (var profile in profiles.Values)
                profile.Dispose();
            profiles.Clear();
        }

        public void Dispose() => Clear();
    }

    internal sealed class Profile : IDisposable
    {
        public ID2D1Geometry Geometry { get; }
        public double AnchorScale { get; }
        public long LastUsed { get; set; }
        public ID2D1GeometryRealization? Fill { get; set; }
        public ID2D1GeometryRealization? Stroke { get; set; }
        public float StrokeWidth { get; set; }
        public Direct2DStrokeRealizationStyleKey StrokeStyleKey { get; set; }

        public Profile(ID2D1Geometry geometry, double anchorScale)
        {
            Geometry = geometry;
            AnchorScale = anchorScale;
        }

        public bool MatchesStroke(
            float strokeWidth,
            Direct2DStrokeRealizationStyleKey strokeStyleKey)
        {
            return Stroke is not null &&
                   Math.Abs(StrokeWidth - strokeWidth) <=
                   Math.Max(1e-6f, Math.Abs(strokeWidth) * 1e-5f) &&
                   StrokeStyleKey == strokeStyleKey;
        }

        public void ClearStroke()
        {
            Stroke?.Dispose();
            Stroke = null;
            StrokeWidth = 0.0f;
            StrokeStyleKey = default;
        }

        public void Dispose()
        {
            Fill?.Dispose();
            Fill = null;
            ClearStroke();
        }
    }
}

internal readonly record struct Direct2DGeometryRealizationStatistics(
    int FillDrawCount,
    int StrokeDrawCount,
    int BuildCount,
    int FallbackCount);
