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
    private int _cacheEvictionCount;
    private long _estimatedBytes;
    private bool _disposed;

    public long EstimatedBytes => Math.Max(0, _estimatedBytes);

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
        _cacheEvictionCount = 0;
        _estimatedBytes = 0;
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
            _fallbackCount,
            _cacheEvictionCount);
        _fillDrawCount = 0;
        _strokeDrawCount = 0;
        _buildCount = 0;
        _fallbackCount = 0;
        _cacheEvictionCount = 0;
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

            entityCache ??= new EntityCache(AdjustEstimatedBytes);
            resources.GeometryRealizations = entityCache;
            profile = entityCache.GetOrCreateFill(geometry, scaleProfile);
            CreateFillRealization(profile, geometry, entity, resources);
            _cacheEvictionCount += entityCache.TrimToBudget(profile);
        }
        else if (profile.Fill is null && TryReserveBuild())
        {
            CreateFillRealization(profile, geometry, entity, resources);
            _cacheEvictionCount += entityCache!.TrimToBudget(profile);
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
            entityCache ??= new EntityCache(AdjustEstimatedBytes);
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
                profile.StrokeEstimatedBytes = EstimateRealizationBytes(entity, resources);
                _buildCount++;
            }
            finally
            {
                RecordBuildDuration(started);
            }
            _cacheEvictionCount += entityCache!.TrimToBudget(profile);
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
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            profile.Fill = _deviceContext!.CreateFilledGeometryRealization(
                geometry,
                ResolveFlatteningTolerance(
                    profile.AnchorScale,
                    entity is CadSpline { Closed: true } or CadCompositePath { Closed: true }
                        ? ClosedSplineFillFlatteningTolerance
                        : DefaultFlatteningTolerance));
            profile.FillEstimatedBytes = EstimateRealizationBytes(entity, resources);
            _buildCount++;
        }
        finally
        {
            RecordBuildDuration(started);
        }
    }

    private static long EstimateRealizationBytes(
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources)
    {
        var complexity = entity switch
        {
            CadPolyline polyline => polyline.Points.Count,
            CadSpline spline => spline.FitPoints.Count * 4,
            CadCompositePath path => path.Segments.Sum(segment => segment is CadCompositeSplineSegment spline
                ? spline.FitPoints.Count * 4
                : 1),
            CadShapeText => resources.GeometryComplexity,
            _ => Math.Max(resources.GeometryComplexity, 1)
        };
        return 4L * 1024 + Math.Max(complexity, 1) * 96L;
    }

    private void RecordBuildDuration(long started)
    {
        _buildElapsedMilliseconds += Stopwatch
            .GetElapsedTime(started)
            .TotalMilliseconds;
    }

    private void AdjustEstimatedBytes(long delta)
    {
        _estimatedBytes = Math.Max(0, _estimatedBytes + delta);
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
            CadCompositePath path => path.Segments.Count >= 8,
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
        private const long CacheBudgetBytes = 2L * 1024 * 1024;
        private static long _globalUsageStamp;
        private readonly Dictionary<int, Profile> _fillProfiles = [];
        private readonly Dictionary<int, Profile> _strokeProfiles = [];
        private readonly Action<long> _estimatedBytesChanged;
        private long _estimatedBytes;

        public long EstimatedBytes => Math.Max(0, _estimatedBytes);

        public EntityCache(Action<long> estimatedBytesChanged)
        {
            _estimatedBytesChanged = estimatedBytesChanged;
        }

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
                profile.LastUsed = Interlocked.Increment(ref _globalUsageStamp);
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
                    profile.LastUsed = Interlocked.Increment(ref _globalUsageStamp);
                    return profile;
                }

                profile.Dispose();
                profiles.Remove(scaleProfile.Key);
            }

            profile = new Profile(geometry, scaleProfile.AnchorScale, AdjustEstimatedBytes)
            {
                LastUsed = Interlocked.Increment(ref _globalUsageStamp)
            };
            profiles.Add(scaleProfile.Key, profile);
            TrimProfiles(profiles, scaleProfile.Key);
            return profile;
        }

        public void ClearStroke()
        {
            DisposeProfiles(_strokeProfiles);
        }

        public int TrimToBudget(Profile protectedProfile)
        {
            var evictionCount = 0;
            while (EstimatedBytes > CacheBudgetBytes)
            {
                Dictionary<int, Profile>? candidateProfiles = null;
                var candidateKey = 0;
                Profile? candidateProfile = null;
                FindOldestCandidate(
                    _fillProfiles,
                    protectedProfile,
                    ref candidateProfiles,
                    ref candidateKey,
                    ref candidateProfile);
                FindOldestCandidate(
                    _strokeProfiles,
                    protectedProfile,
                    ref candidateProfiles,
                    ref candidateKey,
                    ref candidateProfile);
                if (candidateProfiles is null || candidateProfile is null)
                    break;

                candidateProfile.Dispose();
                candidateProfiles.Remove(candidateKey);
                evictionCount++;
            }

            return evictionCount;
        }

        public bool TryGetOldestProfile(out Profile profile)
        {
            Profile? oldest = null;
            FindOldestProfile(_fillProfiles, ref oldest);
            FindOldestProfile(_strokeProfiles, ref oldest);
            profile = oldest!;
            return oldest is not null;
        }

        public bool EvictProfile(Profile profile)
        {
            if (TryRemoveProfile(_fillProfiles, profile) ||
                TryRemoveProfile(_strokeProfiles, profile))
            {
                profile.Dispose();
                return true;
            }

            return false;
        }

        private static void FindOldestCandidate(
            Dictionary<int, Profile> profiles,
            Profile protectedProfile,
            ref Dictionary<int, Profile>? candidateProfiles,
            ref int candidateKey,
            ref Profile? candidateProfile)
        {
            foreach (var pair in profiles)
            {
                if (ReferenceEquals(pair.Value, protectedProfile) ||
                    candidateProfile is not null &&
                    pair.Value.LastUsed >= candidateProfile.LastUsed)
                {
                    continue;
                }

                candidateProfiles = profiles;
                candidateKey = pair.Key;
                candidateProfile = pair.Value;
            }
        }

        private static void FindOldestProfile(
            IReadOnlyDictionary<int, Profile> profiles,
            ref Profile? oldest)
        {
            foreach (var candidate in profiles.Values)
            {
                if (candidate.EstimatedBytes <= 0 ||
                    oldest is not null && candidate.LastUsed >= oldest.LastUsed)
                {
                    continue;
                }

                oldest = candidate;
            }
        }

        private static bool TryRemoveProfile(
            Dictionary<int, Profile> profiles,
            Profile profile)
        {
            int? key = null;
            foreach (var pair in profiles)
            {
                if (ReferenceEquals(pair.Value, profile))
                {
                    key = pair.Key;
                    break;
                }
            }

            return key is not null && profiles.Remove(key.Value);
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

        private void AdjustEstimatedBytes(long delta)
        {
            _estimatedBytes = Math.Max(0, _estimatedBytes + delta);
            _estimatedBytesChanged(delta);
        }

        public void Dispose() => Clear();
    }

    internal sealed class Profile : IDisposable
    {
        private readonly Action<long> _estimatedBytesChanged;
        private long _fillEstimatedBytes;
        private long _strokeEstimatedBytes;

        public ID2D1Geometry Geometry { get; }
        public double AnchorScale { get; }
        public long LastUsed { get; set; }
        public ID2D1GeometryRealization? Fill { get; set; }
        public ID2D1GeometryRealization? Stroke { get; set; }
        public long FillEstimatedBytes
        {
            get => _fillEstimatedBytes;
            set
            {
                var normalized = Math.Max(0, value);
                var delta = normalized - _fillEstimatedBytes;
                _fillEstimatedBytes = normalized;
                _estimatedBytesChanged(delta);
            }
        }
        public long StrokeEstimatedBytes
        {
            get => _strokeEstimatedBytes;
            set
            {
                var normalized = Math.Max(0, value);
                var delta = normalized - _strokeEstimatedBytes;
                _strokeEstimatedBytes = normalized;
                _estimatedBytesChanged(delta);
            }
        }
        public long EstimatedBytes => FillEstimatedBytes + StrokeEstimatedBytes;
        public float StrokeWidth { get; set; }
        public Direct2DStrokeRealizationStyleKey StrokeStyleKey { get; set; }

        public Profile(
            ID2D1Geometry geometry,
            double anchorScale,
            Action<long> estimatedBytesChanged)
        {
            Geometry = geometry;
            AnchorScale = anchorScale;
            _estimatedBytesChanged = estimatedBytesChanged;
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
            StrokeEstimatedBytes = 0;
            StrokeWidth = 0.0f;
            StrokeStyleKey = default;
        }

        public void Dispose()
        {
            Fill?.Dispose();
            Fill = null;
            FillEstimatedBytes = 0;
            ClearStroke();
        }
    }
}

internal readonly record struct Direct2DGeometryRealizationStatistics(
    int FillDrawCount,
    int StrokeDrawCount,
    int BuildCount,
    int FallbackCount,
    int CacheEvictionCount);
