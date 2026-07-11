using System.Numerics;
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
    Direct2DStyleResourceFactory styleFactory,
    Direct2DHandleRenderer handleRenderer)
{
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
        var style = ToTransientStyle(reference.Style);
        if (!document.TryGetEntity(reference.EntityId, out var entity) || entity is null || entity.IsErased)
            return;

        if (TryDrawCachedGeometry(context, entity, viewport, reference, style))
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

    private bool TryDrawCachedGeometry(
        ID2D1DeviceContext context,
        CadEntity entity,
        CadViewport viewport,
        CadSelectionEntityReference reference,
        CadTransientStyle style)
    {
        if (!resourceCache.TryGetEntityResources(entity.Id, out var resources) || resources?.Geometry is null)
            return false;

        using var brush = styleFactory.CreateBrush(context, style.StrokeColor);
        using var strokeStyle = styleFactory.CreateStrokeStyle(resourceCache.Factory, style);
        var strokeWidth = styleFactory.ResolveStrokeWidth(style, viewport);
        if (reference.Offset == CadVectorD.Zero)
        {
            context.DrawGeometry(resources.Geometry, brush, strokeWidth, strokeStyle);
            return true;
        }

        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.CreateTranslation(
            (float)reference.Offset.X,
            (float)reference.Offset.Y) * previousTransform;
        try
        {
            context.DrawGeometry(resources.Geometry, brush, strokeWidth, strokeStyle);
        }
        finally
        {
            context.Transform = previousTransform;
        }

        return true;
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

    private static CadTransientStyle ToTransientStyle(CadHandleStyle style)
    {
        return new CadTransientStyle(
            style.StrokeColor,
            style.StrokeWidth,
            CadTransientLinePattern.Solid,
            null,
            style.KeepSizeScreenConstant);
    }
}
