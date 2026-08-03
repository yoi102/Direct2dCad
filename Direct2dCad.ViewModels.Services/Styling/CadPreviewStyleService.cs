using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Styling;

internal readonly struct CadPreviewStyleService(
    CadDocument document,
    CadUserSettings userSettings)
{
    public CadHandleSceneBuildOptions CreateHandleSceneBuildOptions()
    {
        var interaction = userSettings.Interaction;
        return CadHandleSceneBuildOptions.Default with
        {
            SelectionOutlineStyle = CadHandleStyle.SelectionOutline with
            {
                StrokeColor = interaction.SelectedEntityStrokeColor,
                StrokeWidth = interaction.SelectedEntityStrokeWidth
            },
            GripStyle = CadHandleStyle.Grip with
            {
                StrokeColor = interaction.GripStrokeColor,
                FillColor = interaction.GripFillColor,
                Size = interaction.GripSize,
                StrokeWidth = interaction.GripStrokeWidth
            }
        };
    }

    public CadTransientStyle CreateSelectionWindowStyle()
    {
        var interaction = userSettings.Interaction;
        return CadTransientStyle.SelectionWindow with
        {
            StrokeColor = interaction.SelectionWindowStrokeColor,
            FillColor = interaction.SelectionWindowFillColor,
            StrokeWidth = interaction.SelectionWindowStrokeWidth
        };
    }

    public CadTransientStyle CreateSelectionCrossingStyle()
    {
        var interaction = userSettings.Interaction;
        return CadTransientStyle.SelectionCrossing with
        {
            StrokeColor = interaction.SelectionCrossingStrokeColor,
            FillColor = interaction.SelectionCrossingFillColor,
            StrokeWidth = interaction.SelectionCrossingStrokeWidth
        };
    }

    public CadTransientStyle CreateEntityPreviewStyle(
        CadColor strokeColor,
        CadLineWeight lineWeight,
        CadLineWeight layerLineWeight,
        StyleId? fillStyleId = null)
    {
        var fill = ResolveTransientFill(fillStyleId);
        return new CadTransientStyle(
            strokeColor,
            ResolvePreviewStrokeWidth(lineWeight, layerLineWeight),
            CadTransientLinePattern.Solid,
            fill.FillColor,
            HatchFill: fill.HatchFill);
    }

    public CadTransientStyle CreateEntityPreviewStyle(CadEntity entity)
    {
        var layer = document.TryGetLayer(entity.LayerId, out var resolvedLayer) && resolvedLayer is not null
            ? resolvedLayer
            : document.GetLayer(LayerId.Default);
        var graphic = ResolveEntityGraphicStyle(entity, layer);
        var strokeColor = entity.ColorSource == CadColorSource.Explicit
            ? graphic?.StrokeColor ?? ResolveLayerStrokeColor(layer)
            : ResolveLayerStrokeColor(layer);
        var lineWeight = ResolveEntityLineWeight(entity, graphic, layer);

        var fill = ResolveTransientFill(ResolveEntityFillStyleId(entity));
        CadStrokeStyle? strokeStyle = entity.StrokeStyle == CadStrokeStyle.Default
            ? null
            : entity.StrokeStyle;
        CadLineTypeDefinition? lineType = null;
        if (strokeStyle is null &&
            graphic is not null &&
            graphic.LineTypeId != LineTypeId.Continuous &&
            document.LineTypes.TryGetValue(graphic.LineTypeId, out var resolvedLineType))
        {
            lineType = resolvedLineType;
        }

        return new CadTransientStyle(
            strokeColor,
            ResolvePreviewStrokeWidth(lineWeight, layer.LineWeight),
            CadTransientLinePattern.Solid,
            fill.FillColor,
            HatchFill: fill.HatchFill,
            StrokeStyle: strokeStyle,
            LineType: lineType);
    }

    public CadTransientStyle CreateDrawingAuxiliaryStyle(CadColor strokeColor)
    {
        return CadTransientStyle.Construction with
        {
            StrokeColor = strokeColor,
            StrokeWidth = 1.0,
            LinePattern = CadTransientLinePattern.Dash,
            FillColor = null
        };
    }

    public CadTransientStyle CreateGripAuxiliaryStyle()
    {
        return CadTransientStyle.Construction with
        {
            StrokeColor = userSettings.Interaction.GripPreviewStrokeColor,
            StrokeWidth = userSettings.Interaction.GripPreviewStrokeWidth,
            LinePattern = CadTransientLinePattern.Dash,
            FillColor = null
        };
    }

    public CadColor ResolveLayerStrokeColor(CadLayer layer)
    {
        return layer.DefaultGraphicStyleId is { } styleId &&
               document.TryGetStyle(styleId, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic.StrokeColor
            : layer.Color;
    }

    public static double ResolveLineWeightDisplayValue(CadLineWeight lineWeight)
    {
        return lineWeight.IsByLayer || lineWeight.Value <= 0
            ? CadLineWeight.Default.Value
            : lineWeight.Value;
    }

    private (CadColor? FillColor, CadTransientHatchFill? HatchFill) ResolveTransientFill(StyleId? fillStyleId)
    {
        if (fillStyleId is not { } styleId ||
            !document.TryGetStyle(styleId, out var style))
        {
            return (null, null);
        }

        if (style is CadGradientFillStyle { IsSolid: true } fillStyle)
        {
            var color = fillStyle.Stops[0].Color;
            return (color.IsTransparent ? null : color, null);
        }

        if (style is CadHatchFillStyle hatchStyle &&
            document.TryGetHatchPattern(hatchStyle.PatternId, out var pattern) &&
            pattern is not null)
        {
            return (null, new CadTransientHatchFill(
                hatchStyle.ForegroundColor,
                hatchStyle.HatchScale,
                hatchStyle.HatchAngle,
                hatchStyle.HatchOrigin,
                pattern.Lines.ToArray()));
        }

        return (null, null);
    }

    private CadGraphicStyle? ResolveEntityGraphicStyle(CadEntity entity, CadLayer layer)
    {
        var styleId = ResolveEntityGraphicStyleId(entity) ?? layer.DefaultGraphicStyleId;
        return styleId is { } graphicStyleId &&
               document.TryGetStyle(graphicStyleId, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic
            : null;
    }

    private static StyleId? ResolveEntityGraphicStyleId(CadEntity entity)
    {
        return entity switch
        {
            CadLine line => line.GraphicStyleId,
            CadCircle circle => circle.GraphicStyleId,
            CadEllipse ellipse => ellipse.GraphicStyleId,
            CadEllipseArc ellipseArc => ellipseArc.GraphicStyleId,
            CadRectangle rectangle => rectangle.GraphicStyleId,
            CadArc arc => arc.GraphicStyleId,
            CadPolyline polyline => polyline.GraphicStyleId,
            CadSpline spline => spline.GraphicStyleId,
            CadCompositePath path => path.GraphicStyleId,
            CadText text => text.GraphicStyleId,
            CadShapeText shapeText => shapeText.GraphicStyleId,
            CadBlockReference blockReference => blockReference.GraphicStyleId,
            _ => null
        };
    }

    private static StyleId? ResolveEntityFillStyleId(CadEntity entity)
    {
        return entity switch
        {
            CadCircle circle => circle.FillStyleId,
            CadEllipse ellipse => ellipse.FillStyleId,
            CadRectangle rectangle => rectangle.FillStyleId,
            CadPolyline { Closed: true } polyline => polyline.FillStyleId,
            CadSpline { Closed: true } spline => spline.FillStyleId,
            CadCompositePath { Closed: true } path => path.FillStyleId,
            _ => null
        };
    }

    private static CadLineWeight ResolveEntityLineWeight(
        CadEntity entity,
        CadGraphicStyle? graphic,
        CadLayer layer)
    {
        if (entity.UseLayerLineWeight)
            return layer.LineWeight;

        return entity.LineWeight switch
        {
            { IsByLayer: false } explicitWeight => explicitWeight,
            { IsByLayer: true } => layer.LineWeight,
            _ => graphic?.LineWeight is { IsByLayer: false } styleWeight
                ? styleWeight
                : layer.LineWeight
        };
    }

    private static double ResolvePreviewStrokeWidth(CadLineWeight lineWeight, CadLineWeight layerLineWeight)
    {
        var resolved = lineWeight.IsByLayer ? layerLineWeight : lineWeight;
        return ResolveLineWeightDisplayValue(resolved);
    }
}
