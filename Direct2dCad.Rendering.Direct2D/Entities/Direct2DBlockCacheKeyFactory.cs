using System.Numerics;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering.Direct2D.Scene;

namespace Direct2dCad.Rendering.Direct2D.Entities;

internal static class Direct2DBlockCacheKeyFactory
{
    public static Direct2DBlockDefinitionCacheKey Create(
        BlockId blockId,
        Direct2DBlockRenderStyle style,
        CadViewport viewport,
        CadRenderOptions options,
        Matrix3x2 localToTarget)
    {
        var multiplier = ResolveScaleMultiplier(options);
        var scaleX = Math.Sqrt(
            localToTarget.M11 * localToTarget.M11 +
            localToTarget.M12 * localToTarget.M12) * multiplier;
        var scaleY = Math.Sqrt(
            localToTarget.M21 * localToTarget.M21 +
            localToTarget.M22 * localToTarget.M22) * multiplier;
        return Create(blockId, style, viewport, options, scaleX, scaleY);
    }

    public static Direct2DBlockDefinitionCacheKey Create(
        BlockId blockId,
        Direct2DBlockRenderStyle style,
        CadViewport viewport,
        CadRenderOptions options,
        double screenScaleX,
        double screenScaleY)
    {
        var viewZoom = Direct2DRenderScaleBucket.Quantize(viewport.Zoom);
        var quantizedScaleX = Direct2DRenderScaleBucket.Quantize(
            Math.Max(screenScaleX, double.Epsilon));
        var quantizedScaleY = Direct2DRenderScaleBucket.Quantize(
            Math.Max(screenScaleY, double.Epsilon));
        return new Direct2DBlockDefinitionCacheKey(
            blockId,
            style.EffectiveLayer.Id,
            style.ReferenceColor,
            BitConverter.DoubleToInt64Bits(
                Direct2DBlockReferenceStyleResolver.ResolveLayerStrokeWidth(
                    style.EffectiveLayer)),
            BitConverter.DoubleToInt64Bits(viewZoom),
            BitConverter.DoubleToInt64Bits(quantizedScaleX),
            BitConverter.DoubleToInt64Bits(quantizedScaleY),
            options.IsAntialiasingEnabled,
            options.IsTextAntialiasingEnabled,
            options.EnableGeometryRealizations,
            options.IsLevelOfDetailEnabled,
            options.KeepStrokeWidthScreenConstant,
            BitConverter.DoubleToInt64Bits(options.MinimumScreenStrokeWidth),
            BitConverter.DoubleToInt64Bits(options.EntityLineWeightWorldScale));
    }

    public static double ResolveScaleMultiplier(CadRenderOptions options)
    {
        return double.IsFinite(options.TransformScaleMultiplier) &&
               options.TransformScaleMultiplier > double.Epsilon
            ? options.TransformScaleMultiplier
            : 1.0;
    }
}

internal readonly record struct Direct2DBlockCacheRequestProfileKey(
    BlockId OwnerBlockId,
    long ViewZoomBits,
    long TransformScaleMultiplierBits,
    bool IsAntialiasingEnabled,
    bool IsTextAntialiasingEnabled,
    bool EnableGeometryRealizations,
    bool IsLevelOfDetailEnabled,
    bool KeepStrokeWidthScreenConstant,
    long MinimumScreenStrokeWidthBits,
    long EntityLineWeightWorldScaleBits)
{
    public static Direct2DBlockCacheRequestProfileKey Create(
        CadRenderOptions options,
        double viewportZoom) => new(
        options.ActiveOwnerBlockId,
        BitConverter.DoubleToInt64Bits(Direct2DRenderScaleBucket.Quantize(viewportZoom)),
        BitConverter.DoubleToInt64Bits(
            Direct2DRenderScaleBucket.Quantize(
                Direct2DBlockCacheKeyFactory.ResolveScaleMultiplier(options))),
        options.IsAntialiasingEnabled,
        options.IsTextAntialiasingEnabled,
        options.EnableGeometryRealizations,
        options.IsLevelOfDetailEnabled,
        options.KeepStrokeWidthScreenConstant,
        BitConverter.DoubleToInt64Bits(options.MinimumScreenStrokeWidth),
        BitConverter.DoubleToInt64Bits(options.EntityLineWeightWorldScale));
}
