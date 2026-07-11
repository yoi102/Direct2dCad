using System.Numerics;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering.Transient;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DEntityReferenceRenderer(
    Direct2DResourceCache resourceCache,
    Direct2DEntityRenderer entityRenderer,
    Direct2DTransientRenderer transientRenderer,
    Direct2DOleRenderer oleRenderer)
{
    public void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadTransientEntityReference reference,
        CadRenderOptions options)
    {
        if (!document.TryGetEntity(reference.EntityId, out var entity) || entity is null || entity.IsErased)
            return;

        if (TryDrawTranslated(context, document, entity, viewport, reference, options))
            return;

        switch (entity)
        {
            case CadLine line:
                transientRenderer.DrawLine(context, viewport, line.Start + reference.Offset, line.End + reference.Offset, reference.Style);
                break;
            case CadCircle circle:
                transientRenderer.DrawCircle(context, viewport, circle.Center + reference.Offset, circle.Radius, reference.Style);
                break;
            case CadEllipse ellipse:
                transientRenderer.DrawEllipse(context, viewport, ellipse.Center + reference.Offset, ellipse.RadiusX, ellipse.RadiusY, reference.Style);
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
                    reference.Style);
                break;
            case CadRectangle rectangle:
                transientRenderer.DrawRectangle(
                    context,
                    viewport,
                    rectangle.Bounds.Translate(reference.Offset),
                    reference.Style,
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
                    reference.Style);
                break;
            case CadPolyline polyline:
                transientRenderer.DrawPolyline(
                    context,
                    viewport,
                    polyline.Points.Select(point => point + reference.Offset).ToArray(),
                    polyline.Closed,
                    reference.Style);
                break;
            case CadSpline spline:
                transientRenderer.DrawSpline(
                    context,
                    viewport,
                    spline.FitPoints.Select(point => point + reference.Offset).ToArray(),
                    spline.Closed,
                    reference.Style);
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
                    reference.Style,
                    text.IsInverted,
                    document.ViewSettings.BackgroundColor,
                    text.InvertedMarginFactor,
                    text.ShapeFontId);
                break;
            case CadText text:
                transientRenderer.DrawText(
                    context,
                    document,
                    viewport,
                    text.Text,
                    text.Position + reference.Offset,
                    text.Height,
                    text.TextBounds.Translate(reference.Offset),
                    reference.Style,
                    text.IsInverted,
                    document.ViewSettings.BackgroundColor,
                    text.InvertedMarginFactor,
                    text.TextStyleId,
                    text.RotationRadians);
                break;
            default:
                transientRenderer.DrawRectangle(context, viewport, entity.Bounds.Translate(reference.Offset), reference.Style);
                break;
        }
    }

    private bool TryDrawTranslated(
        ID2D1DeviceContext context,
        CadDocument document,
        CadEntity entity,
        CadViewport viewport,
        CadTransientEntityReference reference,
        CadRenderOptions options)
    {
        var previousTransform = context.Transform;
        context.Transform = Matrix3x2.CreateTranslation(
            (float)reference.Offset.X,
            (float)reference.Offset.Y) * previousTransform;
        try
        {
            if (entity is CadOleObject ole)
            {
                oleRenderer.DrawEntity(context, ole, viewport, allowDraw: false);
                return true;
            }

            if (!resourceCache.TryGetEntityResources(entity.Id, out var resources) || resources is null)
                return false;
            entityRenderer.Draw(context, document, entity, resources, viewport, options);
        }
        finally
        {
            context.Transform = previousTransform;
        }

        return true;
    }
}
