using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.Commands;

public sealed class SetEntityUseLayerColorCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly bool _useLayerColor;
    private readonly Dictionary<EntityId, EntityColorLayerState> _previousStates = [];

    public string Name => "Set Entity Color By Layer";

    public SetEntityUseLayerColorCommand(IEnumerable<EntityId> entityIds, bool useLayerColor)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _useLayerColor = useLayerColor;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _previousStates.Clear();

        foreach (var entityId in _entityIds)
        {
            var entity = document.GetEntity(entityId);
            _previousStates[entityId] = new EntityColorLayerState(
                entity.UseLayerColor,
                GetGraphicStyleId(entity));

            if (!_useLayerColor && GetGraphicStyleId(entity) is null)
                SetGraphicStyleId(entity, CreateEntityGraphicStyleFromLayer(document, entity));

            entity.SetUseLayerColor(_useLayerColor);
        }

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, state) in _previousStates)
        {
            var entity = document.GetEntity(entityId);
            SetGraphicStyleId(entity, state.GraphicStyleId);
            entity.SetUseLayerColor(state.UseLayerColor);
        }

        return CadDocumentChangeSet.ForEntities(_previousStates.Keys, CadEntityChangeKind.Appearance);
    }

    private static StyleId CreateEntityGraphicStyleFromLayer(CadDocument document, CadEntity entity)
    {
        var layer = document.GetLayer(entity.LayerId);
        var color = ResolveLayerStrokeColor(document, layer);

        return document.CreateGraphicStyle(
            $"Entity stroke {color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}",
            color,
            CadLineWeight.ByLayer,
            LineTypeId.Continuous);
    }

    private static CadColor ResolveLayerStrokeColor(CadDocument document, CadLayer layer)
    {
        if (layer.DefaultGraphicStyleId is { } styleId &&
            document.TryGetStyle(styleId, out var style) &&
            style is CadGraphicStyle graphic)
        {
            return graphic.StrokeColor;
        }

        return layer.Color;
    }

    private static StyleId? GetGraphicStyleId(CadEntity entity)
    {
        return entity switch
        {
            CadLine line => line.GraphicStyleId,
            CadCircle circle => circle.GraphicStyleId,
            CadEllipse ellipse => ellipse.GraphicStyleId,
            CadRectangle rectangle => rectangle.GraphicStyleId,
            CadArc arc => arc.GraphicStyleId,
            CadPolyline polyline => polyline.GraphicStyleId,
            CadSpline spline => spline.GraphicStyleId,
            CadText text => text.GraphicStyleId,
            CadShapeText shapeText => shapeText.GraphicStyleId,
            CadBlockReference blockReference => blockReference.GraphicStyleId,
            _ => null
        };
    }

    private static void SetGraphicStyleId(CadEntity entity, StyleId? styleId)
    {
        switch (entity)
        {
            case CadLine line:
                line.SetGraphicStyleInternal(styleId);
                break;
            case CadCircle circle:
                circle.SetGraphicStyleInternal(styleId);
                break;
            case CadEllipse ellipse:
                ellipse.SetGraphicStyleInternal(styleId);
                break;
            case CadRectangle rectangle:
                rectangle.SetGraphicStyleInternal(styleId);
                break;
            case CadArc arc:
                arc.SetGraphicStyleInternal(styleId);
                break;
            case CadPolyline polyline:
                polyline.SetGraphicStyleInternal(styleId);
                break;
            case CadSpline spline:
                spline.SetGraphicStyleInternal(styleId);
                break;
            case CadText text:
                text.SetGraphicStyleInternal(styleId);
                break;
            case CadShapeText shapeText:
                shapeText.SetGraphicStyleInternal(styleId);
                break;
            case CadBlockReference blockReference:
                blockReference.SetGraphicStyleInternal(styleId);
                break;
            default:
                throw new NotSupportedException($"Entity type has no graphic style: {entity.GetType().Name}");
        }
    }

    private readonly record struct EntityColorLayerState(bool UseLayerColor, StyleId? GraphicStyleId);
}
