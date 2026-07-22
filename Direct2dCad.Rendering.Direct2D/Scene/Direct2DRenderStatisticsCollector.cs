using Direct2dCad.Rendering.Direct2D.Resources;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal sealed class Direct2DRenderStatisticsCollector
{
    private bool _isFullFrame;
    private bool _isFrameActive;
    private int _dirtyRegionCount;
    private int _pendingCommandListBuildCount;
    private int _pendingSelectionCommandListBuildCount;
    private int _pendingTileBuildCount;
    private long _pendingHatchLineSubmissionCount;
    private int _pendingHatchSimplifiedLineFamilyCount;
    private int _pendingOleTileBuildCount;

    public int ScenePassCount { get; private set; }
    public int VisibleEntityCount { get; private set; }
    public int EntitySubmissionCount { get; private set; }
    public int BlockReferenceCount { get; private set; }
    public int ExpandedBlockEntityCount { get; private set; }
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
        _pendingCommandListBuildCount = 0;
        _pendingSelectionCommandListBuildCount = 0;
        _pendingTileBuildCount = 0;
        _pendingHatchLineSubmissionCount = 0;
        _pendingHatchSimplifiedLineFamilyCount = 0;
        _pendingOleTileBuildCount = 0;
    }

    public void EndFrame() => _isFrameActive = false;

    public void RecordScenePass() => ScenePassCount++;
    public void RecordVisibleEntity() => VisibleEntityCount++;
    public void RecordVisibleEntities(int count) => VisibleEntityCount += Math.Max(0, count);
    public void RecordEntitySubmission() => EntitySubmissionCount++;
    public void RecordBlockReference() => BlockReferenceCount++;
    public void RecordExpandedBlockEntity() => ExpandedBlockEntityCount++;
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
    public void RecordGeometryRealizations(
        Direct2DGeometryRealizationStatistics statistics)
    {
        GeometryRealizationFillDrawCount += statistics.FillDrawCount;
        GeometryRealizationStrokeDrawCount += statistics.StrokeDrawCount;
        GeometryRealizationBuildCount += statistics.BuildCount;
        GeometryRealizationFallbackCount += statistics.FallbackCount;
    }

    public CadRenderStatistics Snapshot(double renderDurationMilliseconds = 0) => new(
        _isFullFrame,
        _dirtyRegionCount,
        ScenePassCount,
        VisibleEntityCount,
        EntitySubmissionCount,
        BlockReferenceCount,
        ExpandedBlockEntityCount,
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
        renderDurationMilliseconds);
}
