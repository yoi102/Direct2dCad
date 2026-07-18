using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Entities;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;
using Direct2dCad.Rendering.Direct2D.Transient;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Overlays;

internal sealed class Direct2DSelectionRenderer(
    Direct2DResourceCache resourceCache,
    Direct2DTransientRenderer transientRenderer,
    Direct2DStyleResourceCache styleResources,
    Direct2DHandleRenderer handleRenderer,
    Direct2DEntityOrderCache entityOrderCache)
{
    private const byte SelectedSolidFillMaximumAlpha = 64;
    private const int SpatiallyIndexedSelectionThreshold = 256;
    private readonly HashSet<BlockId> _visitedBlocks = [];

    public void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadHandleScene? scene,
        CadRenderOptions options)
    {
        if (scene is null || scene.IsEmpty)
            return;

        _visitedBlocks.Clear();
        var renderWorldBounds = options.DirtyWorldBounds is { IsEmpty: false } dirty
            ? dirty
            : viewport.VisibleWorldBounds;
        if (CanUseSpatialSelectionQuery(scene, options, renderWorldBounds))
        {
            DrawSpatiallyQueriedSelections(
                context,
                document,
                viewport,
                scene,
                options,
                renderWorldBounds);
            DrawNonSelectionItems(context, viewport, scene.NonSelectionItems, options);
            return;
        }

        foreach (var item in scene.Items)
        {
            switch (item)
            {
                case CadSelectionEntityReference reference:
                    DrawSelectionReference(
                        context,
                        document,
                        viewport,
                        reference,
                        renderWorldBounds,
                        _visitedBlocks);
                    break;
                case CadGripHandle grip when options.DrawGripHandles && IsGripVisible(viewport, grip):
                    handleRenderer.DrawGrip(context, resourceCache.Factory, viewport, grip);
                    break;
                case CadRotationHandleGuide guide when options.DrawGripHandles:
                    transientRenderer.DrawLine(
                        context,
                        viewport,
                        guide.Start,
                        guide.End,
                        ToTransientStyle(guide.Style));
                    break;
            }
        }
    }

    private static bool CanUseSpatialSelectionQuery(
        CadHandleScene scene,
        CadRenderOptions options,
        CadRectD renderWorldBounds)
    {
        return scene.SelectionReferenceCount >= SpatiallyIndexedSelectionThreshold &&
               !scene.HasTranslatedSelectionReferences &&
               !renderWorldBounds.IsEmpty &&
               options.EntityBoundsQuery is not null;
    }

    private void DrawSpatiallyQueriedSelections(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadHandleScene scene,
        CadRenderOptions options,
        CadRectD renderWorldBounds)
    {
        var padding = 64.0 / Math.Max(viewport.Zoom, double.Epsilon);
        foreach (var entityId in options.EntityBoundsQuery!(renderWorldBounds.Inflate(padding)))
        {
            if (!scene.TryGetSelectionReference(entityId, out var reference) ||
                reference is null)
            {
                continue;
            }

            DrawSelectionReference(
                context,
                document,
                viewport,
                reference,
                renderWorldBounds,
                _visitedBlocks);
        }
    }

    private void DrawNonSelectionItems(
        ID2D1DeviceContext context,
        CadViewport viewport,
        IReadOnlyList<CadHandleItem> items,
        CadRenderOptions options)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case CadGripHandle grip when options.DrawGripHandles && IsGripVisible(viewport, grip):
                    handleRenderer.DrawGrip(context, resourceCache.Factory, viewport, grip);
                    break;
                case CadRotationHandleGuide guide when options.DrawGripHandles:
                    transientRenderer.DrawLine(
                        context,
                        viewport,
                        guide.Start,
                        guide.End,
                        ToTransientStyle(guide.Style));
                    break;
            }
        }
    }

    private void DrawSelectionReference(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadSelectionEntityReference reference,
        CadRectD? dirtyWorldBounds,
        HashSet<BlockId> visitedBlocks)
    {
        if (!document.TryGetEntity(reference.EntityId, out var entity) || entity is null || entity.IsErased)
            return;

        DrawSelectionEntity(
            context,
            document,
            viewport,
            entity,
            reference.Offset,
            reference.Style,
            dirtyWorldBounds,
            visitedBlocks);
    }

    private void DrawSelectionEntity(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadEntity entity,
        CadVectorD offset,
        CadHandleStyle selectionStyle,
        CadRectD? dirtyWorldBounds,
        HashSet<BlockId> visitedBlocks)
    {
        if (!IntersectsRenderBounds(entity, offset, viewport, dirtyWorldBounds))
            return;

        if (entity is CadBlockReference blockReference)
        {
            DrawBlockReferenceSelection(
                context,
                document,
                viewport,
                blockReference,
                selectionStyle,
                visitedBlocks);
            return;
        }

        resourceCache.TryGetEntityResources(entity.Id, out var resources);
        var style = ToTransientStyle(selectionStyle, resources);
        if (TryDrawCachedGeometry(context, entity, resources, viewport, offset, style))
            return;

        switch (entity)
        {
            case CadLine line:
                transientRenderer.DrawLine(context, viewport, line.Start + offset, line.End + offset, style);
                break;
            case CadCircle circle:
                transientRenderer.DrawCircle(context, viewport, circle.Center + offset, circle.Radius, style);
                break;
            case CadEllipse ellipse:
                transientRenderer.DrawEllipse(context, viewport, ellipse.Center + offset, ellipse.RadiusX, ellipse.RadiusY, style);
                break;
            case CadEllipseArc arc:
                transientRenderer.DrawEllipseArc(
                    context,
                    viewport,
                    arc.Center + offset,
                    arc.RadiusX,
                    arc.RadiusY,
                    arc.StartAngleRadians,
                    arc.SweepAngleRadians,
                    style);
                break;
            case CadRectangle rectangle:
                transientRenderer.DrawRectangle(
                    context,
                    viewport,
                    rectangle.Bounds.Translate(offset),
                    style,
                    rectangle.CornerRadiusX,
                    rectangle.CornerRadiusY);
                break;
            case CadArc arc:
                transientRenderer.DrawArc(
                    context,
                    viewport,
                    arc.Center + offset,
                    arc.Radius,
                    arc.StartAngleRadians,
                    arc.SweepAngleRadians,
                    style);
                break;
            case CadPolyline polyline:
                transientRenderer.DrawPolyline(
                    context,
                    viewport,
                    polyline.Points.Select(point => point + offset).ToArray(),
                    polyline.Closed,
                    style);
                break;
            case CadSpline spline:
                transientRenderer.DrawSpline(
                    context,
                    viewport,
                    spline.FitPoints.Select(point => point + offset).ToArray(),
                    spline.Closed,
                    style);
                break;
            case CadShapeText text:
                transientRenderer.DrawShapeText(
                    context,
                    viewport,
                    text.Text,
                    text.Position + offset,
                    text.Height,
                    text.RotationRadians,
                    text.WidthFactor,
                    text.CharacterSpacingFactor,
                    text.ObliqueAngleRadians,
                    style,
                    shapeFontId: text.ShapeFontId);
                break;
            case CadImage image:
                DrawImageFrame(context, viewport, image, offset, style);
                break;
            default:
                transientRenderer.DrawRectangle(context, viewport, entity.Bounds.Translate(offset), style);
                break;
        }
    }

    private void DrawBlockReferenceSelection(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadBlockReference reference,
        CadHandleStyle selectionStyle,
        HashSet<BlockId> visitedBlocks)
    {
        if (!visitedBlocks.Add(reference.DefinitionBlockId) ||
            !document.TryGetBlock(reference.DefinitionBlockId, out var definition) ||
            definition is null)
        {
            return;
        }

        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.CreateTranslation(
                                (float)-definition.BasePoint.X,
                                (float)-definition.BasePoint.Y) *
                            Matrix3x2.CreateScale((float)reference.ScaleX, (float)reference.ScaleY) *
                            Matrix3x2.CreateRotation((float)reference.RotationRadians) *
                            Matrix3x2.CreateTranslation(
                                (float)reference.Position.X,
                                (float)reference.Position.Y) *
                            previousTransform;
        try
        {
            foreach (var child in entityOrderCache.GetOrderedEntities(
                         document,
                         reference.DefinitionBlockId))
            {
                if (child.IsErased ||
                    !child.IsVisible ||
                    !document.TryGetLayer(child.LayerId, out var layer) ||
                    layer is not { IsVisible: true, IsFrozen: false })
                {
                    continue;
                }

                DrawSelectionEntity(
                    context,
                    document,
                    viewport,
                    child,
                    CadVectorD.Zero,
                    selectionStyle,
                    dirtyWorldBounds: null,
                    visitedBlocks: visitedBlocks);
            }
        }
        finally
        {
            context.Transform = previousTransform;
            visitedBlocks.Remove(reference.DefinitionBlockId);
        }
    }

    private bool TryDrawCachedGeometry(
        ID2D1DeviceContext context,
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket? resources,
        CadViewport viewport,
        CadVectorD offset,
        CadTransientStyle style)
    {
        if (resources?.Geometry is null)
            return false;

        var brush = styleResources.GetBrush(context, style.StrokeColor);
        var strokeStyle = styleResources.GetStrokeStyle(resourceCache.Factory, style);
        var strokeWidth = styleResources.ResolveStrokeWidth(style, viewport);
        if (offset == CadVectorD.Zero)
        {
            DrawCachedFill(context, resources.Geometry, entity.Bounds, style, viewport);
            context.DrawGeometry(resources.Geometry, brush, strokeWidth, strokeStyle);
            return true;
        }

        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.CreateTranslation(
            (float)offset.X,
            (float)offset.Y) * previousTransform;
        try
        {
            DrawCachedFill(context, resources.Geometry, entity.Bounds, style, viewport);
            context.DrawGeometry(resources.Geometry, brush, strokeWidth, strokeStyle);
        }
        finally
        {
            context.Transform = previousTransform;
        }

        return true;
    }

    private void DrawCachedFill(
        ID2D1DeviceContext context,
        ID2D1Geometry geometry,
        CadRectD bounds,
        CadTransientStyle style,
        CadViewport viewport)
    {
        if (style.FillColor is { IsTransparent: false } fillColor)
        {
            var fillBrush = styleResources.GetBrush(context, fillColor);
            context.FillGeometry(geometry, fillBrush);
        }

        if (style.HatchFill is not { ForegroundColor.IsTransparent: false } hatchFill || bounds.IsEmpty)
            return;

        var hatchBrush = styleResources.GetBrush(context, hatchFill.ForegroundColor);
        Direct2DHatchRenderer.Draw(context, geometry, bounds, hatchFill, hatchBrush, viewport);
    }

    private void DrawImageFrame(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadImage image,
        CadVectorD offset,
        CadTransientStyle style)
    {
        var bounds = image.FrameBounds.Translate(offset);
        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.CreateRotation(
            (float)image.RotationRadians,
            new Vector2((float)bounds.Center.X, (float)bounds.Center.Y)) * previousTransform;
        try
        {
            transientRenderer.DrawRectangle(context, viewport, bounds, style);
        }
        finally
        {
            context.Transform = previousTransform;
        }
    }

    private static bool IsGripVisible(CadViewport viewport, CadGripHandle grip)
    {
        var screen = viewport.WorldToScreen(grip.Position);
        var margin = Math.Max(grip.Style.Size, grip.Style.StrokeWidth) + 8.0;
        return screen.X >= -margin &&
               screen.Y >= -margin &&
               screen.X <= viewport.ViewWidth + margin &&
               screen.Y <= viewport.ViewHeight + margin;
    }

    private static bool IntersectsRenderBounds(
        CadEntity entity,
        CadVectorD offset,
        CadViewport viewport,
        CadRectD? dirtyWorldBounds)
    {
        var entityBounds = entity.Bounds.Translate(offset);
        if (entityBounds.IsEmpty)
            return true;

        if (dirtyWorldBounds is not { IsEmpty: false } renderBounds)
            return true;

        var padding = 32.0 / Math.Max(viewport.Zoom, double.Epsilon);
        return entityBounds.Intersects(renderBounds.Inflate(padding));
    }

    private static CadTransientStyle ToTransientStyle(
        CadHandleStyle style,
        Direct2DResourceCache.EntityResourceBucket? resources = null)
    {
        CadColor? fillColor = resources?.FillBrush is not null
            ? WithMaximumAlpha(style.StrokeColor, SelectedSolidFillMaximumAlpha)
            : null;
        CadTransientHatchFill? hatchFill = resources is
        {
            HatchRenderData: { } hatch,
            HatchBrush: not null
        }
            ? hatch with { ForegroundColor = style.StrokeColor }
            : null;

        return new CadTransientStyle(
            style.StrokeColor,
            style.StrokeWidth,
            CadTransientLinePattern.Solid,
            fillColor,
            style.KeepSizeScreenConstant,
            HatchFill: hatchFill);
    }

    private static CadColor WithMaximumAlpha(CadColor color, byte maximumAlpha)
    {
        return CadColor.FromArgb(
            Math.Min(color.A, maximumAlpha),
            color.R,
            color.G,
            color.B);
    }
}
