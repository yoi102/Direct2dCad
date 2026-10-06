using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Tests;

internal sealed class TestRenderSession : ICadRenderSession, ICadGeometryResourceManager
{
    public ICadGeometryResourceManager GeometryResourceManager => this;
    public int TargetWidth { get; private set; }
    public int TargetHeight { get; private set; }
    public double FramesPerSecond => 0;
    public double AverageFrameRenderTimeMilliseconds => 0;
    public CadRenderStatistics RenderStatistics => CadRenderStatistics.Empty;
    public CadRenderResourceStatistics ResourceStatistics => default;
    public CadRenderResourceStatistics ProcessResourceStatistics => default;
    public bool IsViewportInteractionActive => false;
    public bool IsInitialViewReady => true;
    public bool HasPresentedScene => false;
    public bool UsingWarp => false;
    public bool IsDisposed { get; private set; }
    public int DisposeCount { get; private set; }
    public Exception? OleCallbackFailure { get; set; }
    public Action? DisposalCallback { get; set; }
    public int RenderCount { get; private set; }
    public event EventHandler? RenderCacheBuildRequested { add { } remove { } }
    public void SetScene(CadDocument document, CadViewport viewport) { }
    public void SetTransientScene(CadTransientScene? scene) { }
    public void SetHandleScene(CadHandleScene? scene) { }
    public void SetRenderOptions(CadRenderOptions? options) { }
    public void SetGraphicsDeviceMode(CadGraphicsDeviceMode mode) { }
    public void SetSize(int width, int height) => (TargetWidth, TargetHeight) = (width, height);
    public bool BeginViewportInteraction() => false;
    public bool RenderViewportInteractionPreview() => false;
    public void EndViewportInteraction() { }
    public void Render(CadRenderInvalidation? invalidation = null, bool baseSceneChanged = true) => RenderCount++;
    public bool PrepareRenderCacheStep() => false;
    public void SetOleDrawCallback(CadOleRenderCallback? callback)
    {
        if (OleCallbackFailure is not null) throw OleCallbackFailure;
    }
    public void SetOleReleaseCallback(CadOleReleaseCallback? callback) { }
    public void InvalidateOleBitmap(EntityId entityId) { }
    public IReadOnlyList<CadTextBoundsMeasurement> MeasurePendingTextBounds(CadDocument document) => [];
    public bool TryMeasureTextBounds(CadDocument document, string text, CadPointD position,
        double height, StyleId? textStyleId, out CadRectD bounds) { bounds = CadRectD.Empty; return false; }
    public void RebuildAll(CadDocument document) { }
    public void ApplyChanges(CadDocument document, CadDocumentChangeSet changes) { }
    public void RebuildEntity(CadDocument document, EntityId entityId) { }
    public void RemoveEntity(EntityId entityId) { }
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        DisposeCount++;
        DisposalCallback?.Invoke();
    }
}

internal sealed class TestRenderSessionFactory : ICadRenderSessionFactory
{
    public ICadRenderSession Create() => new TestRenderSession();
}
