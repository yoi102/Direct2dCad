using System.Numerics;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Direct2D.Resources;
using Direct2dCad.Rendering.Handles;
using Vortice;
using Vortice.Direct2D1;

namespace Direct2dCad.Rendering.Direct2D.Overlays;

internal sealed class Direct2DHandleRenderer(Direct2DStyleResourceCache styleResources)
{
    public void DrawGrip(
        ID2D1DeviceContext deviceContext,
        ID2D1Factory? factory,
        CadViewport viewport,
        CadGripHandle grip)
    {
        var halfSize = ResolveHalfSize(grip.Style, viewport);
        if (halfSize <= 0)
            return;

        var bounds = CadRectD.FromLTRB(
            grip.Position.X - halfSize,
            grip.Position.Y - halfSize,
            grip.Position.X + halfSize,
            grip.Position.Y + halfSize);
        var strokeBrush = styleResources.GetBrush(deviceContext, grip.Style.StrokeColor);
        var fillBrush = factory is null || grip.Style.FillColor.IsTransparent
            ? null
            : styleResources.GetBrush(deviceContext, grip.Style.FillColor);
        var minimumStrokeWidth = grip.Style.Shape == CadHandleShape.Diamond ? 0.1 : 0.5;
        var strokeWidth = ResolveStrokeWidth(grip.Style, viewport, minimumStrokeWidth);

        switch (grip.Style.Shape)
        {
            case CadHandleShape.Circle:
                {
                    var ellipse = new Ellipse(ToVector2(bounds.Center), (float)halfSize, (float)halfSize);
                    if (fillBrush is not null)
                        deviceContext.FillEllipse(ellipse, fillBrush);
                    deviceContext.DrawEllipse(ellipse, strokeBrush, strokeWidth);
                    break;
                }
            case CadHandleShape.Diamond:
                DrawDiamond(deviceContext, factory, bounds, fillBrush, strokeBrush, strokeWidth);
                break;
            default:
                {
                    var rectangle = ToRawRect(bounds);
                    if (fillBrush is not null)
                        deviceContext.FillRectangle(rectangle, fillBrush);
                    deviceContext.DrawRectangle(rectangle, strokeBrush, strokeWidth);
                    break;
                }
        }
    }

    private void DrawDiamond(
        ID2D1DeviceContext deviceContext,
        ID2D1Factory? factory,
        CadRectD bounds,
        ID2D1Brush? fillBrush,
        ID2D1Brush strokeBrush,
        float strokeWidth)
    {
        Span<Vector2> points =
        [
            new Vector2((float)bounds.Center.X, (float)bounds.MinY),
            new Vector2((float)bounds.MaxX, (float)bounds.Center.Y),
            new Vector2((float)bounds.Center.X, (float)bounds.MaxY),
            new Vector2((float)bounds.MinX, (float)bounds.Center.Y)
        ];

        if (fillBrush is not null && styleResources.GetUnitDiamondGeometry(factory) is { } geometry)
        {
            var previousTransform = deviceContext.Transform;
            deviceContext.Transform = Matrix3x2.CreateScale(
                                          (float)(bounds.Width * 0.5),
                                          (float)(bounds.Height * 0.5)) *
                                      Matrix3x2.CreateTranslation(ToVector2(bounds.Center)) *
                                      previousTransform;
            try
            {
                deviceContext.FillGeometry(geometry, fillBrush);
            }
            finally
            {
                deviceContext.Transform = previousTransform;
            }
        }

        for (var index = 0; index < points.Length; index++)
            deviceContext.DrawLine(points[index], points[(index + 1) % points.Length], strokeBrush, strokeWidth);
    }

    private static double ResolveHalfSize(CadHandleStyle style, CadViewport viewport)
    {
        var size = Math.Max(style.Size, 0.0);
        return style.KeepSizeScreenConstant
            ? size * 0.5 / Math.Max(viewport.Zoom, double.Epsilon)
            : size * 0.5;
    }

    private static float ResolveStrokeWidth(
        CadHandleStyle style,
        CadViewport viewport,
        double minimumStrokeWidth)
    {
        var width = Math.Max(style.StrokeWidth, minimumStrokeWidth);
        return style.KeepSizeScreenConstant
            ? (float)(width / Math.Max(viewport.Zoom, double.Epsilon))
            : (float)width;
    }

    private static Vector2 ToVector2(CadPointD point) => new((float)point.X, (float)point.Y);

    private static RawRectF ToRawRect(CadRectD bounds)
    {
        return new RawRectF((float)bounds.MinX, (float)bounds.MinY, (float)bounds.MaxX, (float)bounds.MaxY);
    }
}
