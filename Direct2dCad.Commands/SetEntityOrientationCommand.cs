using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Commands;

public sealed class SetEntityOrientationCommand(EntityId id,double rotationRadians) : ICadCommand
{
    private double _previous;
    public string Name=>"Set Entity Orientation";
    public CadDocumentChangeSet Execute(CadDocument document)
    {
        if(!double.IsFinite(rotationRadians)) throw new ArgumentOutOfRangeException(nameof(rotationRadians));
        var entity=document.GetEntity(id);CadEntityAccessPolicy.EnsureEditable(document,entity);
        _previous=Rotation(entity);Set(entity,rotationRadians);return CadCommandGeometryChanges.Resolve(document,[id],CadEntityChangeKind.Geometry|CadEntityChangeKind.Rotation);
    }
    public CadDocumentChangeSet Undo(CadDocument document)
    { Set(document.GetEntity(id),_previous);return CadCommandGeometryChanges.Resolve(document,[id],CadEntityChangeKind.Geometry|CadEntityChangeKind.Rotation); }
    public static double Rotation(CadEntity entity)=>entity switch {CadEllipse e=>e.RotationRadians,CadEllipseArc e=>e.RotationRadians,CadRectangle e=>e.RotationRadians,_=>throw new NotSupportedException("Orientation is supported for ellipses, ellipse arcs and rectangles.")};
    private static void Set(CadEntity entity,double rotation)
    { switch(entity) {case CadEllipse e:e.SetRotation(rotation);break;case CadEllipseArc e:e.SetRotation(rotation);break;case CadRectangle e:e.SetRotation(rotation);break;default:throw new NotSupportedException();} }
}
