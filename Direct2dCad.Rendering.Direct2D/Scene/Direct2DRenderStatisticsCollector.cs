namespace Direct2dCad.Rendering.Direct2D.Scene;

internal sealed class Direct2DRenderStatisticsCollector
{
    private bool _isFullFrame;
    private bool _isFrameActive;
    private int _dirtyRegionCount;
    private int _pendingCommandListBuildCount;
    private int _pendingTileBuildCount;

    public int ScenePassCount { get; private set; }
    public int VisibleEntityCount { get; private set; }
    public int EntitySubmissionCount { get; private set; }
    public int BlockReferenceCount { get; private set; }
    public int ExpandedBlockEntityCount { get; private set; }
    public int SelectionEntityCount { get; private set; }
    public int CommandListReplayCount { get; private set; }
    public int CommandListBuildCount { get; private set; }
    public int TileReplayCount { get; private set; }
    public int TileBuildCount { get; private set; }
    public int FallbackEntityCount { get; private set; }

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
        CommandListReplayCount = 0;
        CommandListBuildCount = _pendingCommandListBuildCount;
        TileReplayCount = 0;
        TileBuildCount = _pendingTileBuildCount;
        FallbackEntityCount = 0;
        _pendingCommandListBuildCount = 0;
        _pendingTileBuildCount = 0;
    }

    public void EndFrame() => _isFrameActive = false;

    public void RecordScenePass() => ScenePassCount++;
    public void RecordVisibleEntity() => VisibleEntityCount++;
    public void RecordVisibleEntities(int count) => VisibleEntityCount += Math.Max(0, count);
    public void RecordEntitySubmission() => EntitySubmissionCount++;
    public void RecordBlockReference() => BlockReferenceCount++;
    public void RecordExpandedBlockEntity() => ExpandedBlockEntityCount++;
    public void RecordSelectionEntity() => SelectionEntityCount++;
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

    public CadRenderStatistics Snapshot(double renderDurationMilliseconds = 0) => new(
        _isFullFrame,
        _dirtyRegionCount,
        ScenePassCount,
        VisibleEntityCount,
        EntitySubmissionCount,
        BlockReferenceCount,
        ExpandedBlockEntityCount,
        SelectionEntityCount,
        CommandListReplayCount,
        CommandListBuildCount,
        TileReplayCount,
        TileBuildCount,
        FallbackEntityCount,
        renderDurationMilliseconds);
}
