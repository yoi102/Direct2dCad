using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

/// <summary>Detached geometry copies validate a complete scale before any live entity changes.</summary>
internal sealed class CadScaleGeometryPlan(CadEntity[] before, CadEntity[] after)
{
    public static CadScaleGeometryPlan Create(CadDocument document, EntityId[] ids, CadPointD pivot, double factor)
    {
        GuardPoint(pivot);
        var before = ids.Select(id => Copy(document.GetEntity(id))).ToArray();
        var after = before.Select(Copy).ToArray();
        for (var i = 0; i < after.Length; i++)
        {
            CadEntityTransform.UniformScale(after[i], pivot, factor);
            if (after[i] is CadBlockReference reference)
            {
                var bounds = before[i].Bounds;
                CadPointD Scale(CadPointD point) => pivot + (point - pivot) * factor;
                reference.SetResolvedBounds(CadRectD.FromLTRB(
                    Scale(new(bounds.MinX, bounds.MinY)).X, Scale(new(bounds.MinX, bounds.MinY)).Y,
                    Scale(new(bounds.MaxX, bounds.MaxY)).X, Scale(new(bounds.MaxX, bounds.MaxY)).Y));
            }
            GuardGeometry(after[i]);
        }
        return new(before, after);
    }

    public void Apply(CadDocument document, bool undo)
    {
        var target = undo ? before : after;
        var rollback = undo ? after : before;
        var entities = target.Select(e => document.GetEntity(e.Id)).ToArray();
        for (var i = 0; i < entities.Length; i++)
            if (entities[i].GetType() != target[i].GetType())
                throw new InvalidOperationException("The entity type changed after scale preparation.");
        var applied = 0;
        try
        {
            for (; applied < entities.Length; applied++) CopyTo(target[applied], entities[applied]);
        }
        catch
        {
            // Include the current entity: some setters validate after assigning another field.
            for (var i = Math.Min(applied, entities.Length - 1); i >= 0; i--) CopyTo(rollback[i], entities[i]);
            throw;
        }
    }

    private static CadEntity Copy(CadEntity e)
    {
        CadEntity copy = e switch
        {
            CadLine v => new CadLine(e.Id, e.LayerId, e.OwnerBlockId, v.Start, v.End),
            CadCircle v => new CadCircle(e.Id, e.LayerId, e.OwnerBlockId, v.Center, v.Radius),
            CadEllipse v => new CadEllipse(e.Id, e.LayerId, e.OwnerBlockId, v.Center, v.RadiusX, v.RadiusY),
            CadEllipseArc v => new CadEllipseArc(e.Id, e.LayerId, e.OwnerBlockId, v.Center, v.RadiusX, v.RadiusY, v.StartAngleRadians, v.SweepAngleRadians),
            CadArc v => new CadArc(e.Id, e.LayerId, e.OwnerBlockId, v.Center, v.Radius, v.StartAngleRadians, v.SweepAngleRadians),
            CadRectangle v => new CadRectangle(e.Id, e.LayerId, e.OwnerBlockId, v.FrameBounds, v.CornerRadiusX, v.CornerRadiusY),
            CadPolyline v => new CadPolyline(e.Id, e.LayerId, e.OwnerBlockId, v.Points, v.Closed),
            CadSpline v => new CadSpline(e.Id, e.LayerId, e.OwnerBlockId, v.FitPoints, v.Closed),
            CadRegion v => new CadRegion(e.Id, e.LayerId, e.OwnerBlockId, v.Contours),
            CadCompositePath v => new CadCompositePath(e.Id, e.LayerId, e.OwnerBlockId, v.StartPoint, v.Segments, v.Closed),
            CadText v => new CadText(e.Id, e.LayerId, e.OwnerBlockId, v.Text, v.Position, v.Height, v.RotationRadians,
                isInverted: v.IsInverted, invertedMarginFactor: v.InvertedMarginFactor),
            CadShapeText v => new CadShapeText(e.Id, e.LayerId, e.OwnerBlockId, v.Text, v.Position, v.Height, v.RotationRadians,
                v.WidthFactor, v.CharacterSpacingFactor, v.ObliqueAngleRadians, isInverted: v.IsInverted,
                invertedMarginFactor: v.InvertedMarginFactor, shapeFontId: v.ShapeFontId),
            // Only geometry is retained. Raster/OLE contents stay owned by the live entity.
            CadImage v => new CadImage(e.Id, e.LayerId, e.OwnerBlockId, v.FrameBounds, 1, 1, 4, [0, 0, 0, 0], rotationRadians: v.RotationRadians),
            CadOleObject v => new CadOleObject(e.Id, e.LayerId, e.OwnerBlockId, v.Bounds, [0]),
            CadBlockReference v => new CadBlockReference(e.Id, e.LayerId, e.OwnerBlockId, v.DefinitionBlockId, v.Position, v.RotationRadians, v.ScaleX, v.ScaleY),
            CadDimension v => new CadDimension(e.Id, e.LayerId, e.OwnerBlockId, v.Definition),
            _ => throw new NotSupportedException($"Entity type is not scalable: {e.GetType().Name}")
        };
        CopyTo(e, copy);
        return copy;
    }

    private static void CopyTo(CadEntity source, CadEntity target)
    {
        switch (source, target)
        {
            case (CadLine s, CadLine t): t.SetGeometry(s.Start, s.End); break;
            case (CadCircle s, CadCircle t): t.SetGeometry(s.Center, s.Radius); break;
            case (CadEllipse s, CadEllipse t): t.SetGeometry(s.Center, s.RadiusX, s.RadiusY); t.SetRotation(s.RotationRadians); break;
            case (CadEllipseArc s, CadEllipseArc t):
                t.SetGeometry(s.Center, s.RadiusX, s.RadiusY, s.StartAngleRadians, s.SweepAngleRadians); t.SetRotation(s.RotationRadians); break;
            case (CadArc s, CadArc t): t.SetGeometry(s.Center, s.Radius, s.StartAngleRadians, s.SweepAngleRadians); break;
            case (CadRectangle s, CadRectangle t):
                t.SetBounds(s.FrameBounds); t.SetCornerRadius(s.CornerRadiusX, s.CornerRadiusY); t.SetRotation(s.RotationRadians); break;
            case (CadPolyline s, CadPolyline t): t.ReplacePoints(s.Points); t.SetClosed(s.Closed); break;
            case (CadSpline s, CadSpline t): t.ReplaceFitPoints(s.FitPoints); t.SetClosed(s.Closed); break;
            case (CadRegion s, CadRegion t): t.ReplaceGeometry(s.Contours); break;
            case (CadCompositePath s, CadCompositePath t): t.ReplaceGeometry(s.StartPoint, s.Segments, s.Closed); break;
            case (CadText s, CadText t):
                t.SetPosition(s.Position); t.SetHeight(s.Height); t.SetRotation(s.RotationRadians);
                if (!s.RequiresBoundsMeasurement) t.SetLocalBounds(s.LocalBounds);
                break;
            case (CadShapeText s, CadShapeText t):
                t.SetGeometry(s.Position, s.Height, s.RotationRadians, s.WidthFactor, s.CharacterSpacingFactor, s.ObliqueAngleRadians); break;
            case (CadImage s, CadImage t): t.SetBounds(s.FrameBounds); t.SetRotation(s.RotationRadians); break;
            case (CadOleObject s, CadOleObject t): t.SetBounds(s.Bounds); break;
            case (CadBlockReference s, CadBlockReference t):
                t.SetPosition(s.Position); t.SetRotation(s.RotationRadians); t.SetScale(s.ScaleX, s.ScaleY); t.SetResolvedBounds(s.Bounds); break;
            case (CadDimension s, CadDimension t):
                t.SetDefinition(s.Definition); t.SetLineWeightState(s.LineWeight, s.UseLayerLineWeight); break;
            default: throw new InvalidOperationException("Mismatched scale geometry.");
        }
    }

    private static void GuardPoint(CadPointD point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentException("Scale produces non-finite geometry.");
    }

    private static void GuardGeometry(CadEntity entity)
    {
        var bounds = entity.Bounds;
        GuardPoint(new(bounds.MinX, bounds.MinY));
        GuardPoint(new(bounds.MaxX, bounds.MaxY));
        if (!double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
            throw new ArgumentException("Scale produces non-finite bounds.");
        IEnumerable<CadPointD> points = entity switch
        {
            CadLine v => [v.Start, v.End],
            CadPolyline v => v.Points,
            CadSpline v => v.FitPoints.Concat(v.GetBezierSegments().SelectMany(s => new[] { s.Start, s.Control1, s.Control2, s.End })),
            CadDimension v => v.Strokes.SelectMany(s => new[] { s.Start, s.End }),
            _ => []
        };
        foreach (var point in points) GuardPoint(point);
    }
}
