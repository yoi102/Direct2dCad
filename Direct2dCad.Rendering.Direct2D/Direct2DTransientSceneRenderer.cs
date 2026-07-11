using System.Numerics;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DTransientSceneRenderer(
    Direct2DTransientRenderer primitives,
    Direct2DTransientImageCache imageCache) : IDisposable
{
    public void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        CadTransientScene? scene,
        Action<CadTransientOleObject> drawOle,
        Action<CadTransientEntityReference> drawEntityReference)
    {
        if (scene is null || scene.IsEmpty)
        {
            imageCache.Clear();
            return;
        }

        imageCache.Reconcile(scene);
        foreach (var item in scene.Items)
        {
            switch (item)
            {
                case CadTransientLine line:
                    primitives.DrawLine(context, viewport, line.Start, line.End, line.Style);
                    break;
                case CadTransientCircle circle when circle.Radius > 0:
                    primitives.DrawCircle(context, viewport, circle.Center, circle.Radius, circle.Style);
                    break;
                case CadTransientEllipse ellipse when ellipse.RadiusX > 0 && ellipse.RadiusY > 0:
                    primitives.DrawEllipse(context, viewport, ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, ellipse.Style);
                    break;
                case CadTransientEllipseArc arc when arc.RadiusX > 0 && arc.RadiusY > 0 &&
                                                     Math.Abs(arc.SweepAngleRadians) > double.Epsilon:
                    primitives.DrawEllipseArc(
                        context,
                        viewport,
                        arc.Center,
                        arc.RadiusX,
                        arc.RadiusY,
                        arc.StartAngleRadians,
                        arc.SweepAngleRadians,
                        arc.Style);
                    break;
                case CadTransientArc arc when arc.Radius > 0 && Math.Abs(arc.SweepAngleRadians) > double.Epsilon:
                    primitives.DrawArc(context, viewport, arc.Center, arc.Radius, arc.StartAngleRadians, arc.SweepAngleRadians, arc.Style);
                    break;
                case CadTransientPolyline polyline when polyline.Points.Count >= 2:
                    primitives.DrawPolyline(context, viewport, polyline.Points, polyline.Closed, polyline.Style);
                    break;
                case CadTransientSpline spline when spline.FitPoints.Count >= 2:
                    primitives.DrawSpline(context, viewport, spline.FitPoints, spline.Closed, spline.Style);
                    break;
                case CadTransientRectangle rectangle when !rectangle.Bounds.IsEmpty:
                    primitives.DrawRectangle(
                        context,
                        viewport,
                        rectangle.Bounds,
                        rectangle.Style,
                        rectangle.CornerRadiusX,
                        rectangle.CornerRadiusY);
                    break;
                case CadTransientImage image when !image.Bounds.IsEmpty:
                    DrawImage(context, image);
                    break;
                case CadTransientOleObject ole when !ole.Bounds.IsEmpty:
                    drawOle(ole);
                    break;
                case CadTransientText text when !string.IsNullOrEmpty(text.Text) && text.Height > 0 && !text.Bounds.IsEmpty:
                    primitives.DrawText(
                        context,
                        document,
                        viewport,
                        text.Text,
                        text.Position,
                        text.Height,
                        text.Bounds,
                        text.Style,
                        text.IsInverted,
                        document.ViewSettings.BackgroundColor,
                        text.InvertedMarginFactor,
                        text.TextStyleId,
                        text.RotationRadians);
                    break;
                case CadTransientShapeText text when text.Height > 0:
                    primitives.DrawShapeText(
                        context,
                        viewport,
                        text.Text,
                        text.Position,
                        text.Height,
                        text.RotationRadians,
                        text.WidthFactor,
                        text.CharacterSpacingFactor,
                        text.ObliqueAngleRadians,
                        text.Style,
                        text.IsInverted,
                        document.ViewSettings.BackgroundColor,
                        text.InvertedMarginFactor,
                        text.ShapeFontId);
                    break;
                case CadTransientEntityReference reference:
                    drawEntityReference(reference);
                    break;
            }
        }
    }

    public void Clear() => imageCache.Clear();

    public void Dispose() => imageCache.Dispose();

    private void DrawImage(ID2D1DeviceContext context, CadTransientImage image)
    {
        var bitmap = imageCache.GetOrCreate(context, image);
        if (bitmap is null)
            return;

        var previousTransform = context.Transform;
        context.Transform = CreateWorldRotationTransform(
            image.RotationRadians,
            image.Bounds.Center,
            previousTransform);
        try
        {
            context.DrawBitmap(
                bitmap,
                new RawRectF(
                    (float)image.Bounds.MinX,
                    (float)image.Bounds.MinY,
                    (float)image.Bounds.MaxX,
                    (float)image.Bounds.MaxY),
                ToOpacity(image.Opacity),
                InterpolationMode.Linear,
                null,
                null);
        }
        finally
        {
            context.Transform = previousTransform;
        }
    }

    private static Matrix3x2 CreateWorldRotationTransform(
        double rotation,
        CadPointD center,
        Matrix3x2 transform)
    {
        return Math.Abs(rotation) <= 1e-12
            ? transform
            : Matrix3x2.CreateRotation(
                (float)rotation,
                new Vector2((float)center.X, (float)center.Y)) * transform;
    }

    private static float ToOpacity(double opacity)
    {
        return double.IsFinite(opacity) ? (float)Math.Clamp(opacity, 0.0, 1.0) : 1.0f;
    }
}
