using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Direct2D.Resources;

/// <summary>Owned geometry near zero. IDs, styles and payloads remain owned by the document.</summary>
internal static class Direct2DLocalGeometry
{
    public static CadEntity Resolve(CadEntity entity, out CadPointD origin)
    {
        origin = entity.Bounds.IsEmpty ? default : entity.Bounds.Center;
        if (!Direct2DCoordinateSystem.NeedsOrigin(origin) || entity is CadBlockReference or CadOleObject) { origin = default; return entity; }
        return Translate(entity, new(-origin.X, -origin.Y));
    }
    internal static CadEntity Translate(CadEntity entity, CadVectorD offset)
    {
        CadPointD Point(CadPointD point) => point + offset;
        CadRectD Bounds(CadRectD bounds) => bounds.Translate(offset);
        CadEntity local = entity switch
        {
            CadLine v => new CadLine(v.Id, v.LayerId, v.OwnerBlockId, Point(v.Start), Point(v.End)),
            CadCircle v => new CadCircle(v.Id, v.LayerId, v.OwnerBlockId, Point(v.Center), v.Radius),
            CadEllipse v => new CadEllipse(v.Id, v.LayerId, v.OwnerBlockId, Point(v.Center), v.RadiusX, v.RadiusY),
            CadEllipseArc v => new CadEllipseArc(v.Id, v.LayerId, v.OwnerBlockId, Point(v.Center), v.RadiusX, v.RadiusY, v.StartAngleRadians, v.SweepAngleRadians),
            CadArc v => new CadArc(v.Id, v.LayerId, v.OwnerBlockId, Point(v.Center), v.Radius, v.StartAngleRadians, v.SweepAngleRadians),
            CadRectangle v => new CadRectangle(v.Id, v.LayerId, v.OwnerBlockId, Bounds(v.FrameBounds), v.CornerRadiusX, v.CornerRadiusY),
            CadPolyline v => new CadPolyline(v.Id, v.LayerId, v.OwnerBlockId, v.Points.Select(Point), v.Closed),
            CadSpline v => new CadSpline(v.Id, v.LayerId, v.OwnerBlockId, v.FitPoints.Select(Point), v.Closed),
            CadRegion v => new CadRegion(v.Id, v.LayerId, v.OwnerBlockId, v.Contours.Select(c => c.Transform(Point))),
            CadCompositePath v => new CadCompositePath(v.Id, v.LayerId, v.OwnerBlockId, Point(v.StartPoint), v.Segments.Select(s => s switch
            {
                CadCompositeLineSegment line => (CadCompositePathSegment)new CadCompositeLineSegment(Point(line.End)),
                CadCompositeArcSegment arc => new CadCompositeArcSegment(Point(arc.Center), arc.SweepAngleRadians),
                CadCompositeSplineSegment spline => new CadCompositeSplineSegment(spline.FitPoints.Select(Point).ToArray()),
                _ => throw new NotSupportedException()
            }), v.Closed),
            CadText v => new CadText(v.Id, v.LayerId, v.OwnerBlockId, v.Text, Point(v.Position), v.Height, v.RotationRadians,
                v.TextStyleId, isInverted: v.IsInverted, invertedMarginFactor: v.InvertedMarginFactor),
            CadShapeText v => new CadShapeText(v.Id, v.LayerId, v.OwnerBlockId, v.Text, Point(v.Position), v.Height, v.RotationRadians,
                v.WidthFactor, v.CharacterSpacingFactor, v.ObliqueAngleRadians, isInverted: v.IsInverted,
                invertedMarginFactor: v.InvertedMarginFactor, shapeFontId: v.ShapeFontId),
            // The original resource bucket keeps the embedded payloads.
            CadImage v => new CadImage(v.Id, v.LayerId, v.OwnerBlockId, Bounds(v.FrameBounds), 1, 1, 4, [0, 0, 0, 0], opacity: v.Opacity, rotationRadians: v.RotationRadians),
            CadDimension v => new CadDimension(v.Id, v.LayerId, v.OwnerBlockId, v.Definition with
            {
                Anchors = v.Definition.Anchors.Select(a => a with { Point = Point(a.Point) }).ToArray(), Placement = Point(v.Definition.Placement)
            }),
            _ => throw new NotSupportedException($"Unsupported local render geometry: {entity.GetType().Name}")
        };
        switch (entity, local)
        {
            case (CadEllipse s, CadEllipse t): t.SetRotation(s.RotationRadians); t.SetFillStyleInternal(s.FillStyleId); break;
            case (CadEllipseArc s, CadEllipseArc t): t.SetRotation(s.RotationRadians); break;
            case (CadRectangle s, CadRectangle t): t.SetRotation(s.RotationRadians); t.SetFillStyleInternal(s.FillStyleId); break;
            case (CadCircle s, CadCircle t): t.SetFillStyleInternal(s.FillStyleId); break;
            case (CadPolyline s, CadPolyline t): t.SetFillStyleInternal(s.FillStyleId); break;
            case (CadSpline s, CadSpline t): t.SetFillStyleInternal(s.FillStyleId); break;
            case (CadRegion s, CadRegion t): t.SetFillStyleInternal(s.FillStyleId); break;
            case (CadCompositePath s, CadCompositePath t): t.SetFillStyleInternal(s.FillStyleId); break;
            case (CadText { RequiresBoundsMeasurement: false } s, CadText t): t.SetLocalBounds(s.LocalBounds); break;
        }
        local.SetStrokeStyle(entity.StrokeStyle); local.SetLineWeightState(entity.LineWeight, entity.UseLayerLineWeight);
        local.SetColorSource(entity.ColorSource); local.SetVisible(entity.IsVisible);
        if (entity.IsErased) local.Erase();
        return local;
    }
}
