using Direct2dCad.Db;
using Direct2dCad.Indexing;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;

namespace Direct2dCad.Benchmarks;

internal sealed class BenchmarkRenderSession : IDisposable
{
    public const int SurfaceWidth = 1600;
    public const int SurfaceHeight = 900;

    public BenchmarkDocumentData Data { get; }
    public CadViewport Viewport { get; }
    public CadSpatialIndex SpatialIndex { get; }
    public Direct2DImageRenderHost RenderHost { get; }
    public BenchmarkImageSource ImageSource { get; private set; }

    public BenchmarkRenderSession(
        BenchmarkDocumentData data,
        bool levelOfDetail = false,
        BlockId? activeOwnerBlockId = null,
        LayoutId? activeLayoutId = null,
        LayoutViewportId? activeLayoutViewportId = null,
        CadParallelRenderingMode? parallelRenderingMode = null,
        int parallelWorkerCount = 2)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        Viewport = BenchmarkDocumentFactory.CreateFittedViewport(
            data,
            SurfaceWidth,
            SurfaceHeight);
        SpatialIndex = new CadSpatialIndex();
        ImageSource = new BenchmarkImageSource(SurfaceWidth, SurfaceHeight);
        RenderHost = new Direct2DImageRenderHost();
        RenderHost.AttachImageSource(ImageSource);
        RenderHost.SetSize(SurfaceWidth, SurfaceHeight);
        RenderHost.SetScene(data.Document, Viewport);

        RenderHost.UpdateTextMeasurements(data.Document);
        SpatialIndex.Rebuild(data.Document);
        RenderHost.SetRenderOptions(new CadRenderOptions
        {
            ActiveOwnerBlockId = activeOwnerBlockId ?? BlockId.ModelSpace,
            ActiveLayoutId = activeLayoutId,
            ActiveLayoutViewportId = activeLayoutViewportId,
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = false,
            IsLevelOfDetailEnabled = levelOfDetail,
            IsParallelRenderingEnabled = parallelRenderingMode.HasValue,
            ParallelRenderingMode = parallelRenderingMode ??
                CadParallelRenderingMode.MultipleDevices,
            ParallelRenderingWorkerCount = Math.Clamp(parallelWorkerCount, 2, 4),
            ParallelRenderingEntityThreshold = 2,
            EntityBoundsQueryInto = (ownerBlockId, bounds, results) =>
                SpatialIndex.Query(ownerBlockId, bounds, results)
        });
        RenderHost.RebuildAll(data.Document);
    }

    public void WarmUp(int frameCount = 2)
    {
        for (var index = 0; index < frameCount; index++)
            RenderHost.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
    }

    public long ReattachSurfaceAndRenderFirstFrame()
    {
        ImageSource = new BenchmarkImageSource(SurfaceWidth, SurfaceHeight);
        RenderHost.AttachImageSource(ImageSource);
        RenderHost.SetSize(SurfaceWidth, SurfaceHeight);
        RenderHost.Render(CadRenderInvalidation.Full, baseSceneChanged: true);
        return CaptureFrameChecksum();
    }

    public long CaptureFrameChecksum()
    {
        var statistics = RenderHost.RenderStatistics;
        return statistics.EntitySubmissionCount +
               statistics.VisibleEntityCount +
               statistics.CommandListReplayCount +
               statistics.CommandListBuildCount +
               statistics.TileReplayCount +
               statistics.TileBuildCount +
               statistics.BlockDefinitionCommandListReplayCount +
               statistics.BlockDefinitionCommandListBuildCount +
               statistics.GeometryRealizationBuildCount +
               statistics.HatchLineSubmissionCount +
               statistics.OleTileBuildCount +
               ImageSource.PresentCount +
               ImageSource.DirtyRectCount;
    }

    public void Dispose() => RenderHost.Dispose();
}
