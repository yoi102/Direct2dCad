using System.Numerics;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Direct2D.Scene;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D.Entities;

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
        CadRenderOptions options,
        ID2D1Brush? strokeBrushOverride = null,
        float? strokeWidthOverride = null)
    {
        var strokeBrush = strokeBrushOverride ?? resources.StrokeBrush;
        var strokeWidth = strokeWidthOverride ?? resources.StrokeWidth;
        if (TryDrawSimplified(
                context,
                entity,
                resources,
                options,
                strokeBrush))
        {
            return;
        }

        if (entity is CadShapeText { IsInverted: true } shapeText &&
            resources.Geometry is not null &&
            strokeBrush is not null)
        {
            FillBounds(context, shapeText.InvertedBackgroundBounds, strokeBrush);
            var invertedBrush = styleResources.GetBrush(context, document.ViewSettings.BackgroundColor);
            var resolvedStrokeWidth = ResolveStrokeWidth(strokeWidth, viewport, options);
            if (!resourceCache.TryDrawStrokedGeometry(
                    context,
                    entity,
                    resources,
                    resources.Geometry,
                    invertedBrush,
                    resolvedStrokeWidth,
                    strokeStyle: null,
                    StrokeWidthChangesWithScale(strokeWidth, resolvedStrokeWidth, options)))
            {
                context.DrawGeometry(
                    resources.Geometry,
                    invertedBrush,
                    resolvedStrokeWidth);
            }
            return;
        }

        switch (entity)
        {
            case CadLine line:
                DrawLine(context, line, resources, viewport, options, strokeBrush, strokeWidth);
                return;
            case CadCircle circle:
                DrawEllipse(
                    context,
                    circle,
                    new Ellipse(ToVector2(circle.Center), (float)circle.Radius, (float)circle.Radius),
                    resources,
                    viewport,
                    options,
                    strokeBrush,
                    strokeWidth);
                return;
            case CadEllipse ellipse:
                DrawEllipse(
                    context,
                    ellipse,
                    new Ellipse(ToVector2(ellipse.Center), (float)ellipse.RadiusX, (float)ellipse.RadiusY),
                    resources,
                    viewport,
                    options,
                    strokeBrush,
                    strokeWidth);
                return;
            case CadArc { IsFullCircle: true } arc:
                DrawEllipse(
                    context,
                    arc,
                    new Ellipse(ToVector2(arc.Center), (float)arc.Radius, (float)arc.Radius),
                    resources,
                    viewport,
                    options,
                    strokeBrush,
                    strokeWidth);
                return;
            case CadRectangle rectangle:
                DrawRectangle(context, rectangle, resources, viewport, options, strokeBrush, strokeWidth);
                return;
            case CadImage image:
                DrawImage(context, image, resources);
                return;
        }

        if (options.IsLevelOfDetailEnabled)
            resourceCache.EnsureLevelOfDetailGeometries(entity, resources);
        var geometry = Direct2DEntityLevelOfDetail.ResolveGeometry(
            entity,
            resources,
            context.Transform,
            options);
        if (geometry is not null)
            DrawFill(context, entity, geometry, entity.Bounds, resources, viewport, options);
        if (geometry is not null && strokeBrush is not null)
        {
            var resolvedStrokeWidth = ResolveStrokeWidth(strokeWidth, viewport, options);
            var strokeStyle = Direct2DEntityLevelOfDetail.ResolveStrokeStyle(
                entity,
                resources.StrokeStyle,
                context.Transform,
                options);
            if (!resourceCache.TryDrawStrokedGeometry(
                    context,
                    entity,
                    resources,
                    geometry,
                    strokeBrush,
                    resolvedStrokeWidth,
                    strokeStyle,
                    StrokeWidthChangesWithScale(strokeWidth, resolvedStrokeWidth, options)))
            {
                context.DrawGeometry(
                    geometry,
                    strokeBrush,
                    resolvedStrokeWidth,
                    strokeStyle);
            }
        }

        if (entity is CadText text && resources.TextLayout is not null && strokeBrush is not null)
            DrawText(context, document, text, resources, strokeBrush);
    }

    private static bool TryDrawSimplified(
        ID2D1DeviceContext context,
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadRenderOptions options,
        ID2D1Brush? strokeBrush)
    {
        if (Direct2DEntityLevelOfDetail.Resolve(
                entity,
                resources,
                context.Transform,
                options) != Direct2DEntityRenderDetail.Simplified)
        {
            return false;
        }

        var brush = strokeBrush ?? resources.FillBrush ?? resources.HatchBrush ?? resources.BitmapBrush;
        if (brush is null)
            return true;

        var textDetail = Direct2DEntityLevelOfDetail.ResolveText(
            entity,
            context.Transform,
            options);
        switch (entity)
        {
            case CadText text:
                DrawTextProxy(context, text, textDetail, brush);
                return true;
            case CadShapeText shapeText:
                DrawShapeTextProxy(context, shapeText, textDetail, brush);
                return true;
        }

        DrawBoundsProxy(context, entity.Bounds, brush);
        return true;
    }

    internal static void DrawBoundsProxy(
        ID2D1DeviceContext context,
        CadRectD bounds,
        ID2D1Brush brush)
    {
        if (bounds.IsEmpty)
            return;

        var screenScale = Math.Max(
            (float)Direct2DEntityLevelOfDetail.ResolveMaximumScreenScale(context.Transform),
            float.Epsilon);
        var start = new Vector2((float)bounds.MinX, (float)bounds.MinY);
        var end = new Vector2((float)bounds.MaxX, (float)bounds.MaxY);
        if (start == end)
            end.X += 1.0f / screenScale;

        context.DrawLine(start, end, brush, 1.0f / screenScale);
    }

    internal static void DrawRectangularProxy(
        ID2D1DeviceContext context,
        CadRectD bounds,
        ID2D1Brush brush)
    {
        if (bounds.IsEmpty)
            return;

        var screenScale = Math.Max(
            (float)Direct2DEntityLevelOfDetail.ResolveMaximumScreenScale(context.Transform),
            float.Epsilon);
        context.DrawRectangle(ToRawRect(bounds), brush, 1.0f / screenScale);
    }

    private static void DrawLine(
        ID2D1DeviceContext context,
        CadLine line,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadViewport viewport,
        CadRenderOptions options,
        ID2D1Brush? strokeBrush,
        float strokeWidth)
    {
        if (strokeBrush is null)
            return;
        context.DrawLine(
            ToVector2(line.Start),
            ToVector2(line.End),
            strokeBrush,
            ResolveStrokeWidth(strokeWidth, viewport, options),
            Direct2DEntityLevelOfDetail.ResolveStrokeStyle(
                line,
                resources.StrokeStyle,
                context.Transform,
                options));
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
        CadEntity entity,
        Ellipse ellipse,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadViewport viewport,
        CadRenderOptions options,
        ID2D1Brush? strokeBrush,
        float strokeWidth)
    {
        if (resources.HatchBrush is not null && resources.Geometry is not null)
        {
            DrawFill(
                context,
                entity,
                resources.Geometry,
                CadRectD.FromCenter(
                    new CadPointD(ellipse.Point.X, ellipse.Point.Y),
                    ellipse.RadiusX * 2.0,
                    ellipse.RadiusY * 2.0),
                resources,
                viewport,
                options);
        }
        else if (resources.FillBrush is not null)
        {
            context.FillEllipse(ellipse, resources.FillBrush);
        }

        if (strokeBrush is not null)
        {
            context.DrawEllipse(
                ellipse,
                strokeBrush,
                ResolveStrokeWidth(strokeWidth, viewport, options),
                Direct2DEntityLevelOfDetail.ResolveStrokeStyle(
                    entity,
                    resources.StrokeStyle,
                    context.Transform,
                    options));
        }
    }

    private void DrawRectangle(
        ID2D1DeviceContext context,
        CadRectangle rectangle,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadViewport viewport,
        CadRenderOptions options,
        ID2D1Brush? strokeBrush,
        float strokeWidth)
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
                DrawFill(context, rectangle, resources.Geometry, bounds, resources, viewport, options);
            }
            else if (resources.FillBrush is not null)
            {
                context.FillRoundedRectangle(rounded, resources.FillBrush);
            }

            if (strokeBrush is not null)
            {
                var resolvedStrokeWidth = ResolveStrokeWidth(strokeWidth, viewport, options);
                var strokeStyle = Direct2DEntityLevelOfDetail.ResolveStrokeStyle(
                    rectangle,
                    resources.StrokeStyle,
                    context.Transform,
                    options);
                if (strokeStyle is null)
                    context.DrawRoundedRectangle(rounded, strokeBrush, resolvedStrokeWidth);
                else
                    context.DrawRoundedRectangle(rounded, strokeBrush, resolvedStrokeWidth, strokeStyle);
            }

            return;
        }

        var rect = ToRawRect(bounds);
        if (resources.HatchBrush is not null && resources.Geometry is not null)
        {
            DrawFill(context, rectangle, resources.Geometry, bounds, resources, viewport, options);
        }
        else if (resources.FillBrush is not null)
        {
            context.FillRectangle(rect, resources.FillBrush);
        }

        if (strokeBrush is not null)
        {
            context.DrawRectangle(
                rect,
                strokeBrush,
                ResolveStrokeWidth(strokeWidth, viewport, options),
                Direct2DEntityLevelOfDetail.ResolveStrokeStyle(
                    rectangle,
                    resources.StrokeStyle,
                    context.Transform,
                    options));
        }
    }

    private static void DrawTextProxy(
        ID2D1DeviceContext context,
        CadText text,
        Direct2DTextRenderDetail detail,
        ID2D1Brush brush)
    {
        if (detail is Direct2DTextRenderDetail.Skip or Direct2DTextRenderDetail.Full ||
            text.TextBounds.IsEmpty)
        {
            return;
        }

        var previousTransform = context.Transform;
        context.Transform = CreateWorldRotationTransform(
            text.RotationRadians,
            text.Position,
            previousTransform);
        try
        {
            DrawHorizontalTextProxy(context, text.TextBounds, detail, brush);
        }
        finally
        {
            context.Transform = previousTransform;
        }
    }

    private static void DrawShapeTextProxy(
        ID2D1DeviceContext context,
        CadShapeText text,
        Direct2DTextRenderDetail detail,
        ID2D1Brush brush)
    {
        var bounds = text.TextBounds;
        if (detail is Direct2DTextRenderDetail.Skip or Direct2DTextRenderDetail.Full ||
            bounds.IsEmpty)
        {
            return;
        }

        var direction = new Vector2(
            (float)Math.Cos(text.RotationRadians),
            (float)Math.Sin(text.RotationRadians));
        var normal = new Vector2(-direction.Y, direction.X);
        var halfLength = ResolveContainedHalfLength(bounds, direction) * 0.9f;
        var center = ToVector2(bounds.Center);
        var strokeWidth = ResolveProxyStrokeWidth(context);

        if (detail == Direct2DTextRenderDetail.Summary)
        {
            var offset = normal * (float)(Math.Min(bounds.Width, bounds.Height) * 0.12);
            context.DrawLine(center - direction * halfLength - offset, center + direction * halfLength - offset, brush, strokeWidth);
            context.DrawLine(center - direction * halfLength + offset, center + direction * halfLength + offset, brush, strokeWidth);
            return;
        }

        context.DrawLine(center - direction * halfLength, center + direction * halfLength, brush, strokeWidth);
    }

    private static void DrawHorizontalTextProxy(
        ID2D1DeviceContext context,
        CadRectD bounds,
        Direct2DTextRenderDetail detail,
        ID2D1Brush brush)
    {
        var left = (float)bounds.MinX;
        var right = (float)bounds.MaxX;
        var strokeWidth = ResolveProxyStrokeWidth(context);
        if (detail == Direct2DTextRenderDetail.Summary)
        {
            var lower = (float)(bounds.MinY + bounds.Height * 0.35);
            var upper = (float)(bounds.MinY + bounds.Height * 0.72);
            context.DrawLine(new Vector2(left, lower), new Vector2(right, lower), brush, strokeWidth);
            context.DrawLine(new Vector2(left, upper), new Vector2(right, upper), brush, strokeWidth);
            return;
        }

        var baseline = (float)(bounds.MinY + bounds.Height * 0.45);
        context.DrawLine(new Vector2(left, baseline), new Vector2(right, baseline), brush, strokeWidth);
    }

    private static float ResolveContainedHalfLength(CadRectD bounds, Vector2 direction)
    {
        var halfWidth = (float)Math.Max(bounds.Width * 0.5, 0);
        var halfHeight = (float)Math.Max(bounds.Height * 0.5, 0);
        var horizontal = Math.Abs(direction.X) > 1e-6f
            ? halfWidth / Math.Abs(direction.X)
            : float.PositiveInfinity;
        var vertical = Math.Abs(direction.Y) > 1e-6f
            ? halfHeight / Math.Abs(direction.Y)
            : float.PositiveInfinity;
        var result = Math.Min(horizontal, vertical);
        return float.IsFinite(result) ? result : Math.Max(halfWidth, halfHeight);
    }

    private static float ResolveProxyStrokeWidth(ID2D1DeviceContext context)
    {
        return 1.0f / Math.Max(
            (float)Direct2DEntityLevelOfDetail.ResolveMaximumScreenScale(context.Transform),
            float.Epsilon);
    }

    private void DrawText(
        ID2D1DeviceContext context,
        CadDocument document,
        CadText text,
        Direct2DResourceCache.EntityResourceBucket resources,
        ID2D1Brush strokeBrush)
    {
        var previousTransform = context.Transform;
        context.Transform = CreateWorldRotationTransform(text.RotationRadians, text.Position, previousTransform);
        try
        {
            if (text.IsInverted)
            {
                FillBounds(context, text.InvertedBackgroundBounds, strokeBrush);
                var invertedBrush = styleResources.GetBrush(context, document.ViewSettings.BackgroundColor);
                DrawTextClipped(context, resources.TextLayout!, text.Position, text.TextBounds, invertedBrush);
                return;
            }

            DrawTextClipped(context, resources.TextLayout!, text.Position, text.TextBounds, strokeBrush);
        }
        finally
        {
            context.Transform = previousTransform;
        }
    }

    private void DrawFill(
        ID2D1DeviceContext context,
        CadEntity entity,
        ID2D1Geometry geometry,
        CadRectD bounds,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadViewport viewport,
        CadRenderOptions options)
    {
        if (resources.FillBrush is not null &&
            !resourceCache.TryDrawFilledGeometry(
                context,
                entity,
                resources,
                geometry,
                resources.FillBrush))
        {
            context.FillGeometry(geometry, resources.FillBrush);
        }
        if (resources.HatchBrush is null ||
            resources.HatchRenderData is not { } hatch ||
            resourceCache.Factory is null ||
            bounds.IsEmpty)
        {
            return;
        }

        Direct2DHatchRenderer.Draw(
            context,
            geometry,
            bounds,
            hatch,
            resources.HatchBrush,
            viewport,
            options.IsLevelOfDetailEnabled);
    }

    private static void FillBounds(ID2D1DeviceContext context, CadRectD bounds, ID2D1Brush brush)
    {
        if (!bounds.IsEmpty)
            context.FillRectangle(ToRawRect(bounds), brush);
    }

    private static void DrawTextClipped(
        ID2D1DeviceContext context,
        IDWriteTextLayout layout,
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
            context.DrawTextLayout(
                ToVector2(origin),
                layout,
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

    private static bool StrokeWidthChangesWithScale(
        float modelWidth,
        float resolvedWidth,
        CadRenderOptions options)
    {
        return options.KeepStrokeWidthScreenConstant ||
               Math.Abs(resolvedWidth - modelWidth) >
               Math.Max(1e-6f, Math.Abs(modelWidth) * 1e-5f);
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
