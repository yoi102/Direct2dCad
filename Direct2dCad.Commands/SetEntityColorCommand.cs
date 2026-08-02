using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.Commands;

public sealed class SetEntityColorCommand : ICadCommand
{
    private readonly EntityId[] _entityIds;
    private readonly CadColor _color;
    private readonly Dictionary<EntityId, StyleId?> _previousGraphicStyles = [];
    private CadGraphicStyle? _createdGraphicStyle;

    public string Name => "Set Entity Color";

    public SetEntityColorCommand(IEnumerable<EntityId> entityIds, CadColor color)
    {
        _entityIds = entityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(entityIds));
        _color = color;

        if (_entityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(entityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        CadCommandEntityAccess.EnsureEditable(document, _entityIds);
        _previousGraphicStyles.Clear();

        var entities = _entityIds
            .Select(document.GetEntity)
            .ToArray();
        foreach (var entity in entities)
            EnsureSupportsGraphicStyle(entity);

        EnsureGraphicStyle(document);

        foreach (var entity in entities)
        {
            _previousGraphicStyles[entity.Id] = GetGraphicStyleId(entity);
            SetGraphicStyleId(entity, _createdGraphicStyle!.Id);
        }

        return CreateChangeSet(_entityIds);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var (entityId, styleId) in _previousGraphicStyles)
            SetGraphicStyleId(document.GetEntity(entityId), styleId);

        if (_createdGraphicStyle is not null &&
            document.Styles.ContainsKey(_createdGraphicStyle.Id) &&
            !document.Entities.Values.Any(entity => GetGraphicStyleId(entity) == _createdGraphicStyle.Id))
        {
            document.RemoveStyleCore(_createdGraphicStyle.Id);
        }

        return CreateChangeSet(_previousGraphicStyles.Keys);
    }

    private void EnsureGraphicStyle(CadDocument document)
    {
        if (_createdGraphicStyle is null)
        {
            var styleId = document.CreateGraphicStyle(
                $"Color {_color.R},{_color.G},{_color.B},{_color.A}",
                _color,
                CadLineWeight.ByLayer,
                LineTypeId.Continuous);
            _createdGraphicStyle = (CadGraphicStyle)document.Styles[styleId];
        }
        else if (!document.TryGetStyle(_createdGraphicStyle.Id, out _))
        {
            document.AddStyleCore(_createdGraphicStyle);
        }
    }

    private CadDocumentChangeSet CreateChangeSet(IEnumerable<EntityId> entityIds) =>
        CadDocumentChangeSet.ForEntities(entityIds, CadEntityChangeKind.Appearance)
            .WithDocumentStructureChanged();

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
}
