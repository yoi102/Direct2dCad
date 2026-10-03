using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;
using static Direct2dCad.ViewModels.Services.Geometry.CadGripDragGeometryFactory;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel
{
    public bool IsGripEditing => _gripDrag.IsActive;
    private CadEntity? GripEntity => _gripDrag.ActiveDrag is { } drag &&
        CadEditor.Document.TryGetEntity(drag.Handle.EntityId, out var entity) ? entity : null;

    private string[] GetGripDynamicInputKeys()
    {
        if (_gripDrag.ActiveDrag is not { } drag) return [];
        if (drag.Handle.Type == CadHandleType.Center) return ["X", "Y"];
        return GripEntity switch
        {
            CadLine => ["Length", "Angle"],
            CadCircle => ["Radius"],
            CadArc => drag.Handle.Type == CadHandleType.Vertex ? ["Radius", "Angle"] : ["Radius"],
            CadEllipse ellipse => [IsHorizontalEllipseGrip(ellipse) ? "AxisX" : "AxisY"],
            CadRectangle when drag.Handle.Type == CadHandleType.BoundsCorner => ["Width", "Height"],
            CadCompositePath or CadRegion when drag.Handle.Type == CadHandleType.BoundsCorner => ["GripScale"],
            CadImage or CadBlockReference when drag.Handle.Type == CadHandleType.Rotation => ["Angle"],
            _ => ["X", "Y"]
        };
    }

    private CadPointD? GripDynamicInputAnchor
    {
        get
        {
            if (_gripDrag.ActiveDrag is not { } drag) return null;
            if (drag.Handle.Type == CadHandleType.Center) return null;
            return GripEntity switch
            {
                CadLine line => IsLineStartGrip(line, drag.Handle.Position) ? line.End : line.Start,
                CadCircle circle => circle.Center,
                CadArc arc => arc.Center,
                CadEllipse ellipse => ellipse.Center,
                CadRectangle rectangle => RectangleOppositeCorner(rectangle),
                CadCompositePath or CadRegion => BoundsOppositeCorner(GripEntity.Bounds, drag.Handle.Position),
                CadImage image => image.FrameBounds.Center,
                CadBlockReference reference => reference.Position,
                _ => null
            };
        }
    }

    private bool IsHorizontalEllipseGrip(CadEllipse ellipse)
    {
        var delta = ellipse.ToLocal(_gripDrag.ActiveDrag!.Handle.Position) - ellipse.Center;
        return Math.Abs(delta.X) >= Math.Abs(delta.Y);
    }
    private static CadPointD BoundsOppositeCorner(CadRectD bounds, CadPointD grip) => new(
        Math.Abs(grip.X - bounds.MinX) <= Math.Abs(grip.X - bounds.MaxX) ? bounds.MaxX : bounds.MinX,
        Math.Abs(grip.Y - bounds.MinY) <= Math.Abs(grip.Y - bounds.MaxY) ? bounds.MaxY : bounds.MinY);
    private CadPointD RectangleOppositeCorner(CadRectangle rectangle) => rectangle.GeometryTransform.TransformPoint(
        BoundsOppositeCorner(rectangle.FrameBounds, rectangle.ToLocal(_gripDrag.ActiveDrag!.Handle.Position)));

    private double[] GetGripDynamicInputValues(CadPointD point)
    {
        double Display(double n) => Direct2dCad.Db.Cad.Settings.CadUnitConversion.FromMillimeters(n, DocumentUnit);
        var keys = GetGripDynamicInputKeys();
        var delta = point - (GripDynamicInputAnchor ?? CadPointD.Origin);
        if (keys.Length == 0) return [];
        if (keys[0] == "X") return [Display(point.X), Display(point.Y)];
        if (GripEntity is CadEllipse ellipse)
        {
            var local = ellipse.ToLocal(point) - ellipse.Center;
            return [Display(IsHorizontalEllipseGrip(ellipse) ? Math.Abs(local.X) : Math.Abs(local.Y))];
        }
        if (keys[0] == "Width" && GripEntity is CadRectangle rectangle)
        {
            var local = CadMatrixD.CreateRotation(-rectangle.RotationRadians).TransformVector(delta);
            return [Display(Math.Abs(local.X)), Display(Math.Abs(local.Y))];
        }
        if (keys[0] == "GripScale")
        {
            var original = _gripDrag.ActiveDrag!.Handle.Position - GripDynamicInputAnchor!.Value;
            return [delta.Dot(original) / original.LengthSquared];
        }
        var angle = Math.Atan2(delta.Y, delta.X) * 180 / Math.PI;
        if (keys[0] == "Angle") return [GripRotation(point)];
        return keys.Length == 1 ? [Display(delta.Length)] : [Display(delta.Length), (angle + 360) % 360];
    }

    private double GripRotation(CadPointD point)
    {
        var original = _gripDrag.ActiveDrag!.Handle.Position - GripDynamicInputAnchor!.Value;
        var target = point - GripDynamicInputAnchor.Value;
        var rotation = GripEntity switch { CadImage i => i.RotationRadians, CadBlockReference b => b.RotationRadians, _ => 0 };
        return (rotation + Math.Atan2(target.Y, target.X) - Math.Atan2(original.Y, original.X)) * 180 / Math.PI;
    }

    private bool TryResolveGripDynamicInput(double[] values, out CadPointD point)
    {
        point = _dynamicInputPointer;
        var keys = GetGripDynamicInputKeys();
        if (keys.Length == 0) return false;
        double World(double n) => Direct2dCad.Db.Cad.Settings.CadUnitConversion.ToMillimeters(n, DocumentUnit);
        if (keys[0] == "X") { point = new(World(values[0]), World(values[1])); return true; }
        if (keys[0] != "Angle" && values[0] <= 0) return false;
        var anchor = GripDynamicInputAnchor!.Value;
        var delta = point - anchor;
        var original = _gripDrag.ActiveDrag!.Handle.Position - anchor;
        if (GripEntity is CadEllipse ellipse)
        {
            var local = ellipse.ToLocal(point) - ellipse.Center;
            var source = ellipse.ToLocal(_gripDrag.ActiveDrag.Handle.Position) - ellipse.Center;
            var horizontal = IsHorizontalEllipseGrip(ellipse);
            var sign = Math.Sign(horizontal ? local.X : local.Y);
            if (sign == 0) sign = Math.Sign(horizontal ? source.X : source.Y);
            point = ellipse.GeometryTransform.TransformPoint(ellipse.Center +
                (horizontal ? new CadVectorD(sign * World(values[0]), 0) : new CadVectorD(0, sign * World(values[0]))));
        }
        else if (keys[0] == "Width" && GripEntity is CadRectangle rectangle)
        {
            if (values[1] <= 0) return false;
            var local = CadMatrixD.CreateRotation(-rectangle.RotationRadians).TransformVector(delta);
            var source = CadMatrixD.CreateRotation(-rectangle.RotationRadians).TransformVector(original);
            var sx = Math.Sign(local.X == 0 ? source.X : local.X); var sy = Math.Sign(local.Y == 0 ? source.Y : local.Y);
            point = anchor + CadMatrixD.CreateRotation(rectangle.RotationRadians).TransformVector(new(sx * World(values[0]), sy * World(values[1])));
        }
        else if (keys[0] == "GripScale") point = anchor + original * values[0];
        else if (keys[0] == "Angle")
        {
            var rotation = GripEntity switch { CadImage i => i.RotationRadians, CadBlockReference b => b.RotationRadians, _ => 0 };
            var radians = values[0] % 360 * Math.PI / 180 - rotation + Math.Atan2(original.Y, original.X);
            point = anchor + new CadVectorD(Math.Cos(radians), Math.Sin(radians)) * (delta.Length > 1e-9 ? delta.Length : original.Length);
        }
        else
        {
            var direction = keys.Length > 1 ? new CadVectorD(Math.Cos(values[1] % 360 * Math.PI / 180), Math.Sin(values[1] % 360 * Math.PI / 180)) :
                delta.Length > 1e-9 ? delta.Normalize() : original.Normalize();
            point = anchor + direction * World(values[0]);
        }
        return double.IsFinite(point.X) && double.IsFinite(point.Y);
    }

    private void UpdateGripDynamicInputPointer(CadPointD pointerWorld)
    {
        if (_gripDrag.ActiveDrag is not { } drag) return;
        var point = drag.Handle.Position + (pointerWorld - drag.StartPointerWorld);
        UpdateDynamicInputPointer(point);
        ApplyGripDynamicInput(ResolveDynamicInputPreview(point));
    }
    private void ApplyGripDynamicInput(CadPointD point)
    {
        if (_gripDrag.ActiveDrag is { } drag)
            drag.CurrentPointerWorld = drag.StartPointerWorld + (point - drag.Handle.Position);
    }
}
