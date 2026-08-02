using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.Commands;

/// <summary>
/// Changes a shared graphic style and reports every entity/layer affected by it.
/// </summary>
public sealed class SetGraphicStylePropertiesCommand : ICadCommand
{
    private readonly StyleId _styleId;
    private readonly CadColor? _strokeColor;
    private readonly CadLineWeight? _lineWeight;
    private readonly LineTypeId? _lineTypeId;
    private GraphicStyleState? _previousState;

    public string Name => "Set Graphic Style Properties";

    public SetGraphicStylePropertiesCommand(
        StyleId styleId,
        CadColor? strokeColor = null,
        CadLineWeight? lineWeight = null,
        LineTypeId? lineTypeId = null)
    {
        _styleId = styleId;
        _strokeColor = strokeColor;
        _lineWeight = lineWeight;
        _lineTypeId = lineTypeId;

        if (_lineTypeId is { Value: <= 0 })
            throw new ArgumentOutOfRangeException(nameof(lineTypeId));

        if (_strokeColor is null && _lineWeight is null && _lineTypeId is null)
            throw new ArgumentException("At least one graphic style property is required.");
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var style = GetStyle(document);
        _previousState ??= GraphicStyleState.From(style);
        Apply(style);
        return CreateChangeSet(document);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_previousState is not { } previousState)
            return CadDocumentChangeSet.Empty;

        Apply(GetStyle(document), previousState);
        return CreateChangeSet(document);
    }

    private CadGraphicStyle GetStyle(CadDocument document)
    {
        if (!document.TryGetStyle(_styleId, out var style) || style is not CadGraphicStyle graphicStyle)
            throw new InvalidOperationException($"Graphic style does not exist: {_styleId}");
        return graphicStyle;
    }

    private void Apply(CadGraphicStyle style)
    {
        if (_strokeColor is { } strokeColor)
            style.SetStrokeColor(strokeColor);
        if (_lineWeight is { } lineWeight)
            style.SetLineWeight(lineWeight);
        if (_lineTypeId is { } lineTypeId)
            style.SetLineType(lineTypeId);
    }

    private static void Apply(CadGraphicStyle style, GraphicStyleState state)
    {
        style.SetStrokeColor(state.StrokeColor);
        style.SetLineWeight(state.LineWeight);
        style.SetLineType(state.LineTypeId);
    }

    private CadDocumentChangeSet CreateChangeSet(CadDocument document)
    {
        var ids = document.Entities.Values
            .Where(entity => !entity.IsErased &&
                (GetGraphicStyleId(entity) == _styleId ||
                 document.GetLayer(entity.LayerId).DefaultGraphicStyleId == _styleId))
            .Select(entity => entity.Id)
            .ToArray();

        return CadDocumentChangeSet
            .ForEntities(ids, CadEntityChangeKind.Appearance)
            .WithDocumentStructureChanged();
    }

    private static StyleId? GetGraphicStyleId(CadEntity entity) => entity switch
    {
        CadLine value => value.GraphicStyleId,
        CadCircle value => value.GraphicStyleId,
        CadArc value => value.GraphicStyleId,
        CadEllipse value => value.GraphicStyleId,
        CadEllipseArc value => value.GraphicStyleId,
        CadRectangle value => value.GraphicStyleId,
        CadPolyline value => value.GraphicStyleId,
        CadSpline value => value.GraphicStyleId,
        CadCompositePath value => value.GraphicStyleId,
        CadText value => value.GraphicStyleId,
        CadShapeText value => value.GraphicStyleId,
        CadBlockReference value => value.GraphicStyleId,
        _ => null
    };

    private readonly record struct GraphicStyleState(
        CadColor StrokeColor,
        CadLineWeight LineWeight,
        LineTypeId LineTypeId)
    {
        public static GraphicStyleState From(CadGraphicStyle style) => new(
            style.StrokeColor,
            style.LineWeight,
            style.LineTypeId);
    }
}
