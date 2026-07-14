using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DTransientRenderer(
    Direct2DResourceCache resourceCache,
    Direct2DGeometryFactory geometryFactory,
    Direct2DStyleResourceCache styleResources,
    Direct2DTextFormatResourceCache textFormatResources)
{
    public void DrawLine(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadPointD start,
        CadPointD end,
        CadTransientStyle style)
    {
        var brush = styleResources.GetBrush(context, style.StrokeColor);
        var strokeStyle = styleResources.GetStrokeStyle(resourceCache.Factory, style);
        context.DrawLine(
            ToVector2(start),
            ToVector2(end),
            brush,
            styleResources.ResolveStrokeWidth(style, viewport),
            strokeStyle);
    }

    public void DrawPolyline(
        ID2D1DeviceContext context,
        CadViewport viewport,
        IReadOnlyList<CadPointD> points,
        bool closed,
        CadTransientStyle style)
    {
        if (points.Count < 2)
            return;

        var brush = styleResources.GetBrush(context, style.StrokeColor);
        var strokeStyle = styleResources.GetStrokeStyle(resourceCache.Factory, style);
        var strokeWidth = styleResources.ResolveStrokeWidth(style, viewport);
        if (resourceCache.Factory is not { } factory || !closed || !HasFill(style))
        {
            for (var index = 1; index < points.Count; index++)
                context.DrawLine(ToVector2(points[index - 1]), ToVector2(points[index]), brush, strokeWidth, strokeStyle);
            if (closed && points.Count > 2)
                context.DrawLine(ToVector2(points[^1]), ToVector2(points[0]), brush, strokeWidth, strokeStyle);
            return;
        }

        using var geometry = geometryFactory.CreatePolyline(factory, points, closed);
        DrawFill(context, geometry, BoundsFromPoints(points), style, viewport);
        context.DrawGeometry(geometry, brush, strokeWidth, strokeStyle);
    }

    public void DrawSpline(
        ID2D1DeviceContext context,
        CadViewport viewport,
        IReadOnlyList<CadPointD> fitPoints,
        bool closed,
        CadTransientStyle style)
    {
        if (resourceCache.Factory is not { } factory || fitPoints.Count < 2)
            return;

        using var geometry = geometryFactory.CreateSpline(factory, fitPoints, closed);
        var brush = styleResources.GetBrush(context, style.StrokeColor);
        var strokeStyle = styleResources.GetStrokeStyle(factory, style);
        if (closed && HasFill(style))
            DrawFill(context, geometry, BoundsFromPoints(fitPoints), style, viewport);
        context.DrawGeometry(geometry, brush, styleResources.ResolveStrokeWidth(style, viewport), strokeStyle);
    }

    public void DrawArc(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadPointD center,
        double radius,
        double startAngleRadians,
        double sweepAngleRadians,
        CadTransientStyle style)
    {
        if (resourceCache.Factory is not { } factory || radius <= 0 || Math.Abs(sweepAngleRadians) <= double.Epsilon)
            return;

        using var geometry = geometryFactory.CreateArc(factory, center, radius, startAngleRadians, sweepAngleRadians);
        DrawGeometry(context, viewport, geometry, style);
    }

    public void DrawEllipseArc(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadPointD center,
        double radiusX,
        double radiusY,
        double startAngleRadians,
        double sweepAngleRadians,
        CadTransientStyle style)
    {
        if (resourceCache.Factory is not { } factory ||
            radiusX <= 0 || radiusY <= 0 || Math.Abs(sweepAngleRadians) <= double.Epsilon)
        {
            return;
        }

        using var geometry = geometryFactory.CreateEllipseArc(
            factory,
            center,
            radiusX,
            radiusY,
            startAngleRadians,
            sweepAngleRadians);
        DrawGeometry(context, viewport, geometry, style);
    }

    public void DrawCircle(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadPointD center,
        double radius,
        CadTransientStyle style)
    {
        DrawEllipse(context, viewport, center, radius, radius, style);
    }

    public void DrawEllipse(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadPointD center,
        double radiusX,
        double radiusY,
        CadTransientStyle style)
    {
        var ellipse = new Ellipse(ToVector2(center), (float)radiusX, (float)radiusY);
        if (HasHatchFill(style) && resourceCache.Factory is { } factory)
        {
            using var geometry = factory.CreateEllipseGeometry(ellipse);
            DrawFill(context, geometry, CadRectD.FromCenter(center, radiusX * 2.0, radiusY * 2.0), style, viewport);
        }
        else if (style.FillColor is { IsTransparent: false } fillColor)
        {
            var fillBrush = styleResources.GetBrush(context, fillColor);
            context.FillEllipse(ellipse, fillBrush);
        }

        var brush = styleResources.GetBrush(context, style.StrokeColor);
        var strokeStyle = styleResources.GetStrokeStyle(resourceCache.Factory, style);
        context.DrawEllipse(ellipse, brush, styleResources.ResolveStrokeWidth(style, viewport), strokeStyle);
    }

    public void DrawRectangle(
        ID2D1DeviceContext context,
        CadViewport viewport,
        CadRectD bounds,
        CadTransientStyle style,
        double cornerRadiusX = 0,
        double cornerRadiusY = 0)
    {
        var radiusX = geometryFactory.ClampCornerRadius(cornerRadiusX, bounds.Width);
        var radiusY = geometryFactory.ClampCornerRadius(cornerRadiusY, bounds.Height);
        if (radiusX > 0 && radiusY > 0)
        {
            var rounded = geometryFactory.CreateRoundedRectangle(bounds, radiusX, radiusY);
            if (HasHatchFill(style) && resourceCache.Factory is { } factory)
            {
                using var geometry = factory.CreateRoundedRectangleGeometry(rounded);
                DrawFill(context, geometry, bounds, style, viewport);
            }
            else if (style.FillColor is { IsTransparent: false } fillColor)
            {
                var fillBrush = styleResources.GetBrush(context, fillColor);
                context.FillRoundedRectangle(rounded, fillBrush);
            }

            var brush = styleResources.GetBrush(context, style.StrokeColor);
            var strokeStyle = styleResources.GetStrokeStyle(resourceCache.Factory, style);
            var strokeWidth = styleResources.ResolveStrokeWidth(style, viewport);
            if (strokeStyle is null)
                context.DrawRoundedRectangle(rounded, brush, strokeWidth);
            else
                context.DrawRoundedRectangle(rounded, brush, strokeWidth, strokeStyle);
            return;
        }

        var rectangle = ToRawRect(bounds);
        if (HasHatchFill(style) && resourceCache.Factory is { } rectangleFactory)
        {
            using var geometry = rectangleFactory.CreateRectangleGeometry(rectangle);
            DrawFill(context, geometry, bounds, style, viewport);
        }
        else if (style.FillColor is { IsTransparent: false } fillColor)
        {
            var fillBrush = styleResources.GetBrush(context, fillColor);
            context.FillRectangle(rectangle, fillBrush);
        }

        var rectangleBrush = styleResources.GetBrush(context, style.StrokeColor);
        var rectangleStrokeStyle = styleResources.GetStrokeStyle(resourceCache.Factory, style);
        context.DrawRectangle(
            rectangle,
            rectangleBrush,
            styleResources.ResolveStrokeWidth(style, viewport),
            rectangleStrokeStyle);
    }

    public void DrawText(
        ID2D1DeviceContext context,
        CadDocument document,
        CadViewport viewport,
        string text,
        CadPointD position,
        double height,
        CadRectD bounds,
        CadTransientStyle style,
        bool isInverted,
        CadColor? invertedTextColor,
        double invertedMarginFactor,
        StyleId? textStyleId,
        double rotationRadians)
    {
        if (resourceCache.WriteFactory is null || bounds.IsEmpty)
            return;

        var previousTransform = context.Transform;
        context.Transform = CreateWorldRotationTransform(rotationRadians, position, previousTransform);
        try
        {
            if (isInverted)
            {
                var fillBrush = styleResources.GetBrush(context, style.StrokeColor);
                FillBounds(context, CreateInvertedBounds(bounds, height, invertedMarginFactor), fillBrush);
            }

            var brush = styleResources.GetBrush(
                context,
                isInverted ? invertedTextColor ?? CadColor.Black : style.StrokeColor);
            var format = textFormatResources.GetForFrame(document, textStyleId, height);
            if (format is not null)
                DrawTextClipped(context, text, format, position, bounds, brush);
        }
        finally
        {
            context.Transform = previousTransform;
        }
    }

    public void DrawShapeText(
        ID2D1DeviceContext context,
        CadViewport viewport,
        string text,
        CadPointD position,
        double height,
        double rotationRadians,
        double widthFactor,
        double characterSpacingFactor,
        double obliqueAngleRadians,
        CadTransientStyle style,
        bool isInverted = false,
        CadColor? invertedTextColor = null,
        double invertedMarginFactor = CadShapeText.DefaultInvertedMarginFactor,
        CadShapeFontId shapeFontId = default)
    {
        var shapeFont = CadShapeFontRegistry.GetOrDefault(shapeFontId);
        if (isInverted)
        {
            var bounds = CadStrokeFont.MeasureBounds(
                text,
                position,
                height,
                widthFactor,
                characterSpacingFactor,
                obliqueAngleRadians,
                rotationRadians,
                shapeFont.Id);
            if (!bounds.IsEmpty)
            {
                var fillBrush = styleResources.GetBrush(context, style.StrokeColor);
                FillBounds(context, CreateInvertedBounds(bounds, height, invertedMarginFactor), fillBrush);
            }
        }

        var brush = styleResources.GetBrush(
            context,
            isInverted ? invertedTextColor ?? CadColor.Black : style.StrokeColor);
        var strokeStyle = styleResources.GetStrokeStyle(resourceCache.Factory, style);
        var strokeWidth = styleResources.ResolveStrokeWidth(style, viewport);
        foreach (var segment in CadStrokeFont.CreateSegments(
                     text,
                     position,
                     height,
                     widthFactor,
                     characterSpacingFactor,
                     obliqueAngleRadians,
                     rotationRadians,
                     shapeFont.Id))
        {
            context.DrawLine(ToVector2(segment.Start), ToVector2(segment.End), brush, strokeWidth, strokeStyle);
        }
    }

    private void DrawGeometry(
        ID2D1DeviceContext context,
        CadViewport viewport,
        ID2D1Geometry geometry,
        CadTransientStyle style)
    {
        var brush = styleResources.GetBrush(context, style.StrokeColor);
        var strokeStyle = styleResources.GetStrokeStyle(resourceCache.Factory, style);
        context.DrawGeometry(geometry, brush, styleResources.ResolveStrokeWidth(style, viewport), strokeStyle);
    }

    private void DrawFill(
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

        if (style.HatchFill is not { ForegroundColor.IsTransparent: false } hatchFill ||
            resourceCache.Factory is null ||
            bounds.IsEmpty)
        {
            return;
        }

        var hatchBrush = styleResources.GetBrush(context, hatchFill.ForegroundColor);
        Direct2DHatchRenderer.Draw(context, geometry, bounds, hatchFill, hatchBrush, viewport);
    }

    private static bool HasFill(CadTransientStyle style)
    {
        return style.FillColor is { IsTransparent: false } ||
               style.HatchFill is { ForegroundColor.IsTransparent: false, Lines.Count: > 0 };
    }

    private static bool HasHatchFill(CadTransientStyle style)
    {
        return style.HatchFill is { ForegroundColor.IsTransparent: false, Lines.Count: > 0 };
    }

    private static CadRectD BoundsFromPoints(IReadOnlyList<CadPointD> points)
    {
        var bounds = CadRectD.Empty;
        foreach (var point in points)
            bounds = bounds.ExpandToInclude(point);
        return bounds;
    }

    private static CadRectD CreateInvertedBounds(CadRectD bounds, double height, double marginFactor)
    {
        var margin = height > 0 && marginFactor > 0 && double.IsFinite(height) && double.IsFinite(marginFactor)
            ? height * marginFactor
            : 0;
        return margin > 0 ? bounds.Inflate(margin) : bounds;
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

    private static RawRectF ToRawRect(CadRectD bounds)
    {
        return new RawRectF((float)bounds.MinX, (float)bounds.MinY, (float)bounds.MaxX, (float)bounds.MaxY);
    }

    private static Vector2 ToVector2(CadPointD point) => new((float)point.X, (float)point.Y);
}
