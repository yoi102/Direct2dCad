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

        selectedEntityIds.TryGetNonEnumeratedCount(out var selectedEntityCount);
        var items = new List<CadHandleItem>(selectedEntityCount);
        var selectedEntities = new List<CadEntity>(selectedEntityCount);
        BuildSelectionHandlesCore(
            document,
            selectedEntityIds,
            options,
            items,
            selectedEntities);
        return items;
    }

    public IReadOnlyList<CadHandleItem> BuildSelectionHandles(
        CadDocument document,
        IEnumerable<EntityId> selectedEntityIds,
        CadHandleSceneBuildBuffer buffer,
        CadHandleSceneBuildOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedEntityIds);
        ArgumentNullException.ThrowIfNull(buffer);

        options ??= CadHandleSceneBuildOptions.Default;
        buffer.Items.Clear();
        buffer.SelectedEntities.Clear();
        if (selectedEntityIds.TryGetNonEnumeratedCount(out var selectedEntityCount))
        {
            if (buffer.Items.Capacity < selectedEntityCount)
                buffer.Items.Capacity = selectedEntityCount;
            if (buffer.SelectedEntities.Capacity < selectedEntityCount)
                buffer.SelectedEntities.Capacity = selectedEntityCount;
        }

        BuildSelectionHandlesCore(
            document,
            selectedEntityIds,
            options,
            buffer.Items,
            buffer.SelectedEntities);
        return buffer.Items;
    }

    private static void BuildSelectionHandlesCore(
        CadDocument document,
        IEnumerable<EntityId> selectedEntityIds,
        CadHandleSceneBuildOptions options,
        List<CadHandleItem> items,
        List<CadEntity> selectedEntities)
    {
        foreach (var entityId in selectedEntityIds)
        {
            if (!TryGetSelectableEntity(document, entityId, out var entity))
                continue;

            selectedEntities.Add(entity);
        }

        var includeIndividualGrips =
            options.IncludeGripHandles &&
            selectedEntities.Count <= Math.Max(0, options.MaximumIndividualGripEntityCount);

        foreach (var entity in selectedEntities)
        {
            if (options.IncludeSelectionOutline)
            {
                items.Add(new CadSelectionEntityReference(
                    entity.Id,
                    entity.Bounds,
                    CadVectorD.Zero,
                    options.SelectionOutlineStyle));
            }

            if (includeIndividualGrips &&
                (!entity.IsLocked || options.IncludeLockedEntityGripHandles))
            {
                AddEntityGripHandles(items, document, entity, options.GripStyle, options.RotationHandleOffset);
            }
        }

        if (options.IncludeGripHandles &&
            !includeIndividualGrips &&
            options.IncludeAggregateMoveGripForLargeSelection)
        {
            AddAggregateMoveGrip(items, selectedEntities, options);
        }
    }

    private static void AddAggregateMoveGrip(
        List<CadHandleItem> items,
        IReadOnlyList<CadEntity> selectedEntities,
        CadHandleSceneBuildOptions options)
    {
        CadEntity? representative = null;
        var bounds = CadRectD.Empty;

        foreach (var entity in selectedEntities)
        {
            if ((entity.IsLocked && !options.IncludeLockedEntityGripHandles) ||
                !SupportsCenterGrip(entity))
            {
                continue;
            }

            representative ??= entity;
            bounds = bounds.Union(entity.Bounds);
        }

        if (representative is null || bounds.IsEmpty)
            return;

        AddGrip(
            items,
            representative.Id,
            bounds.Center,
            CadHandleType.Center,
            options.GripStyle);
    }

    public static bool SupportsCenterGrip(CadEntity entity)
    {
        return entity is CadLine or CadCircle or CadEllipse or CadEllipseArc or CadRectangle or CadArc or CadPolyline or CadSpline or CadText or CadShapeText or CadImage or CadOleObject or CadBlockReference;
    }

    public IReadOnlyList<CadHandleItem> BuildBlockReferenceGripHandles(
        CadDocument document,
        EntityId entityId,
        BlockId definitionBlockId,
        CadPointD position,
        double rotationRadians,
        double scaleX,
        double scaleY,
        CadHandleSceneBuildOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= CadHandleSceneBuildOptions.Default;

        var items = new List<CadHandleItem>();
        if (!document.TryGetBlock(definitionBlockId, out var definition) || definition is null)
            return items;

        AddBlockReferenceGripHandles(
            items,
            entityId,
            definition,
            document.GetBlockBounds(definitionBlockId),
            position,
            rotationRadians,
            scaleX,
            scaleY,
            options.GripStyle,
            options.RotationHandleOffset);
        return items;
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
        CadDocument document,
        CadEntity entity,
        CadHandleStyle gripStyle,
        double rotationHandleOffset)
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

            case CadEllipseArc ellipseArc:
                AddGrip(items, entity.Id, ellipseArc.Center, CadHandleType.Center, gripStyle);
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

            case CadImage image:
                AddImageGripHandles(
                    items,
                    image.Id,
                    image.FrameBounds,
                    image.RotationRadians,
                    gripStyle,
                    rotationHandleOffset);
                break;

            case CadOleObject:
                AddBoundsAndSideGripHandles(items, entity.Id, entity.Bounds, gripStyle);
                break;

            case CadBlockReference blockReference:
                AddBlockReferenceGripHandles(
                    items,
                    document,
                    blockReference,
                    gripStyle,
                    rotationHandleOffset);
                break;

            default:
                if (SupportsCenterGrip(entity) && !entity.Bounds.IsEmpty)
                    AddGrip(items, entity.Id, entity.Bounds.Center, CadHandleType.Center, gripStyle);
                break;
        }
    }

    private static void AddBlockReferenceGripHandles(
        List<CadHandleItem> items,
        CadDocument document,
        CadBlockReference reference,
        CadHandleStyle gripStyle,
        double rotationHandleOffset)
    {
        if (!document.TryGetBlock(reference.DefinitionBlockId, out var definition) || definition is null)
            return;

        AddBlockReferenceGripHandles(
            items,
            reference.Id,
            definition,
            document.GetBlockBounds(definition.Id),
            reference.Position,
            reference.RotationRadians,
            reference.ScaleX,
            reference.ScaleY,
            gripStyle,
            rotationHandleOffset);
    }

    private static void AddBlockReferenceGripHandles(
        List<CadHandleItem> items,
        EntityId entityId,
        CadBlockDefinition definition,
        CadRectD localBounds,
        CadPointD position,
        double rotationRadians,
        double scaleX,
        double scaleY,
        CadHandleStyle gripStyle,
        double rotationHandleOffset)
    {
        var transform = CadBlockTransform.Create(
            definition,
            position,
            rotationRadians,
            scaleX,
            scaleY);
        AddGrip(items, entityId, position, CadHandleType.Center, gripStyle);
        if (localBounds.IsEmpty)
            return;

        var corners = new[]
        {
            new CadPointD(localBounds.MinX, localBounds.MinY),
            new CadPointD(localBounds.MaxX, localBounds.MinY),
            new CadPointD(localBounds.MaxX, localBounds.MaxY),
            new CadPointD(localBounds.MinX, localBounds.MaxY)
        };
        foreach (var corner in corners)
        {
            AddGrip(
                items,
                entityId,
                transform.TransformPoint(corner),
                CadHandleType.BoundsCorner,
                gripStyle);
        }

        var localTop = new CadPointD(localBounds.Center.X, localBounds.MaxY);
        var guideStart = transform.TransformPoint(localTop);
        var direction = guideStart - position;
        var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        var offset = double.IsFinite(rotationHandleOffset) && rotationHandleOffset > 0
            ? rotationHandleOffset
            : Math.Max(localBounds.Transform(transform).Height * 0.2, 1.0);
        var unit = length > double.Epsilon
            ? new CadVectorD(direction.X / length, direction.Y / length)
            : new CadVectorD(-Math.Sin(rotationRadians), Math.Cos(rotationRadians));
        var rotationPosition = guideStart + unit * offset;
        items.Add(new CadRotationHandleGuide(guideStart, rotationPosition, gripStyle));
        AddGrip(items, entityId, rotationPosition, CadHandleType.Rotation, gripStyle);
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

    public IReadOnlyList<CadHandleItem> BuildImageGripHandles(
        EntityId entityId,
        CadRectD frameBounds,
        double rotationRadians,
        CadHandleSceneBuildOptions? options = null)
    {
        options ??= CadHandleSceneBuildOptions.Default;
        var items = new List<CadHandleItem>();
        AddImageGripHandles(
            items,
            entityId,
            frameBounds,
            rotationRadians,
            options.GripStyle,
            options.RotationHandleOffset);
        return items;
    }

    private static void AddImageGripHandles(
        List<CadHandleItem> items,
        EntityId entityId,
        CadRectD bounds,
        double rotationRadians,
        CadHandleStyle gripStyle,
        double rotationHandleOffset)
    {
        if (bounds.IsEmpty)
            return;

        CadPointD ToWorld(CadPointD point) => RotateAround(point, bounds.Center, rotationRadians);

        AddGrip(items, entityId, ToWorld(new CadPointD(bounds.MinX, bounds.MinY)), CadHandleType.BoundsCorner, gripStyle);
        AddGrip(items, entityId, ToWorld(new CadPointD(bounds.MaxX, bounds.MinY)), CadHandleType.BoundsCorner, gripStyle);
        AddGrip(items, entityId, ToWorld(new CadPointD(bounds.MaxX, bounds.MaxY)), CadHandleType.BoundsCorner, gripStyle);
        AddGrip(items, entityId, ToWorld(new CadPointD(bounds.MinX, bounds.MaxY)), CadHandleType.BoundsCorner, gripStyle);
        AddGrip(items, entityId, ToWorld(new CadPointD(bounds.Center.X, bounds.MinY)), CadHandleType.BoundsSide, gripStyle);
        AddGrip(items, entityId, ToWorld(new CadPointD(bounds.MaxX, bounds.Center.Y)), CadHandleType.BoundsSide, gripStyle);
        AddGrip(items, entityId, ToWorld(new CadPointD(bounds.Center.X, bounds.MaxY)), CadHandleType.BoundsSide, gripStyle);
        AddGrip(items, entityId, ToWorld(new CadPointD(bounds.MinX, bounds.Center.Y)), CadHandleType.BoundsSide, gripStyle);
        AddGrip(items, entityId, bounds.Center, CadHandleType.Center, gripStyle);

        var rotationOffset = double.IsFinite(rotationHandleOffset) && rotationHandleOffset > 0
            ? rotationHandleOffset
            : Math.Max(bounds.Height * 0.2, Math.Min(bounds.Width, bounds.Height) * 0.15);
        var rotationGuideStart = ToWorld(new CadPointD(bounds.Center.X, bounds.MaxY));
        var rotationHandlePosition = ToWorld(new CadPointD(bounds.Center.X, bounds.MaxY + rotationOffset));
        items.Add(new CadRotationHandleGuide(rotationGuideStart, rotationHandlePosition, gripStyle));
        AddGrip(
            items,
            entityId,
            rotationHandlePosition,
            CadHandleType.Rotation,
            gripStyle);
    }

    private static CadPointD RotateAround(CadPointD point, CadPointD center, double rotationRadians)
    {
        if (Math.Abs(rotationRadians) <= 1e-12)
            return point;

        var cos = Math.Cos(rotationRadians);
        var sin = Math.Sin(rotationRadians);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        return new CadPointD(
            center.X + dx * cos - dy * sin,
            center.Y + dx * sin + dy * cos);
    }

    private static void AddBoundsAndSideGripHandles(
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
        AddGrip(items, entityId, new CadPointD(bounds.Center.X, bounds.MinY), CadHandleType.BoundsSide, gripStyle);
        AddGrip(items, entityId, new CadPointD(bounds.MaxX, bounds.Center.Y), CadHandleType.BoundsSide, gripStyle);
        AddGrip(items, entityId, new CadPointD(bounds.Center.X, bounds.MaxY), CadHandleType.BoundsSide, gripStyle);
        AddGrip(items, entityId, new CadPointD(bounds.MinX, bounds.Center.Y), CadHandleType.BoundsSide, gripStyle);
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
            CadHandleType.Rotation => gripStyle with { Shape = CadHandleShape.Diamond },
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
