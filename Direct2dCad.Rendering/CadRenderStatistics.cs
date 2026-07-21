namespace Direct2dCad.Rendering;

public sealed record CadRenderStatistics(
    bool IsFullFrame,
    int DirtyRegionCount,
    int ScenePassCount,
    int VisibleEntityCount,
    int EntitySubmissionCount,
    int BlockReferenceCount,
    int ExpandedBlockEntityCount,
    int SelectionEntityCount,
    int CommandListReplayCount,
    int CommandListBuildCount,
    int TileReplayCount,
    int TileBuildCount,
    int FallbackEntityCount,
    double RenderDurationMilliseconds)
{
    public static CadRenderStatistics Empty { get; } = new(
        IsFullFrame: true,
        DirtyRegionCount: 0,
        ScenePassCount: 0,
        VisibleEntityCount: 0,
        EntitySubmissionCount: 0,
        BlockReferenceCount: 0,
        ExpandedBlockEntityCount: 0,
        SelectionEntityCount: 0,
        CommandListReplayCount: 0,
        CommandListBuildCount: 0,
        TileReplayCount: 0,
        TileBuildCount: 0,
        FallbackEntityCount: 0,
        RenderDurationMilliseconds: 0);
}
