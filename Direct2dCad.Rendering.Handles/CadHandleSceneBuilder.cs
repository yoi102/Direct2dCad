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
                    CadHandleStyle.SelectionOutline));
            }

            if (options.IncludeGripHandles &&
                (!entity.IsLocked || options.IncludeLockedEntityGripHandles))
            {
                AddEntityGripHandles(items, entity);
            }
        }

        return items;
    }

    public static bool SupportsCenterGrip(CadEntity entity)
    {
        return entity is CadLine or CadCircle or CadArc or CadPolyline or CadText or CadBlockReference;
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

    private static void AddEntityGripHandles(List<CadHandleItem> items, CadEntity entity)
    {
        switch (entity)
        {
            case CadLine line:
                AddGrip(items, entity.Id, line.Start, CadHandleType.Vertex);
                AddGrip(items, entity.Id, line.End, CadHandleType.Vertex);
                AddGrip(items, entity.Id, Midpoint(line.Start, line.End), CadHandleType.Center);
                break;

            case CadCircle circle:
                AddGrip(items, entity.Id, circle.Center, CadHandleType.Center);
                AddGrip(items, entity.Id, new CadPointD(circle.Center.X + circle.Radius, circle.Center.Y), CadHandleType.Radius);
                AddGrip(items, entity.Id, new CadPointD(circle.Center.X, circle.Center.Y + circle.Radius), CadHandleType.Radius);
                AddGrip(items, entity.Id, new CadPointD(circle.Center.X - circle.Radius, circle.Center.Y), CadHandleType.Radius);
                AddGrip(items, entity.Id, new CadPointD(circle.Center.X, circle.Center.Y - circle.Radius), CadHandleType.Radius);
                break;

            case CadText:
                AddBoundsGripHandles(items, entity.Id, entity.Bounds);
                break;

            default:
                if (SupportsCenterGrip(entity) && !entity.Bounds.IsEmpty)
                    AddGrip(items, entity.Id, entity.Bounds.Center, CadHandleType.Center);
                break;
        }
    }

    private static void AddBoundsGripHandles(List<CadHandleItem> items, EntityId entityId, CadRectD bounds)
    {
        if (bounds.IsEmpty)
            return;

        AddGrip(items, entityId, new CadPointD(bounds.MinX, bounds.MinY), CadHandleType.BoundsCorner);
        AddGrip(items, entityId, new CadPointD(bounds.MaxX, bounds.MinY), CadHandleType.BoundsCorner);
        AddGrip(items, entityId, new CadPointD(bounds.MaxX, bounds.MaxY), CadHandleType.BoundsCorner);
        AddGrip(items, entityId, new CadPointD(bounds.MinX, bounds.MaxY), CadHandleType.BoundsCorner);
        AddGrip(items, entityId, bounds.Center, CadHandleType.Center);
    }

    private static void AddGrip(List<CadHandleItem> items, EntityId entityId, CadPointD position, CadHandleType type)
    {
        items.Add(new CadGripHandle(entityId, position, type, CreateGripStyle(type)));
    }

    private static CadHandleStyle CreateGripStyle(CadHandleType type)
    {
        return type switch
        {
            CadHandleType.Center => CadHandleStyle.Grip with { Shape = CadHandleShape.Circle },
            CadHandleType.Radius => CadHandleStyle.Grip with { Shape = CadHandleShape.Diamond },
            _ => CadHandleStyle.Grip
        };
    }

    private static CadPointD Midpoint(CadPointD start, CadPointD end)
    {
        return new CadPointD(
            (start.X + end.X) * 0.5,
            (start.Y + end.Y) * 0.5);
    }
}
