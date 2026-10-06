using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.Rendering;

/// <summary>A document-owned render session. The platform composes the backend and presentation surface.</summary>
public interface ICadRenderSession : ICadTextMetrics, IDisposable
{
    ICadGeometryResourceManager GeometryResourceManager { get; }
    int TargetWidth { get; }
    int TargetHeight { get; }
    double FramesPerSecond { get; }
    double AverageFrameRenderTimeMilliseconds { get; }
    CadRenderStatistics RenderStatistics { get; }
    CadRenderResourceStatistics ResourceStatistics { get; }
    CadRenderResourceStatistics ProcessResourceStatistics { get; }
    bool IsViewportInteractionActive { get; }
    bool IsInitialViewReady { get; }
    bool HasPresentedScene { get; }
    bool UsingWarp { get; }
    event EventHandler? RenderCacheBuildRequested;
    void SetScene(CadDocument document, CadViewport viewport);
    void SetTransientScene(CadTransientScene? scene);
    void SetHandleScene(CadHandleScene? scene);
    void SetRenderOptions(CadRenderOptions? options);
    void SetGraphicsDeviceMode(CadGraphicsDeviceMode mode);
    void SetSize(int width, int height);
    bool BeginViewportInteraction();
    bool RenderViewportInteractionPreview();
    void EndViewportInteraction();
    void Render(CadRenderInvalidation? invalidation = null, bool baseSceneChanged = true);
    bool PrepareRenderCacheStep();
    void SetOleDrawCallback(CadOleRenderCallback? callback);
    void SetOleReleaseCallback(CadOleReleaseCallback? callback);
    void InvalidateOleBitmap(EntityId entityId);
}

public interface ICadTextMetrics
{
    IReadOnlyList<CadTextBoundsMeasurement> MeasurePendingTextBounds(CadDocument document);
    bool TryMeasureTextBounds(CadDocument document, string text, CadPointD position,
        double height, StyleId? textStyleId, out CadRectD bounds);
}

/// <summary>The caller owns and disposes each returned document session.</summary>
public interface ICadRenderSessionFactory
{
    ICadRenderSession Create();
}
