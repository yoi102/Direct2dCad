using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DSelectionRenderer(
    Direct2DResourceCache resourceCache,
    Direct2DTransientRenderer transientRenderer,
    Direct2DStyleResourceCache styleResources,
    Direct2DHandleRenderer handleRenderer)
{
    private const byte SelectedSolidFillMaximumAlpha = 64;

    public void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadHandleScene? scene,
        CadRenderOptions options)
    {
        if (scene is null || scene.IsEmpty)
            return;

        foreach (var item in scene.Items)
        {
            switch (item)
            {
                case CadSelectionEntityReference reference:
                    DrawSelectionReference(context, document, viewport, reference);
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

    private void DrawSelectionReference(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadSelectionEntityReference reference)
    {
        DrawSelectionReference(context, document, viewport, reference, []);
    }

    private void DrawSelectionReference(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadSelectionEntityReference reference,
        HashSet<BlockId> visitedBlocks)
    {
        if (!document.TryGetEntity(reference.EntityId, out var entity) || entity is null || entity.IsErased)
            return;

        if (entity is CadBlockReference blockReference)
        {
            DrawBlockReferenceSelection(
                context,
                document,
                viewport,
                blockReference,
                reference.Style,
                visitedBlocks);
            return;
        }

        resourceCache.TryGetEntityResources(entity.Id, out var resources);
        var style = ToTransientStyle(reference.Style, resources);
        if (TryDrawCachedGeometry(context, entity, resources, viewport, reference, style))
            return;

        switch (entity)
        {
            case CadLine line:
                transientRenderer.DrawLine(context, viewport, line.Start + reference.Offset, line.End + reference.Offset, style);
                break;
            case CadCircle circle:
                transientRenderer.DrawCircle(context, viewport, circle.Center + reference.Offset, circle.Radius, style);
                break;
            case CadEllipse ellipse:
                transientRenderer.DrawEllipse(context, viewport, ellipse.Center + reference.Offset, ellipse.RadiusX, ellipse.RadiusY, style);
                break;
            case CadEllipseArc arc:
                transientRenderer.DrawEllipseArc(
                    context,
                    viewport,
                    arc.Center + reference.Offset,
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
                    rectangle.Bounds.Translate(reference.Offset),
                    style,
                    rectangle.CornerRadiusX,
                    rectangle.CornerRadiusY);
                break;
            case CadArc arc:
                transientRenderer.DrawArc(
                    context,
                    viewport,
                    arc.Center + reference.Offset,
                    arc.Radius,
                    arc.StartAngleRadians,
                    arc.SweepAngleRadians,
                    style);
                break;
            case CadPolyline polyline:
                transientRenderer.DrawPolyline(
                    context,
                    viewport,
                    polyline.Points.Select(point => point + reference.Offset).ToArray(),
                    polyline.Closed,
                    style);
                break;
            case CadSpline spline:
                transientRenderer.DrawSpline(
                    context,
                    viewport,
                    spline.FitPoints.Select(point => point + reference.Offset).ToArray(),
                    spline.Closed,
                    style);
                break;
            case CadShapeText text:
                transientRenderer.DrawShapeText(
                    context,
                    viewport,
                    text.Text,
                    text.Position + reference.Offset,
                    text.Height,
                    text.RotationRadians,
                    text.WidthFactor,
                    text.CharacterSpacingFactor,
                    text.ObliqueAngleRadians,
                    style,
                    shapeFontId: text.ShapeFontId);
                break;
            case CadImage image:
                DrawImageFrame(context, viewport, image, reference, style);
                break;
            default:
                transientRenderer.DrawRectangle(context, viewport, entity.Bounds.Translate(reference.Offset), style);
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
            foreach (var child in document.GetEntitiesInBlock(reference.DefinitionBlockId)
                         .Where(entity =>
                             !entity.IsErased &&
                             entity.IsVisible &&
                             document.TryGetLayer(entity.LayerId, out var layer) &&
                             layer is { IsVisible: true, IsFrozen: false })
                         .OrderBy(entity => document.DocumentSettings.LayerDrawingPriority.GetPriority(entity.LayerId))
                         .ThenBy(entity => entity.ZIndex)
                         .ThenBy(entity => entity.Id.Value))
            {
                DrawSelectionReference(
                    context,
                    document,
                    viewport,
                    new CadSelectionEntityReference(child.Id, CadVectorD.Zero, selectionStyle),
                    visitedBlocks);
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
        CadSelectionEntityReference reference,
        CadTransientStyle style)
    {
        if (resources?.Geometry is null)
            return false;

        var brush = styleResources.GetBrush(context, style.StrokeColor);
        var strokeStyle = styleResources.GetStrokeStyle(resourceCache.Factory, style);
        var strokeWidth = styleResources.ResolveStrokeWidth(style, viewport);
        if (reference.Offset == CadVectorD.Zero)
        {
            DrawCachedFill(context, resources.Geometry, entity.Bounds, style, viewport);
            context.DrawGeometry(resources.Geometry, brush, strokeWidth, strokeStyle);
            return true;
        }

        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.CreateTranslation(
            (float)reference.Offset.X,
            (float)reference.Offset.Y) * previousTransform;
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
        CadSelectionEntityReference reference,
        CadTransientStyle style)
    {
        var bounds = image.FrameBounds.Translate(reference.Offset);
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

    private static CadTransientStyle ToTransientStyle(
        CadHandleStyle style,
        Direct2DResourceCache.EntityResourceBucket? resources = null)
    {
        CadColor? fillColor = resources?.FillBrush is not null
            ? WithMaximumAlpha(style.StrokeColor, SelectedSolidFillMaximumAlpha)
            : null;
        var hatchFill = resources is
        {
            HatchFillStyle: { } hatchStyle,
            HatchPattern: { } hatchPattern,
            HatchBrush: not null
        }
            ? new CadTransientHatchFill(
                style.StrokeColor,
                hatchStyle.HatchScale,
                hatchStyle.HatchAngle,
                hatchStyle.HatchOrigin,
                hatchPattern.Lines)
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
