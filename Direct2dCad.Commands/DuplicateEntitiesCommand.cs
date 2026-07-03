using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class DuplicateEntitiesCommand : ICadCommand
{
    private readonly EntityId[] _sourceEntityIds;
    private readonly CadVectorD _delta;
    private readonly List<EntityId> _createdEntityIds = [];

    public string Name => "Duplicate Entities";
    public IReadOnlyList<EntityId> CreatedEntityIds => _createdEntityIds;

    public DuplicateEntitiesCommand(IEnumerable<EntityId> sourceEntityIds, CadVectorD delta)
    {
        _sourceEntityIds = sourceEntityIds?.Distinct().ToArray() ?? throw new ArgumentNullException(nameof(sourceEntityIds));
        _delta = delta;

        if (_sourceEntityIds.Length == 0)
            throw new ArgumentException("At least one entity is required.", nameof(sourceEntityIds));
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_createdEntityIds.Count > 0)
        {
            var restoredIds = new List<EntityId>();
            foreach (var entityId in _createdEntityIds)
            {
                if (!document.TryGetEntity(entityId, out var entity) || entity is null)
                    continue;

                entity.Restore();
                restoredIds.Add(entity.Id);
            }

            return restoredIds.Count == 0
                ? CadDocumentChangeSet.Empty
                : CadDocumentChangeSet.ForEntities(
                    restoredIds,
                    CadEntityChangeKind.Created | CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance | CadEntityChangeKind.Visibility);
        }

        foreach (var sourceEntityId in _sourceEntityIds)
        {
            if (!document.TryGetEntity(sourceEntityId, out var source) || source is null || source.IsErased)
                continue;

            if (TryDuplicate(document, source, _delta, out var created) && created is not null)
                _createdEntityIds.Add(created.Id);
        }

        return _createdEntityIds.Count == 0
            ? CadDocumentChangeSet.Empty
            : CadDocumentChangeSet.ForEntities(
                _createdEntityIds,
                CadEntityChangeKind.Created | CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var erasedIds = new List<EntityId>();
        foreach (var entityId in _createdEntityIds)
        {
            if (!document.TryGetEntity(entityId, out var entity) || entity is null)
                continue;

            entity.Erase();
            erasedIds.Add(entity.Id);
        }

        return erasedIds.Count == 0
            ? CadDocumentChangeSet.Empty
            : CadDocumentChangeSet.ForEntities(erasedIds, CadEntityChangeKind.Deleted | CadEntityChangeKind.Visibility);
    }

    private static bool TryDuplicate(
        CadDocument document,
        CadEntity source,
        CadVectorD delta,
        out CadEntity? created)
    {
        created = source switch
        {
            CadLine line => document.AddLine(
                line.Start + delta,
                line.End + delta,
                line.LayerId,
                line.GraphicStyleId,
                line.Name),
            CadCircle circle => document.AddCircle(
                circle.Center + delta,
                circle.Radius,
                circle.LayerId,
                circle.GraphicStyleId,
                circle.FillStyleId,
                circle.Name),
            CadEllipse ellipse => document.AddEllipse(
                ellipse.Center + delta,
                ellipse.RadiusX,
                ellipse.RadiusY,
                ellipse.LayerId,
                ellipse.GraphicStyleId,
                ellipse.FillStyleId,
                ellipse.Name),
            CadRectangle rectangle => document.AddRectangle(
                rectangle.Bounds.Translate(delta),
                rectangle.CornerRadiusX,
                rectangle.CornerRadiusY,
                rectangle.LayerId,
                rectangle.GraphicStyleId,
                rectangle.FillStyleId,
                rectangle.Name),
            CadArc arc => document.AddArc(
                arc.Center + delta,
                arc.Radius,
                arc.StartAngleRadians,
                arc.SweepAngleRadians,
                arc.LayerId,
                arc.GraphicStyleId,
                arc.Name),
            CadPolyline polyline => document.AddPolyline(
                polyline.Points.Select(x => x + delta),
                polyline.Closed,
                polyline.LayerId,
                polyline.GraphicStyleId,
                polyline.FillStyleId,
                polyline.Name),
            CadSpline spline => document.AddSpline(
                spline.FitPoints.Select(x => x + delta),
                spline.Closed,
                spline.LayerId,
                spline.GraphicStyleId,
                spline.Name),
            CadText text => document.AddText(
                text.Text,
                text.Position + delta,
                text.Height,
                text.RotationRadians,
                text.LayerId,
                text.GraphicStyleId,
                text.TextStyleId,
                text.Name,
                text.IsInverted,
                text.InvertedMarginFactor),
            CadShapeText shapeText => document.AddShapeText(
                shapeText.Text,
                shapeText.Position + delta,
                shapeText.Height,
                shapeText.RotationRadians,
                shapeText.WidthFactor,
                shapeText.CharacterSpacingFactor,
                shapeText.ObliqueAngleRadians,
                shapeText.LayerId,
                shapeText.GraphicStyleId,
                shapeText.Name,
                shapeText.IsInverted,
                shapeText.InvertedMarginFactor,
                shapeText.ShapeFontId),
            _ => null
        };

        if (created is null)
            return false;

        created.SetLineWeightState(source.LineWeight, source.UseLayerLineWeight);
        created.SetUseLayerColor(source.UseLayerColor);
        created.SetVisible(source.IsVisible);
        created.SetZIndex(source.ZIndex);
        return true;
    }
}
