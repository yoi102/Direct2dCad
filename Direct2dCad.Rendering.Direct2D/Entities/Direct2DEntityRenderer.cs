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
    Direct2DStyleResourceCache styleResources,
    Direct2DRenderStatisticsCollector statistics)
{
    private const float BaselineProxyScreenStrokeWidth = 0.55f;
    private const float SummaryProxyScreenStrokeWidth = 0.50f;
    private const double BaselineProxyOpacity = 0.65;
    private const double SummaryProxyOpacity = 0.55;

    public void Draw(
        ID2D1DeviceContext context,
        CadDocument document,
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadViewport viewport,
        CadRenderOptions options,
        ID2D1Brush? strokeBrushOverride = null,
        float? strokeWidthOverride = null,
        CadColor? strokeColorOverride = null)
    {
        var strokeBrush = strokeBrushOverride ?? resources.StrokeBrush;
        var strokeWidth = strokeWidthOverride ?? resources.StrokeWidth;
        var renderDetail = Direct2DEntityLevelOfDetail.Resolve(
            entity,
            resources,
            context.Transform,
            options,
            strokeWidthOverride);
        if (renderDetail == Direct2DEntityRenderDetail.Skip)
            return;
        if (TryDrawSimplified(
                context,
                entity,
                resources,
                options,
                strokeBrush,
                strokeColorOverride ?? resources.StrokeColor,
                renderDetail))
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
            if (resources.GraphicLineTypeStrokeStyle is not null ||
                !options.EnableGeometryRealizations ||
                !resourceCache.TryDrawStrokedGeometry(
                    context,
                    entity,
                    resources,
                    resources.Geometry,
                    invertedBrush,
                    resolvedStrokeWidth,
                    strokeStyle: null,
                    Direct2DStrokeRealizationStyleKey.Default,
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
            var geometrySimplified = !ReferenceEquals(geometry, resources.Geometry);
            var useLevelOfDetailStrokeStyle = geometrySimplified ||
                                              Direct2DEntityLevelOfDetail.ShouldSimplifyStrokeStyle(
                                                  entity,
                                                  context.Transform,
                                                  options);
            var strokeStyle = ResolveStrokeStyle(
                context,
                entity,
                resources,
                options,
                geometrySimplified);
            // A custom graphic line type is not part of the realization key. Do
            // not reuse a realization built for another dash pattern; draw it
            // with the cached Direct2D stroke style instead.
            var canUseStrokeRealization = resources.GraphicLineTypeStrokeStyle is null;
            if (!options.EnableGeometryRealizations ||
                !canUseStrokeRealization ||
                !resourceCache.TryDrawStrokedGeometry(
                    context,
                    entity,
                    resources,
                    geometry,
                    strokeBrush,
                    resolvedStrokeWidth,
                    strokeStyle,
                    useLevelOfDetailStrokeStyle
                        ? Direct2DStrokeRealizationStyleKey.ForLevelOfDetail(entity.StrokeStyle)
                        : Direct2DStrokeRealizationStyleKey.ForEntity(entity.StrokeStyle),
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

    private bool TryDrawSimplified(
        ID2D1DeviceContext context,
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadRenderOptions options,
        ID2D1Brush? strokeBrush,
        CadColor? strokeColor,
        Direct2DEntityRenderDetail renderDetail)
    {
        if (renderDetail != Direct2DEntityRenderDetail.Simplified)
        {
            return false;
        }

        var textDetail = Direct2DEntityLevelOfDetail.ResolveText(
            entity,
            context.Transform,
            options);
        var brush = strokeBrush ?? resources.FillBrush ?? resources.HatchBrush ?? resources.BitmapBrush;
        if (entity is CadText or CadShapeText && strokeColor is { } color)
        {
            brush = styleResources.GetBrush(
                context,
                ResolveTextProxyColor(color, textDetail));
        }
        if (brush is null)
            return true;

        switch (entity)
        {
            case CadText text:
                DrawTextProxy(
                    context,
                    text,
                    textDetail,
                    brush,
                    options.TransformScaleMultiplier);
                return true;
            case CadShapeText shapeText:
                DrawShapeTextProxy(
                    context,
                    shapeText,
                    textDetail,
                    brush,
                    options.TransformScaleMultiplier);
                return true;
        }

        DrawPointProxy(
            context,
            entity.Bounds,
            brush,
            options.TransformScaleMultiplier);
        return true;
    }

    internal static void DrawPointProxy(
        ID2D1DeviceContext context,
        CadRectD bounds,
        ID2D1Brush brush,
        double transformScaleMultiplier = 1.0)
    {
        if (bounds.IsEmpty)
            return;

        var halfSize = 0.5f / ResolveEffectiveScreenScale(
            context,
            transformScaleMultiplier);
        var center = ToVector2(bounds.Center);
        context.FillRectangle(
            new RawRectF(
                center.X - halfSize,
                center.Y - halfSize,
                center.X + halfSize,
                center.Y + halfSize),
            brush);
    }

    internal static void DrawRectangularProxy(
        ID2D1DeviceContext context,
        CadRectD bounds,
        ID2D1Brush brush,
        double transformScaleMultiplier = 1.0,
        float? strokeWidth = null)
    {
        if (bounds.IsEmpty)
            return;

        var screenScale = ResolveEffectiveScreenScale(
            context,
            transformScaleMultiplier);
        context.DrawRectangle(
            ToRawRect(bounds),
            brush,
            Math.Max(strokeWidth ?? 1.0f / screenScale, 1.0f / screenScale));
    }

    private void DrawLine(
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
            ResolveStrokeStyle(context, line, resources, options));
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
                ResolveStrokeStyle(context, entity, resources, options));
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
                var strokeStyle = ResolveStrokeStyle(
                    context,
                    rectangle,
                    resources,
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
                ResolveStrokeStyle(context, rectangle, resources, options));
        }
    }

    private ID2D1StrokeStyle? ResolveStrokeStyle(
        ID2D1DeviceContext context,
        CadEntity entity,
        Direct2DResourceCache.EntityResourceBucket resources,
        CadRenderOptions options,
        bool geometrySimplified = false)
    {
        if (resources.GraphicLineTypeStrokeStyle is not null)
            return resources.GraphicLineTypeStrokeStyle;

        if (!geometrySimplified &&
            !Direct2DEntityLevelOfDetail.ShouldSimplifyStrokeStyle(
                entity,
                context.Transform,
                options))
        {
            return resources.StrokeStyle ?? resources.GraphicLineTypeStrokeStyle;
        }

        return styleResources.GetLevelOfDetailStrokeStyle(
            resourceCache.Factory,
            entity.StrokeStyle);
    }

    private static void DrawTextProxy(
        ID2D1DeviceContext context,
        CadText text,
        Direct2DTextRenderDetail detail,
        ID2D1Brush brush,
        double transformScaleMultiplier)
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
            DrawHorizontalTextProxy(
                context,
                text.TextBounds,
                detail,
                brush,
                transformScaleMultiplier);
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
        ID2D1Brush brush,
        double transformScaleMultiplier)
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
        var strokeWidth = ResolveProxyStrokeWidth(
            context,
            transformScaleMultiplier,
            detail);

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
        ID2D1Brush brush,
        double transformScaleMultiplier)
    {
        var left = (float)bounds.MinX;
        var right = (float)bounds.MaxX;
        var strokeWidth = ResolveProxyStrokeWidth(
            context,
            transformScaleMultiplier,
            detail);
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

    private static float ResolveProxyStrokeWidth(
        ID2D1DeviceContext context,
        double transformScaleMultiplier,
        Direct2DTextRenderDetail detail)
    {
        var screenStrokeWidth = detail == Direct2DTextRenderDetail.Summary
            ? SummaryProxyScreenStrokeWidth
            : BaselineProxyScreenStrokeWidth;
        return screenStrokeWidth /
               ResolveEffectiveScreenScale(context, transformScaleMultiplier);
    }

    private static CadColor ResolveTextProxyColor(
        CadColor color,
        Direct2DTextRenderDetail detail)
    {
        var opacity = detail == Direct2DTextRenderDetail.Summary
            ? SummaryProxyOpacity
            : BaselineProxyOpacity;
        var alpha = (byte)Math.Clamp(
            (int)Math.Round(color.A * opacity),
            0,
            byte.MaxValue);
        return CadColor.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static float ResolveEffectiveScreenScale(
        ID2D1DeviceContext context,
        double transformScaleMultiplier)
    {
        if (!double.IsFinite(transformScaleMultiplier) ||
            transformScaleMultiplier <= double.Epsilon)
        {
            transformScaleMultiplier = 1.0;
        }

        return Math.Max(
            (float)(Direct2DEntityLevelOfDetail.ResolveMaximumScreenScale(context.Transform) *
                    transformScaleMultiplier),
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
            (!options.EnableGeometryRealizations ||
             !resourceCache.TryDrawFilledGeometry(
                context,
                entity,
                resources,
                geometry,
                resources.FillBrush)))
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
            options.IsLevelOfDetailEnabled,
            options.TransformScaleMultiplier,
            resourceCache.HatchTiles,
            statistics);
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

    internal static float ResolveStrokeWidth(float modelWidth, CadViewport viewport, CadRenderOptions options)
    {
        var zoom = Math.Max((float)viewport.Zoom, float.Epsilon);
        var rasterScale = ResolveEntityStrokeScaleMultiplier(options);
        var width = options.KeepStrokeWidthScreenConstant
            ? modelWidth * rasterScale / zoom
            : modelWidth;
        return Math.Max(
            width,
            (float)options.MinimumScreenStrokeWidth * rasterScale / zoom);
    }

    private static float ResolveEntityStrokeScaleMultiplier(CadRenderOptions options)
    {
        var multiplier = options.EntityStrokeScaleMultiplier;
        return double.IsFinite(multiplier) && multiplier > double.Epsilon
            ? (float)multiplier
            : 1.0f;
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
