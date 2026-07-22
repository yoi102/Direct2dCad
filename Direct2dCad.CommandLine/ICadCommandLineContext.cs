namespace Direct2dCad.CommandLine;

public interface ICadCommandLineContext
{
    string DocumentName { get; }
    int EntityCount { get; }
    int SelectionCount { get; }
    CadCommandLineDrawingMode ToolMode { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    CadCommandLinePoint? LastInputPoint { get; }

    void SetToolMode(CadCommandLineDrawingMode mode);
    void Cancel();
    void Undo();
    void Redo();
    void FitToWindow();
    int SelectAll();
    int DeleteSelection();
    CadCommandLineClipboardSummary? CopySelection();
    CadCommandLineClipboardSummary? BeginPaste();
    bool SubmitDrawingPoint(CadCommandLinePoint point);
    bool CompleteCurrentDrawing();
    CadCommandLineRenderStatistics? GetRenderStatistics();
}

public sealed record CadCommandLineClipboardSummary(
    int EntityCount,
    int BlockReferenceCount,
    int BlockDefinitionCount);

public sealed record CadCommandLineRenderStatistics(
    double FramesPerSecond,
    double AverageFrameMilliseconds,
    double LastRenderMilliseconds,
    bool IsFullFrame,
    int DirtyRegionCount,
    int ScenePassCount,
    int VisibleEntityCount,
    int EntitySubmissionCount,
    int BlockReferenceCount,
    int ExpandedBlockEntityCount,
    int BlockDefinitionCommandListReplayCount,
    int BlockDefinitionCommandListBuildCount,
    int SelectionEntityCount,
    int SelectionCommandListReplayCount,
    int SelectionCommandListBuildCount,
    int CommandListReplayCount,
    int CommandListBuildCount,
    int TileReplayCount,
    int TileBuildCount,
    int FallbackEntityCount,
    int GeometryRealizationFillDrawCount,
    int GeometryRealizationStrokeDrawCount,
    int GeometryRealizationBuildCount,
    int GeometryRealizationFallbackCount,
    long HatchLineSubmissionCount,
    int HatchSimplifiedLineFamilyCount,
    int OleTileBuildCount,
    long SceneTileCacheBytes,
    long CommandListCacheBytes,
    long SelectionCommandListCacheBytes,
    long BlockDefinitionCacheBytes,
    long GeometryRealizationCacheBytes,
    long HatchTileCacheBytes,
    long ImageBitmapCacheBytes,
    long OleTileCacheBytes,
    long GpuCacheBytes,
    long GpuCachePeakBytes,
    long GpuCacheBudgetBytes,
    int GpuCacheEvictionCount);
