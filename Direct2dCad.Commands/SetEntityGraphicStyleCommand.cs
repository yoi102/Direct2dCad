using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.Commands;

public sealed class SetEntityGraphicStyleCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly StyleId? _graphicStyleId;
    private readonly Dictionary<EntityId, StyleId?> _previousGraphicStyles = [];

    public string Name => "Set Entity Graphic Style";

    public SetEntityGraphicStyleCommand(IEnumerable<EntityId> entityIds, StyleId? graphicStyleId)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _graphicStyleId = graphicStyleId;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadCommandEntityAccess.EnsureEditable(document, _entityIds);
        _previousGraphicStyles.Clear();
        ValidateGraphicStyle(document);

        foreach (var entityId in _entityIds)
        {
            var entity = document.GetEntity(entityId);
            _previousGraphicStyles[entityId] = GetGraphicStyleId(entity);
            SetGraphicStyleId(entity, _graphicStyleId);
        }

        return CadDocumentChangeSet.ForEntities(_entityIds, CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, styleId) in _previousGraphicStyles)
            SetGraphicStyleId(document.GetEntity(entityId), styleId);

        return CadDocumentChangeSet.ForEntities(_previousGraphicStyles.Keys, CadEntityChangeKind.Appearance);
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

    private void ValidateGraphicStyle(CadDocument document)
    {
        if (_graphicStyleId is null)
            return;

        if (!document.TryGetStyle(_graphicStyleId.Value, out var style))
            throw new InvalidOperationException($"Style does not exist: {_graphicStyleId}");

        if (style is not CadGraphicStyle)
            throw new InvalidOperationException($"Style is not graphic style: {_graphicStyleId}");
    }
}
