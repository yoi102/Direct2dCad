using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D;

namespace Direct2dCad.ViewModels.Services.Text;

internal sealed class CadTextMeasurementService(
    CadDocument document,
    Direct2DImageRenderHost renderHost,
    CadViewport viewport)
{
    public CadRectD CreateTextBounds(
        string text,
        CadPointD position,
        double height,
        StyleId? textStyleId = null)
    {
        return renderHost.TryMeasureTextBounds(
            document,
            text,
            position,
            height,
            textStyleId,
            out var bounds)
            ? bounds
            : CadText.CreateUnmeasuredBounds(position, height);
    }

    public double ResolveTextBoxHeight(string text, StyleId? textStyleId = null)
    {
        var grid = document.ViewSettings.Grid;
        var spacingY = grid.GetSnapSpacingY();
        return IsFinitePositive(spacingY)
            ? SnapTextHeightUp(text, spacingY, grid.GetSnapSpacingX(), spacingY, textStyleId) * 25
            : Math.Max(8.0 / Math.Max(viewport.Zoom, double.Epsilon) * 25, 1.0);
    }

    public double MeasureTextWidth(string text, double height, StyleId? textStyleId = null)
    {
        if (renderHost.TryMeasureTextBounds(
            document,
            text,
            CadPointD.Origin,
            height,
            textStyleId,
            out var bounds))
        {
            return bounds.Width;
        }

        return CadText.CreateUnmeasuredBounds(CadPointD.Origin, height).Width;
    }

    public double SnapTextHeightUp(
        string text,
        double baseHeight,
        double snapSpacingX,
        double snapSpacingY,
        StyleId? textStyleId = null)
    {
        var heightStep = IsFinitePositive(snapSpacingY)
            ? snapSpacingY
            : IsFinitePositive(snapSpacingX)
                ? snapSpacingX
                : 1.0;
        var startStep = Math.Max(1, (int)Math.Ceiling(Math.Max(baseHeight, heightStep) / heightStep));

        for (var offset = 0; offset < 128; offset++)
        {
            var height = heightStep * (startStep + offset);
            if (IsDimensionAligned(MeasureTextWidth(text, height, textStyleId), snapSpacingX))
                return height;
        }

        return heightStep * startStep;
    }

    public static double GetCachedTextWidthFactor(CadText text)
    {
        return IsFinitePositive(text.Height) && IsFinitePositive(text.LocalBounds.Width)
            ? text.LocalBounds.Width / text.Height
            : 1.0;
    }

    public static double GetCachedShapeTextWidthFactor(CadShapeText text)
    {
        return IsFinitePositive(text.Height) && IsFinitePositive(text.TextBounds.Width)
            ? Math.Max(text.TextBounds.Width / text.Height, 1e-6)
            : Math.Max(text.WidthFactor, 1e-6);
    }

    public static CadRectD CreateShapeTextPreviewBounds(
        string text,
        CadPointD position,
        double height,
        double widthFactor,
        double characterSpacingFactor,
        double obliqueAngleRadians,
        double rotationRadians,
        bool isInverted,
        double invertedMarginFactor,
        CadShapeFontId shapeFontId)
    {
        var bounds = CadShapeFontMetrics.MeasureBounds(
            text,
            position,
            height,
            widthFactor,
            characterSpacingFactor,
            obliqueAngleRadians,
            rotationRadians,
            shapeFontId);

        return isInverted
            ? bounds.Inflate(height * Math.Max(invertedMarginFactor, 0))
            : bounds;
    }

    private static bool IsDimensionAligned(double value, double step)
    {
        if (!IsFinitePositive(step))
            return true;

        var ratio = value / step;
        return Math.Abs(ratio - Math.Round(ratio)) <= 1e-6;
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
