using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Handles;

public sealed class CadHandleSceneBuilder
{
    public IReadOnlyList<CadHandleItem> BuildSelectionHandles(
        CadDocument document,
        IEnumerable<EntityId> selectedEntityIds,
        CadHandleSceneBuildOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedEntityIds);

        options ??= CadHandleSceneBuildOptions.Default;

        var items = new List<CadHandleItem>();
        foreach (var entityId in selectedEntityIds)
        {
            if (!TryGetSelectableEntity(document, entityId, out var entity))
                continue;

            if (options.IncludeSelectionOutline)
            {
                items.Add(new CadSelectionEntityReference(
                    entity.Id,
                    CadVectorD.Zero,
                    options.SelectionOutlineStyle));
            }

            if (options.IncludeGripHandles &&
                (!entity.IsLocked || options.IncludeLockedEntityGripHandles))
            {
                AddEntityGripHandles(items, entity, options.GripStyle);
            }
        }

        return items;
    }

    public static bool SupportsCenterGrip(CadEntity entity)
    {
        return entity is CadLine or CadCircle or CadEllipse or CadRectangle or CadArc or CadPolyline or CadSpline or CadText or CadShapeText or CadBlockReference;
    }

    private static bool TryGetSelectableEntity(
        CadDocument document,
        EntityId entityId,
        out CadEntity entity)
    {
        entity = default!;

        if (!document.TryGetEntity(entityId, out var found) ||
            found is null ||
            found.IsErased ||
            !found.IsVisible ||
            !document.TryGetLayer(found.LayerId, out var layer) ||
            layer is null ||
            !layer.IsVisible ||
            layer.IsFrozen)
        {
            return false;
        }

        entity = found;
        return true;
    }

    private static void AddEntityGripHandles(
        List<CadHandleItem> items,
        CadEntity entity,
        CadHandleStyle gripStyle)
    {
        switch (entity)
        {
            case CadLine line:
                AddGrip(items, entity.Id, line.Start, CadHandleType.Vertex, gripStyle);
                AddGrip(items, entity.Id, line.End, CadHandleType.Vertex, gripStyle);
                AddGrip(items, entity.Id, Midpoint(line.Start, line.End), CadHandleType.Center, gripStyle);
                break;

            case CadCircle circle:
                AddGrip(items, entity.Id, circle.Center, CadHandleType.Center, gripStyle);
                AddGrip(items, entity.Id, new CadPointD(circle.Center.X + circle.Radius, circle.Center.Y), CadHandleType.Radius, gripStyle);
                AddGrip(items, entity.Id, new CadPointD(circle.Center.X, circle.Center.Y + circle.Radius), CadHandleType.Radius, gripStyle);
                AddGrip(items, entity.Id, new CadPointD(circle.Center.X - circle.Radius, circle.Center.Y), CadHandleType.Radius, gripStyle);
                AddGrip(items, entity.Id, new CadPointD(circle.Center.X, circle.Center.Y - circle.Radius), CadHandleType.Radius, gripStyle);
                break;

            case CadEllipse ellipse:
                AddGrip(items, entity.Id, ellipse.Center, CadHandleType.Center, gripStyle);
                AddGrip(items, entity.Id, new CadPointD(ellipse.Center.X + ellipse.RadiusX, ellipse.Center.Y), CadHandleType.Radius, gripStyle);
                AddGrip(items, entity.Id, new CadPointD(ellipse.Center.X, ellipse.Center.Y + ellipse.RadiusY), CadHandleType.Radius, gripStyle);
                AddGrip(items, entity.Id, new CadPointD(ellipse.Center.X - ellipse.RadiusX, ellipse.Center.Y), CadHandleType.Radius, gripStyle);
                AddGrip(items, entity.Id, new CadPointD(ellipse.Center.X, ellipse.Center.Y - ellipse.RadiusY), CadHandleType.Radius, gripStyle);
                break;

            case CadArc arc:
                AddGrip(items, entity.Id, arc.Center, CadHandleType.Center, gripStyle);
                AddGrip(items, entity.Id, arc.StartPoint, CadHandleType.Vertex, gripStyle);
                AddGrip(items, entity.Id, arc.EndPoint, CadHandleType.Vertex, gripStyle);
                AddGrip(items, entity.Id, arc.GetPointAtAngle(arc.StartAngleRadians + arc.SweepAngleRadians * 0.5), CadHandleType.Radius, gripStyle);
                break;

            case CadRectangle:
                AddBoundsGripHandles(items, entity.Id, entity.Bounds, gripStyle);
                break;

            case CadPolyline polyline:
                AddPolylineGripHandles(items, polyline, gripStyle);
                break;

            case CadSpline spline:
                AddSplineGripHandles(items, spline, gripStyle);
                break;

            case CadText:
                AddBoundsGripHandles(items, entity.Id, entity.Bounds, gripStyle);
                break;

            case CadShapeText:
                AddBoundsGripHandles(items, entity.Id, entity.Bounds, gripStyle);
                break;

            default:
                if (SupportsCenterGrip(entity) && !entity.Bounds.IsEmpty)
                    AddGrip(items, entity.Id, entity.Bounds.Center, CadHandleType.Center, gripStyle);
                break;
        }
    }

    private static void AddPolylineGripHandles(
        List<CadHandleItem> items,
        CadPolyline polyline,
        CadHandleStyle gripStyle)
    {
        foreach (var point in polyline.Points)
            AddGrip(items, polyline.Id, point, CadHandleType.Vertex, gripStyle);

        if (!polyline.Bounds.IsEmpty)
            AddGrip(items, polyline.Id, polyline.Bounds.Center, CadHandleType.Center, gripStyle);
    }

    private static void AddSplineGripHandles(
        List<CadHandleItem> items,
        CadSpline spline,
        CadHandleStyle gripStyle)
    {
        foreach (var point in spline.FitPoints)
            AddGrip(items, spline.Id, point, CadHandleType.Vertex, gripStyle);

        if (!spline.Bounds.IsEmpty)
            AddGrip(items, spline.Id, spline.Bounds.Center, CadHandleType.Center, gripStyle);
    }

    private static void AddBoundsGripHandles(
        List<CadHandleItem> items,
        EntityId entityId,
        CadRectD bounds,
        CadHandleStyle gripStyle)
    {
        if (bounds.IsEmpty)
            return;

        AddGrip(items, entityId, new CadPointD(bounds.MinX, bounds.MinY), CadHandleType.BoundsCorner, gripStyle);
        AddGrip(items, entityId, new CadPointD(bounds.MaxX, bounds.MinY), CadHandleType.BoundsCorner, gripStyle);
        AddGrip(items, entityId, new CadPointD(bounds.MaxX, bounds.MaxY), CadHandleType.BoundsCorner, gripStyle);
        AddGrip(items, entityId, new CadPointD(bounds.MinX, bounds.MaxY), CadHandleType.BoundsCorner, gripStyle);
        AddGrip(items, entityId, bounds.Center, CadHandleType.Center, gripStyle);
    }

    private static void AddGrip(
        List<CadHandleItem> items,
        EntityId entityId,
        CadPointD position,
        CadHandleType type,
        CadHandleStyle gripStyle)
    {
        items.Add(new CadGripHandle(entityId, position, type, CreateGripStyle(type, gripStyle)));
    }

    private static CadHandleStyle CreateGripStyle(CadHandleType type, CadHandleStyle gripStyle)
    {
        return type switch
        {
            CadHandleType.Center => gripStyle with { Shape = CadHandleShape.Circle },
            CadHandleType.Radius => gripStyle with { Shape = CadHandleShape.Diamond },
            _ => gripStyle
        };
    }

    private static CadPointD Midpoint(CadPointD start, CadPointD end)
    {
        return new CadPointD(
            (start.X + end.X) * 0.5,
            (start.Y + end.Y) * 0.5);
    }
}
