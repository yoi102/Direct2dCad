using System.Diagnostics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Overlays;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal sealed class Direct2DLayoutRenderer(
    Direct2DStyleResourceCache styleResources,
    Direct2DRenderStatisticsCollector statistics,
    Direct2DSelectionRenderer selectionRenderer,
    Action<ID2D1DeviceContext, CadDocument, CadViewport, CadRenderOptions, CadTransientScene?, CadHandleScene?> drawScene,
    Action<ID2D1DeviceContext, CadDocument, CadViewport, CadTransientScene?, CadRenderOptions> drawTransients)
{
    private readonly Direct2DStyleResourceCache _styleResources = styleResources;
    private readonly Direct2DRenderStatisticsCollector _statistics = statistics;
    private readonly Direct2DSelectionRenderer _selectionRenderer = selectionRenderer;
    private readonly Action<ID2D1DeviceContext, CadDocument, CadViewport, CadRenderOptions, CadTransientScene?, CadHandleScene?> _drawScene = drawScene;
    private readonly Action<ID2D1DeviceContext, CadDocument, CadViewport, CadTransientScene?, CadRenderOptions> _drawTransients = drawTransients;

    public void DrawPaper(
        ID2D1DeviceContext context,
        CadLayout layout,
        bool drawLayoutGuides)
    {
        var bounds = ToRawRect(layout.PaperBounds);
        var paperBrush = _styleResources.GetBrush(context, layout.PaperColor);
        context.FillRectangle(bounds, paperBrush);
        if (!drawLayoutGuides)
            return;

        var edgeBrush = _styleResources.GetBrush(context, CadColor.FromRgb(64, 64, 64));
        var marginBrush = _styleResources.GetBrush(context, CadColor.FromArgb(217, 115, 115, 115));
        context.DrawRectangle(bounds, edgeBrush, 1f / Math.Max((float)CadEditorZoom(context), 1e-6f));
        context.DrawRectangle(
            ToRawRect(layout.PrintableBounds),
            marginBrush,
            0.75f / Math.Max((float)CadEditorZoom(context), 1e-6f));
    }

    public void DrawLayoutViewportsBase(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport paperViewport,
        CadLayout layout,
        CadTransientScene? transientScene,
        CadHandleScene? handleScene,
        CadRenderOptions options)
    {
        var paperTransform = context.Transform;
        var borderBrush = _styleResources.GetBrush(context, CadColor.FromArgb(230, 51, 115, 204));

        foreach (var layoutViewport in layout.Viewports)
        {
            if (!layoutViewport.IsVisible)
                continue;

            if (options.DirtyWorldBounds is { IsEmpty: false } dirty &&
                !layoutViewport.Bounds.Inflate(3.0 / Math.Max(paperViewport.Zoom, double.Epsilon)).Intersects(dirty))
                continue;

            context.Transform = paperTransform;
            context.PushAxisAlignedClip(ToRawRect(layoutViewport.Bounds), AntialiasMode.PerPrimitive);
            try
            {
                var modelToPaper = CreateModelToPaperTransform(layoutViewport);
                context.Transform = modelToPaper * paperTransform;

                var modelViewport = CreateModelViewport(paperViewport, layoutViewport);
                var isActiveViewport = options.ActiveLayoutViewportId == layoutViewport.Id;
                var modelOptions = CreateModelViewportOptions(
                    options,
                    layoutViewport,
                    includeHiddenEntities: isActiveViewport);

                var entityStarted = Stopwatch.GetTimestamp();
                try
                {
                    _drawScene(
                        context,
                        document,
                        modelViewport,
                        modelOptions,
                        isActiveViewport ? transientScene : null,
                        isActiveViewport ? handleScene : null);
                }
                finally
                {
                    _statistics.RecordEntityRender(ElapsedMilliseconds(entityStarted));
                }

            }
            finally
            {
                context.PopAxisAlignedClip();
                context.Transform = paperTransform;
            }

            if (options.DrawLayoutGuides)
            {
                context.DrawRectangle(
                    ToRawRect(layoutViewport.Bounds),
                    borderBrush,
                    (options.ActiveLayoutViewportId == layoutViewport.Id ? 2f : 1f) /
                    Math.Max((float)paperViewport.Zoom, 1e-6f));
            }
        }
    }

    public void DrawLayoutViewportOverlays(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport paperViewport,
        CadLayout layout,
        CadTransientScene? transientScene,
        CadHandleScene? handleScene,
        CadRenderOptions options)
    {
        if (options.ActiveLayoutViewportId is not { } activeViewportId)
            return;

        var layoutViewport = layout.Viewports.FirstOrDefault(
            viewport => viewport.Id == activeViewportId && viewport.IsVisible);
        if (layoutViewport is null)
            return;

        var paperTransform = context.Transform;
        context.PushAxisAlignedClip(ToRawRect(layoutViewport.Bounds), AntialiasMode.PerPrimitive);
        try
        {
            context.Transform =
                CreateModelToPaperTransform(layoutViewport) * paperTransform;
            var modelViewport = CreateModelViewport(paperViewport, layoutViewport);
            var activeModelOptions = CreateModelViewportOptions(
                options,
                layoutViewport,
                drawGripHandles: true,
                includeHiddenEntities: true);

            var transientStarted = Stopwatch.GetTimestamp();
            try
            {
                _drawTransients(
                    context,
                    document,
                    modelViewport,
                    transientScene,
                    activeModelOptions);
            }
            finally
            {
                _statistics.RecordTransientRender(ElapsedMilliseconds(transientStarted));
            }

            var selectionStarted = Stopwatch.GetTimestamp();
            try
            {
                _selectionRenderer.Draw(
                    context,
                    document,
                    modelViewport,
                    handleScene,
                    activeModelOptions);
            }
            finally
            {
                _statistics.RecordSelectionRender(ElapsedMilliseconds(selectionStarted));
            }
        }
        finally
        {
            context.PopAxisAlignedClip();
            context.Transform = paperTransform;
        }
    }

    internal static System.Numerics.Matrix3x2 CreateModelToPaperTransform(
        CadLayoutViewport viewport) =>
        System.Numerics.Matrix3x2.CreateTranslation(
            (float)-viewport.ModelCenter.X,
            (float)-viewport.ModelCenter.Y) *
        System.Numerics.Matrix3x2.CreateRotation((float)viewport.RotationRadians) *
        System.Numerics.Matrix3x2.CreateScale((float)viewport.Scale) *
        System.Numerics.Matrix3x2.CreateTranslation(
            (float)viewport.Bounds.Center.X,
            (float)viewport.Bounds.Center.Y);

    internal static CadViewport CreateModelViewport(
        CadViewport paperViewport,
        CadLayoutViewport layoutViewport)
    {
        var viewport = new CadViewport();
        viewport.SetSize(paperViewport.ViewWidth, paperViewport.ViewHeight);
        var zoom = Math.Max(paperViewport.Zoom * layoutViewport.Scale, 1e-6);
        var screenCenter = paperViewport.WorldToScreen(layoutViewport.Bounds.Center);
        viewport.SetView(zoom, new CadPointD(
            screenCenter.X - layoutViewport.ModelCenter.X * zoom,
            screenCenter.Y + layoutViewport.ModelCenter.Y * zoom));
        return viewport;
    }

    internal static CadRenderOptions CreateModelViewportOptions(
        CadRenderOptions options,
        CadLayoutViewport layoutViewport,
        bool drawGripHandles = false,
        bool includeHiddenEntities = true) => new()
        {
            ActiveOwnerBlockId = BlockId.ModelSpace,
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = drawGripHandles,
            IsAntialiasingEnabled = options.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = options.IsTextAntialiasingEnabled,
            EnableGeometryRealizations = options.EnableGeometryRealizations,
            IsLevelOfDetailEnabled = options.IsLevelOfDetailEnabled,
            AllowApproximateTileScaleFallback = options.AllowApproximateTileScaleFallback,
            TransformScaleMultiplier = options.TransformScaleMultiplier,
            KeepStrokeWidthScreenConstant = false,
            MinimumScreenStrokeWidth = options.MinimumScreenStrokeWidth,
            EntityLineWeightWorldScale = 1.0 / Math.Max(layoutViewport.Scale, double.Epsilon),
            EntityBoundsQuery = options.EntityBoundsQuery,
            EntityBoundsQueryInto = options.EntityBoundsQueryInto,
            EntityBoundsCount = options.EntityBoundsCount,
            DirtyWorldBounds = CadLayoutViewportMapper.PaperToModelBounds(
                layoutViewport,
                options.DirtyWorldBounds is { IsEmpty: false } dirty
                    ? dirty
                    : layoutViewport.Bounds),
            HiddenEntityIds = includeHiddenEntities
            ? options.HiddenEntityIds
            : CadRenderOptions.NoHiddenEntities
        };

    internal static CadRenderOptions CreatePaperSpaceOptions(
        CadLayout layout,
        CadRenderOptions options) => new()
        {
            ActiveOwnerBlockId = layout.PaperSpaceBlockId,
            ActiveLayoutId = layout.Id,
            DrawGrid = false,
            DrawOrigin = false,
            DrawGripHandles = options.DrawGripHandles,
            IsAntialiasingEnabled = options.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = options.IsTextAntialiasingEnabled,
            EnableGeometryRealizations = options.EnableGeometryRealizations,
            IsLevelOfDetailEnabled = options.IsLevelOfDetailEnabled,
            AllowApproximateTileScaleFallback = options.AllowApproximateTileScaleFallback,
            TransformScaleMultiplier = options.TransformScaleMultiplier,
            KeepStrokeWidthScreenConstant = false,
            MinimumScreenStrokeWidth = options.MinimumScreenStrokeWidth,
            EntityLineWeightWorldScale = 1.0,
            HiddenEntityIds = options.HiddenEntityIds,
            DirtyWorldBounds = options.DirtyWorldBounds,
            EntityBoundsQuery = options.EntityBoundsQuery,
            EntityBoundsQueryInto = options.EntityBoundsQueryInto,
            EntityBoundsCount = options.EntityBoundsCount
        };

    private static double CadEditorZoom(ID2D1DeviceContext context)
    {
        var transform = context.Transform;
        return Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
    }

    private static RawRectF ToRawRect(CadRectD bounds) => new(
        (float)bounds.MinX, (float)bounds.MinY, (float)bounds.MaxX, (float)bounds.MaxY);

    private static double ElapsedMilliseconds(long started) =>
        Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
