using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;
using Vortice.DirectWrite;

namespace Direct2dCad.Rendering.Direct2D;

internal static class Direct2DTextServices
{
    private const float LayoutExtent = 1_000_000.0f;

    public static IDWriteTextFormat? CreateTextFormat(
        IDWriteFactory? writeFactory,
        CadDocument document,
        CadText text)
    {
        if (writeFactory is null)
            return null;

        var style = ResolveTextStyle(document, text.TextStyleId);
        return CreateTextFormat(writeFactory, style, text.Height);
    }

    public static IDWriteTextFormat? CreateTextFormat(
        IDWriteFactory? writeFactory,
        CadDocument document,
        StyleId? textStyleId,
        double height)
    {
        if (writeFactory is null)
            return null;

        var style = ResolveTextStyle(document, textStyleId);
        return CreateTextFormat(writeFactory, style, height);
    }

    public static bool TryMeasureTextBounds(
        IDWriteFactory? writeFactory,
        CadDocument document,
        CadText text,
        out CadRectD localBounds)
    {
        return TryMeasureTextBounds(
            writeFactory,
            document,
            text.Text,
            text.Height,
            text.TextStyleId,
            out localBounds);
    }

    public static bool TryMeasureTextBounds(
        IDWriteFactory? writeFactory,
        CadDocument document,
        string text,
        double height,
        StyleId? textStyleId,
        out CadRectD localBounds)
    {
        localBounds = CadRectD.Empty;

        if (writeFactory is null ||
            height <= 0 ||
            double.IsNaN(height) ||
            double.IsInfinity(height))
        {
            return false;
        }

        var safeText = string.IsNullOrEmpty(text) ? " " : text;
        using var format = CreateTextFormat(writeFactory, ResolveTextStyle(document, textStyleId), height);
        using var layout = writeFactory.CreateTextLayout(safeText, format, LayoutExtent, LayoutExtent);
        var metrics = layout.Metrics;
        var overhang = layout.OverhangMetrics;

        var left = (double)Math.Min(metrics.Left, -overhang.Left);
        var top = (double)Math.Min(metrics.Top, -overhang.Top);
        var right = (double)Math.Max(
            metrics.Left + Math.Max(metrics.WidthIncludingTrailingWhitespace, metrics.Width),
            metrics.Left + Math.Max(metrics.WidthIncludingTrailingWhitespace, metrics.Width) + overhang.Right);
        var bottom = (double)Math.Max(
            metrics.Top + metrics.Height,
            metrics.Top + metrics.Height + overhang.Bottom);

        var minWidth = Math.Max(height * CadText.FontSizeScale * 0.25, 1e-6);
        var minHeight = Math.Max(height, 1e-6);
        if (right - left < minWidth)
            right = left + minWidth;
        if (bottom - top < minHeight)
            bottom = top + minHeight;

        localBounds = CadRectD.FromLTRB(left, top, right, bottom);
        return !localBounds.IsEmpty;
    }

    private static IDWriteTextFormat CreateTextFormat(
        IDWriteFactory writeFactory,
        CadTextStyle? style,
        double height)
    {
        var fontFamily = style?.FontFamily ?? "Meiryo";
        var fontWeight = style?.IsBold == true ? FontWeight.Bold : FontWeight.Normal;
        var fontStyle = style?.IsItalic == true ? FontStyle.Italic : FontStyle.Normal;
        var fontSize = (float)(height * CadText.FontSizeScale);

        var format = writeFactory.CreateTextFormat(
            fontFamily,
            null,
            fontWeight,
            fontStyle,
            FontStretch.Normal,
            fontSize,
            "ja-JP");

        format.TextAlignment = TextAlignment.Leading;
        format.ParagraphAlignment = ParagraphAlignment.Near;
        format.WordWrapping = WordWrapping.NoWrap;
        return format;
    }

    private static CadTextStyle? ResolveTextStyle(CadDocument document, StyleId? styleId)
    {
        return styleId is not null &&
               document.TryGetStyle(styleId.Value, out var style) &&
               style is CadTextStyle textStyle
            ? textStyle
            : null;
    }
}
