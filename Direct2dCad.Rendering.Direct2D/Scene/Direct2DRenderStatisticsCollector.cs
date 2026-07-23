using Direct2dCad.Rendering.Direct2D.Resources;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal sealed class Direct2DRenderStatisticsCollector
{
    private bool _isFullFrame;
    private bool _isFrameActive;
    private int _dirtyRegionCount;
    private int _pendingCommandListBuildCount;
    private int _pendingBlockDefinitionCommandListBuildCount;
    private int _pendingSelectionCommandListBuildCount;
    private int _pendingTileBuildCount;
    private long _pendingHatchLineSubmissionCount;
    private int _pendingHatchSimplifiedLineFamilyCount;
    private int _pendingOleTileBuildCount;
    private int _pendingGpuCacheEvictionCount;
    private long _gpuCachePeakBytes;

    public int ScenePassCount { get; private set; }
    public int VisibleEntityCount { get; private set; }
    public int EntitySubmissionCount { get; private set; }
    public int BlockReferenceCount { get; private set; }
    public int ExpandedBlockEntityCount { get; private set; }
    public int BlockDefinitionCommandListReplayCount { get; private set; }
    public int BlockDefinitionCommandListBuildCount { get; private set; }
    public int SelectionEntityCount { get; private set; }
    public int SelectionCommandListReplayCount { get; private set; }
    public int SelectionCommandListBuildCount { get; private set; }
    public int CommandListReplayCount { get; private set; }
    public int CommandListBuildCount { get; private set; }
    public int TileReplayCount { get; private set; }
    public int TileBuildCount { get; private set; }
    public int FallbackEntityCount { get; private set; }
    public int GeometryRealizationFillDrawCount { get; private set; }
    public int GeometryRealizationStrokeDrawCount { get; private set; }
    public int GeometryRealizationBuildCount { get; private set; }
    public int GeometryRealizationFallbackCount { get; private set; }
    public long HatchLineSubmissionCount { get; private set; }
    public int HatchSimplifiedLineFamilyCount { get; private set; }
    public int OleTileBuildCount { get; private set; }
    public long SceneTileCacheBytes { get; private set; }
    public long CommandListCacheBytes { get; private set; }
    public long SelectionCommandListCacheBytes { get; private set; }
    public long BlockDefinitionCacheBytes { get; private set; }
    public long GeometryRealizationCacheBytes { get; private set; }
    public long HatchTileCacheBytes { get; private set; }
    public long ImageBitmapCacheBytes { get; private set; }
    public long OleTileCacheBytes { get; private set; }
    public long GpuCacheBytes { get; private set; }
    public long GpuCachePeakBytes => _gpuCachePeakBytes;
    public long GpuCacheBudgetBytes { get; private set; }
    public int GpuCacheEvictionCount { get; private set; }
    public double CachePreparationMilliseconds { get; private set; }
    public double BackgroundRenderMilliseconds { get; private set; }
    public double EntityRenderMilliseconds { get; private set; }
    public double TransientRenderMilliseconds { get; private set; }
    public double SelectionRenderMilliseconds { get; private set; }
    public double OlePreparationMilliseconds { get; private set; }
    public double SurfaceDrawMilliseconds { get; private set; }

    public void BeginFrame(bool isFullFrame, int dirtyRegionCount)
    {
        _isFrameActive = true;
        _isFullFrame = isFullFrame;
        _dirtyRegionCount = Math.Max(0, dirtyRegionCount);
        ScenePassCount = 0;
        VisibleEntityCount = 0;
        EntitySubmissionCount = 0;
        BlockReferenceCount = 0;
        ExpandedBlockEntityCount = 0;
        BlockDefinitionCommandListReplayCount = 0;
        BlockDefinitionCommandListBuildCount = _pendingBlockDefinitionCommandListBuildCount;
        SelectionEntityCount = 0;
        SelectionCommandListReplayCount = 0;
        SelectionCommandListBuildCount = _pendingSelectionCommandListBuildCount;
        CommandListReplayCount = 0;
        CommandListBuildCount = _pendingCommandListBuildCount;
        TileReplayCount = 0;
        TileBuildCount = _pendingTileBuildCount;
        FallbackEntityCount = 0;
        GeometryRealizationFillDrawCount = 0;
        GeometryRealizationStrokeDrawCount = 0;
        GeometryRealizationBuildCount = 0;
        GeometryRealizationFallbackCount = 0;
        HatchLineSubmissionCount = _pendingHatchLineSubmissionCount;
        HatchSimplifiedLineFamilyCount = _pendingHatchSimplifiedLineFamilyCount;
        OleTileBuildCount = _pendingOleTileBuildCount;
        GpuCacheEvictionCount = _pendingGpuCacheEvictionCount;
        CachePreparationMilliseconds = 0;
        BackgroundRenderMilliseconds = 0;
        EntityRenderMilliseconds = 0;
        TransientRenderMilliseconds = 0;
        SelectionRenderMilliseconds = 0;
        OlePreparationMilliseconds = 0;
        SurfaceDrawMilliseconds = 0;
        _pendingCommandListBuildCount = 0;
        _pendingBlockDefinitionCommandListBuildCount = 0;
        _pendingSelectionCommandListBuildCount = 0;
        _pendingTileBuildCount = 0;
        _pendingHatchLineSubmissionCount = 0;
        _pendingHatchSimplifiedLineFamilyCount = 0;
        _pendingOleTileBuildCount = 0;
        _pendingGpuCacheEvictionCount = 0;
    }

    public void EndFrame() => _isFrameActive = false;

    public void RecordScenePass() => ScenePassCount++;
    public void RecordVisibleEntity() => VisibleEntityCount++;
    public void RecordVisibleEntities(int count) => VisibleEntityCount += Math.Max(0, count);
    public void RecordEntitySubmission() => EntitySubmissionCount++;
    public void RecordBlockReference() => BlockReferenceCount++;
    public void RecordExpandedBlockEntity() => ExpandedBlockEntityCount++;
    public void RecordExpandedBlockEntities(int count) =>
        ExpandedBlockEntityCount += Math.Max(0, count);
    public void RecordBlockDefinitionCommandListReplay() =>
        BlockDefinitionCommandListReplayCount++;
    public void RecordBlockDefinitionCommandListBuild()
    {
        if (_isFrameActive)
            BlockDefinitionCommandListBuildCount++;
        else
            _pendingBlockDefinitionCommandListBuildCount++;
    }
    public void RecordSelectionEntity() => SelectionEntityCount++;
    public void RecordSelectionEntities(int count) => SelectionEntityCount += Math.Max(0, count);
    public void RecordSelectionCommandListReplay() => SelectionCommandListReplayCount++;
    public void RecordSelectionCommandListBuild()
    {
        if (_isFrameActive)
            SelectionCommandListBuildCount++;
        else
            _pendingSelectionCommandListBuildCount++;
    }
    public void RecordCommandListReplay() => CommandListReplayCount++;
    public void RecordCommandListBuild()
    {
        if (_isFrameActive)
            CommandListBuildCount++;
        else
            _pendingCommandListBuildCount++;
    }
    public void RecordTileReplay() => TileReplayCount++;
    public void RecordTileBuild()
    {
        if (_isFrameActive)
            TileBuildCount++;
        else
            _pendingTileBuildCount++;
    }
    public void RecordFallbackEntity() => FallbackEntityCount++;
    public void RecordHatchLineSubmissions(int count)
    {
        if (count <= 0)
            return;
        if (_isFrameActive)
            HatchLineSubmissionCount += count;
        else
            _pendingHatchLineSubmissionCount += count;
    }
    public void RecordHatchSimplifiedLineFamily()
    {
        if (_isFrameActive)
            HatchSimplifiedLineFamilyCount++;
        else
            _pendingHatchSimplifiedLineFamilyCount++;
    }
    public void RecordOleTileBuild()
    {
        if (_isFrameActive)
            OleTileBuildCount++;
        else
            _pendingOleTileBuildCount++;
    }
    public void RecordGpuCacheEviction(int count = 1)
    {
        if (count <= 0)
            return;
        if (_isFrameActive)
            GpuCacheEvictionCount += count;
        else
            _pendingGpuCacheEvictionCount += count;
    }
    public void RecordCachePreparation(double milliseconds) =>
        CachePreparationMilliseconds += NormalizeDuration(milliseconds);
    public void RecordBackgroundRender(double milliseconds) =>
        BackgroundRenderMilliseconds += NormalizeDuration(milliseconds);
    public void RecordEntityRender(double milliseconds) =>
        EntityRenderMilliseconds += NormalizeDuration(milliseconds);
    public void RecordTransientRender(double milliseconds) =>
        TransientRenderMilliseconds += NormalizeDuration(milliseconds);
    public void RecordSelectionRender(double milliseconds) =>
        SelectionRenderMilliseconds += NormalizeDuration(milliseconds);
    public void RecordOlePreparation(double milliseconds) =>
        OlePreparationMilliseconds += NormalizeDuration(milliseconds);
    public void RecordSurfaceDraw(double milliseconds) =>
        SurfaceDrawMilliseconds += NormalizeDuration(milliseconds);
    public void SetGpuCacheMemory(
        long sceneTileBytes,
        long commandListBytes,
        long selectionCommandListBytes,
        long blockDefinitionBytes,
        long geometryRealizationBytes,
        long hatchTileBytes,
        long imageBitmapBytes,
        long oleTileBytes,
        long budgetBytes)
    {
        SceneTileCacheBytes = Math.Max(0, sceneTileBytes);
        CommandListCacheBytes = Math.Max(0, commandListBytes);
        SelectionCommandListCacheBytes = Math.Max(0, selectionCommandListBytes);
        BlockDefinitionCacheBytes = Math.Max(0, blockDefinitionBytes);
        GeometryRealizationCacheBytes = Math.Max(0, geometryRealizationBytes);
        HatchTileCacheBytes = Math.Max(0, hatchTileBytes);
        ImageBitmapCacheBytes = Math.Max(0, imageBitmapBytes);
        OleTileCacheBytes = Math.Max(0, oleTileBytes);
        GpuCacheBytes = SceneTileCacheBytes +
                        CommandListCacheBytes +
                        SelectionCommandListCacheBytes +
                        BlockDefinitionCacheBytes +
                        GeometryRealizationCacheBytes +
                        HatchTileCacheBytes +
                        ImageBitmapCacheBytes +
                        OleTileCacheBytes;
        _gpuCachePeakBytes = Math.Max(_gpuCachePeakBytes, GpuCacheBytes);
        GpuCacheBudgetBytes = Math.Max(0, budgetBytes);
    }
    public void RecordGeometryRealizations(
        Direct2DGeometryRealizationStatistics statistics)
    {
        GeometryRealizationFillDrawCount += statistics.FillDrawCount;
        GeometryRealizationStrokeDrawCount += statistics.StrokeDrawCount;
        GeometryRealizationBuildCount += statistics.BuildCount;
        GeometryRealizationFallbackCount += statistics.FallbackCount;
        RecordGpuCacheEviction(statistics.CacheEvictionCount);
    }

    public CadRenderStatistics Snapshot(double renderDurationMilliseconds = 0) => new(
        _isFullFrame,
        _dirtyRegionCount,
        ScenePassCount,
        VisibleEntityCount,
        EntitySubmissionCount,
        BlockReferenceCount,
        ExpandedBlockEntityCount,
        BlockDefinitionCommandListReplayCount,
        BlockDefinitionCommandListBuildCount,
        SelectionEntityCount,
        SelectionCommandListReplayCount,
        SelectionCommandListBuildCount,
        CommandListReplayCount,
        CommandListBuildCount,
        TileReplayCount,
        TileBuildCount,
        FallbackEntityCount,
        GeometryRealizationFillDrawCount,
        GeometryRealizationStrokeDrawCount,
        GeometryRealizationBuildCount,
        GeometryRealizationFallbackCount,
        HatchLineSubmissionCount,
        HatchSimplifiedLineFamilyCount,
        OleTileBuildCount,
        SceneTileCacheBytes,
        CommandListCacheBytes,
        SelectionCommandListCacheBytes,
        BlockDefinitionCacheBytes,
        GeometryRealizationCacheBytes,
        HatchTileCacheBytes,
        ImageBitmapCacheBytes,
        OleTileCacheBytes,
        GpuCacheBytes,
        GpuCachePeakBytes,
        GpuCacheBudgetBytes,
        GpuCacheEvictionCount,
        CachePreparationMilliseconds,
        BackgroundRenderMilliseconds,
        EntityRenderMilliseconds,
        TransientRenderMilliseconds,
        SelectionRenderMilliseconds,
        OlePreparationMilliseconds,
        SurfaceDrawMilliseconds,
        renderDurationMilliseconds);

    private static double NormalizeDuration(double milliseconds) =>
        double.IsFinite(milliseconds) ? Math.Max(0.0, milliseconds) : 0.0;
}
