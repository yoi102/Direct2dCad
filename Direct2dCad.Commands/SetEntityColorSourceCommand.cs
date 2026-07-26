using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.Commands;

public sealed class SetEntityColorSourceCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly CadColorSource _colorSource;
    private readonly Dictionary<EntityId, EntityColorLayerState> _previousStates = [];

    public string Name => "Set Entity Color Source";

    public SetEntityColorSourceCommand(IEnumerable<EntityId> entityIds, CadColorSource colorSource)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        if (!Enum.IsDefined(colorSource))
            throw new ArgumentOutOfRangeException(nameof(colorSource));
        _colorSource = colorSource;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadCommandEntityAccess.EnsureEditable(document, _entityIds);
        _previousStates.Clear();

        var entities = _entityIds
            .Select(document.GetEntity)
            .ToArray();
        foreach (var entity in entities)
            EnsureSupportsGraphicStyle(entity);

        foreach (var entity in entities)
        {
            _previousStates[entity.Id] = new EntityColorLayerState(
                entity.ColorSource,
                GetGraphicStyleId(entity));

            if (_colorSource == CadColorSource.Explicit && GetGraphicStyleId(entity) is null)
                SetGraphicStyleId(entity, CreateEntityGraphicStyleFromLayer(document, entity));

            entity.SetColorSource(_colorSource);
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
            entity.SetColorSource(state.ColorSource);
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
            case CadEllipseArc ellipseArc:
                ellipseArc.SetGraphicStyleInternal(styleId);
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
            case CadCompositePath path:
                path.SetGraphicStyleInternal(styleId);
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

    private static void EnsureSupportsGraphicStyle(CadEntity entity)
    {
        if (CadEntityCapabilities.SupportsGraphicStyle(entity))
            return;

        throw new NotSupportedException($"Entity type has no graphic style: {entity.GetType().Name}");
    }

    private readonly record struct EntityColorLayerState(CadColorSource ColorSource, StyleId? GraphicStyleId);
}

public sealed class SetEntityUseLayerColorCommand : ICadCommand
{
    private readonly SetEntityColorSourceCommand _inner;

    public SetEntityUseLayerColorCommand(IEnumerable<EntityId> entityIds, bool useLayerColor)
    {
        _inner = new SetEntityColorSourceCommand(
            entityIds,
            useLayerColor ? CadColorSource.ByLayer : CadColorSource.Explicit);
    }

    public string Name => _inner.Name;
    public CadDocumentChangeSet Execute(CadDocument document) => _inner.Execute(document);
    public CadDocumentChangeSet Undo(CadDocument document) => _inner.Undo(document);
}
