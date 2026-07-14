using System.Numerics;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DEntityRenderer(
    Direct2DResourceCache resourceCache,
    Direct2DGeometryFactory geometryFactory,
    Direct2DStyleResourceCache styleResources)
{
    public void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadViewport viewport,
        CadRenderOptions options)
    {
        if (entity is CadShapeText { IsInverted: true } shapeText &&
            resources.Geometry is not null &&
            resources.StrokeBrush is not null)
        {
            FillBounds(context, shapeText.InvertedBackgroundBounds, resources.StrokeBrush);
            var invertedBrush = styleResources.GetBrush(context, document.ViewSettings.BackgroundColor);
            context.DrawGeometry(
                resources.Geometry,
                invertedBrush,
                ResolveStrokeWidth(resources.StrokeWidth, viewport, options));
            return;
        }

        switch (entity)
        {
            case CadLine line:
                DrawLine(context, line, resources, viewport, options);
                return;
            case CadCircle circle:
                DrawEllipse(
                    context,
                    new Ellipse(ToVector2(circle.Center), (float)circle.Radius, (float)circle.Radius),
                    resources,
                    viewport,
                    options);
                return;
            case CadEllipse ellipse:
                DrawEllipse(
                    context,
                    new Ellipse(ToVector2(ellipse.Center), (float)ellipse.RadiusX, (float)ellipse.RadiusY),
                    resources,
                    viewport,
                    options);
                return;
            case CadArc { IsFullCircle: true } arc:
                DrawEllipse(
                    context,
                    new Ellipse(ToVector2(arc.Center), (float)arc.Radius, (float)arc.Radius),
                    resources,
                    viewport,
                    options);
                return;
            case CadRectangle rectangle:
                DrawRectangle(context, rectangle, resources, viewport, options);
                return;
            case CadImage image:
                DrawImage(context, image, resources);
                return;
        }

        if (resources.Geometry is not null)
            DrawFill(context, resources.Geometry, entity.Bounds, resources, viewport);
        if (resources.Geometry is not null && resources.StrokeBrush is not null)
        {
            context.DrawGeometry(
                resources.Geometry,
                resources.StrokeBrush,
                ResolveStrokeWidth(resources.StrokeWidth, viewport, options),
                resources.StrokeStyle);
        }

        if (entity is CadText text && resources.TextFormat is not null && resources.StrokeBrush is not null)
            DrawText(context, document, text, resources);
    }

    private static void DrawLine(
        ID2D1DeviceContext context,
        CadLine line,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadViewport viewport,
        CadRenderOptions options)
    {
        if (resources.StrokeBrush is null)
            return;
        context.DrawLine(
            ToVector2(line.Start),
            ToVector2(line.End),
            resources.StrokeBrush,
            ResolveStrokeWidth(resources.StrokeWidth, viewport, options),
            resources.StrokeStyle);
    }

    private static void DrawImage(
        ID2D1DeviceContext context,
        CadImage image,
        Direct2DResourceCache.EntityResourceBucket resources)
    {
        var bounds = image.FrameBounds;
        if (resources.BitmapBrush is null || bounds.IsEmpty)
            return;

        resources.BitmapBrush.Opacity = ToOpacity(image.Opacity);
        var previousTransform = context.Transform;
        context.Transform = CreateWorldRotationTransform(image.RotationRadians, bounds.Center, previousTransform);
        try
        {
            context.FillRectangle(ToRawRect(bounds), resources.BitmapBrush);
        }
        finally
        {
            context.Transform = previousTransform;
        }
    }

    private void DrawEllipse(
        ID2D1DeviceContext context,
        Ellipse ellipse,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadViewport viewport,
        CadRenderOptions options)
    {
        if (resources.HatchBrush is not null && resources.Geometry is not null)
        {
            DrawFill(
                context,
                resources.Geometry,
                CadRectD.FromCenter(
                    new CadPointD(ellipse.Point.X, ellipse.Point.Y),
                    ellipse.RadiusX * 2.0,
                    ellipse.RadiusY * 2.0),
                resources,
                viewport);
        }
        else if (resources.FillBrush is not null)
        {
            context.FillEllipse(ellipse, resources.FillBrush);
        }

        if (resources.StrokeBrush is not null)
        {
            context.DrawEllipse(
                ellipse,
                resources.StrokeBrush,
                ResolveStrokeWidth(resources.StrokeWidth, viewport, options),
                resources.StrokeStyle);
        }
    }

    private void DrawRectangle(
        ID2D1DeviceContext context,
        CadRectangle rectangle,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadViewport viewport,
        CadRenderOptions options)
    {
        var bounds = rectangle.Bounds;
        if (bounds.IsEmpty)
            return;

        var radiusX = geometryFactory.ClampCornerRadius(rectangle.CornerRadiusX, bounds.Width);
        var radiusY = geometryFactory.ClampCornerRadius(rectangle.CornerRadiusY, bounds.Height);
        if (radiusX > 0 && radiusY > 0)
        {
            var rounded = geometryFactory.CreateRoundedRectangle(bounds, radiusX, radiusY);
            if (resources.HatchBrush is not null && resources.Geometry is not null)
            {
                DrawFill(context, resources.Geometry, bounds, resources, viewport);
            }
            else if (resources.FillBrush is not null)
            {
                context.FillRoundedRectangle(rounded, resources.FillBrush);
            }

            if (resources.StrokeBrush is not null)
            {
                var strokeWidth = ResolveStrokeWidth(resources.StrokeWidth, viewport, options);
                if (resources.StrokeStyle is null)
                    context.DrawRoundedRectangle(rounded, resources.StrokeBrush, strokeWidth);
                else
                    context.DrawRoundedRectangle(rounded, resources.StrokeBrush, strokeWidth, resources.StrokeStyle);
            }

            return;
        }

        var rect = ToRawRect(bounds);
        if (resources.HatchBrush is not null && resources.Geometry is not null)
        {
            DrawFill(context, resources.Geometry, bounds, resources, viewport);
        }
        else if (resources.FillBrush is not null)
        {
            context.FillRectangle(rect, resources.FillBrush);
        }

        if (resources.StrokeBrush is not null)
        {
            context.DrawRectangle(
                rect,
                resources.StrokeBrush,
                ResolveStrokeWidth(resources.StrokeWidth, viewport, options),
                resources.StrokeStyle);
        }
    }

    private void DrawText(
        ID2D1DeviceContext context,
        CadDocument document,
        CadText text,
        Direct2DResourceCache.EntityResourceBucket resources)
    {
        var previousTransform = context.Transform;
        context.Transform = CreateWorldRotationTransform(text.RotationRadians, text.Position, previousTransform);
        try
        {
            if (text.IsInverted)
            {
                FillBounds(context, text.InvertedBackgroundBounds, resources.StrokeBrush!);
                var invertedBrush = styleResources.GetBrush(context, document.ViewSettings.BackgroundColor);
                DrawTextClipped(context, text.Text, resources.TextFormat!, text.Position, text.TextBounds, invertedBrush);
                return;
            }

            DrawTextClipped(context, text.Text, resources.TextFormat!, text.Position, text.TextBounds, resources.StrokeBrush!);
        }
        finally
        {
            context.Transform = previousTransform;
        }
    }

    private void DrawFill(
        ID2D1DeviceContext context,
        ID2D1Geometry geometry,
        CadRectD bounds,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadViewport viewport)
    {
        if (resources.FillBrush is not null)
            context.FillGeometry(geometry, resources.FillBrush);
        if (resources.HatchBrush is null ||
            resources.HatchFillStyle is null ||
            resources.HatchPattern is null ||
            resourceCache.Factory is null ||
            bounds.IsEmpty)
        {
            return;
        }

        var hatch = new CadTransientHatchFill(
            resources.HatchFillStyle.ForegroundColor,
            resources.HatchFillStyle.HatchScale,
            resources.HatchFillStyle.HatchAngle,
            resources.HatchFillStyle.HatchOrigin,
            resources.HatchPattern.Lines.ToArray());
        Direct2DHatchRenderer.Draw(context, geometry, bounds, hatch, resources.HatchBrush, viewport);
    }

    private static void FillBounds(ID2D1DeviceContext context, CadRectD bounds, ID2D1Brush brush)
    {
        if (!bounds.IsEmpty)
            context.FillRectangle(ToRawRect(bounds), brush);
    }

    private static void DrawTextClipped(
        ID2D1DeviceContext context,
        string text,
        IDWriteTextFormat format,
        CadPointD origin,
        CadRectD bounds,
        ID2D1Brush brush)
    {
        if (bounds.IsEmpty)
            return;

        var previousTransform = context.Transform;
        context.Transform = CreateTextLayoutTransform(bounds) * previousTransform;
        context.PushAxisAlignedClip(ToRawRect(bounds), AntialiasMode.PerPrimitive);
        try
        {
            context.DrawText(
                text,
                format,
                Rect.FromLTRB(
                    (float)origin.X,
                    (float)origin.Y,
                    (float)(origin.X + Math.Max(bounds.Width, 1e-6)),
                    (float)(origin.Y + Math.Max(bounds.Height, 1e-6))),
                brush,
                DrawTextOptions.Clip);
        }
        finally
        {
            context.PopAxisAlignedClip();
            context.Transform = previousTransform;
        }
    }

    private static float ResolveStrokeWidth(float modelWidth, CadViewport viewport, CadRenderOptions options)
    {
        var zoom = Math.Max((float)viewport.Zoom, float.Epsilon);
        var width = options.KeepStrokeWidthScreenConstant ? modelWidth / zoom : modelWidth;
        return Math.Max(width, (float)options.MinimumScreenStrokeWidth / zoom);
    }

    private static Matrix3x2 CreateWorldRotationTransform(double rotation, CadPointD center, Matrix3x2 transform)
    {
        return Math.Abs(rotation) <= 1e-12
            ? transform
            : Matrix3x2.CreateRotation((float)rotation, ToVector2(center)) * transform;
    }

    private static Matrix3x2 CreateTextLayoutTransform(CadRectD bounds)
    {
        return Matrix3x2.CreateScale(1.0f, -1.0f) *
               Matrix3x2.CreateTranslation(0.0f, (float)(bounds.MinY + bounds.MaxY));
    }

    private static float ToOpacity(double opacity)
    {
        return double.IsFinite(opacity) ? (float)Math.Clamp(opacity, 0.0, 1.0) : 1.0f;
    }

    private static RawRectF ToRawRect(CadRectD bounds)
    {
        return new RawRectF((float)bounds.MinX, (float)bounds.MinY, (float)bounds.MaxX, (float)bounds.MaxY);
    }

    private static Vector2 ToVector2(CadPointD point) => new((float)point.X, (float)point.Y);
}
