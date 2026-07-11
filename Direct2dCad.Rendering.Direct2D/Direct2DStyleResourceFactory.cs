using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering.Transient;
using Vortice;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace Direct2dCad.Rendering.Direct2D;

internal sealed class Direct2DStyleResourceFactory
{
    public ID2D1SolidColorBrush CreateBrush(ID2D1DeviceContext context, CadColor color)
    {
        return context.CreateSolidColorBrush(new Color4(
            color.R / 255.0f,
            color.G / 255.0f,
            color.B / 255.0f,
            color.A / 255.0f));
    }

    public ID2D1StrokeStyle? CreateStrokeStyle(ID2D1Factory? factory, CadTransientStyle style)
    {
        if (factory is null || style.LinePattern == CadTransientLinePattern.Solid)
            return null;

        var dashStyle = style.LinePattern switch
        {
            CadTransientLinePattern.Dot => DashStyle.Dot,
            CadTransientLinePattern.DashDot => DashStyle.DashDot,
            _ => DashStyle.Dash
        };
        return factory.CreateStrokeStyle(new StrokeStyleProperties
        {
            StartCap = CapStyle.Flat,
            EndCap = CapStyle.Flat,
            DashCap = CapStyle.Flat,
            LineJoin = LineJoin.Miter,
            DashStyle = dashStyle
        });
    }

    public float ResolveStrokeWidth(CadTransientStyle style, CadViewport viewport)
    {
        var zoom = Math.Max(viewport.Zoom, double.Epsilon);
        var width = Math.Max(style.StrokeWidth, 0.1);
        var strokeWidth = style.KeepStrokeWidthScreenConstant
            ? (float)(width / zoom)
            : (float)width;
        var minimumStrokeWidth = (float)(Math.Max(style.MinimumScreenStrokeWidth, 0.0) / zoom);
        return Math.Max(strokeWidth, minimumStrokeWidth);
    }
}
