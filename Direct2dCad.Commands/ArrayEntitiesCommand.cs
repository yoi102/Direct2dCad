using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class ArrayEntitiesCommand : ICadCommand
{
    private readonly EntityId[] _ids;
    private readonly List<(CadVectorD Delta,double Angle)> _placements=[];
    private readonly List<ICadCommand> _executed=[];
    private readonly CadPointD _center;
    public string Name { get; }
    public IReadOnlyList<EntityId> CreatedEntityIds { get; private set; }=[];
    public ArrayEntitiesCommand(IEnumerable<EntityId> ids,int rows,int columns,double spacingX,double spacingY)
    {
        _ids=ids.Distinct().ToArray(); Name="Rectangular Array";
        ValidateCount(rows,columns,_ids.Length);
        if(!double.IsFinite(spacingX) || !double.IsFinite(spacingY) || columns>1 && Math.Abs(spacingX)<=CadGeometryTolerance.Absolute || rows>1 && Math.Abs(spacingY)<=CadGeometryTolerance.Absolute) throw new ArgumentOutOfRangeException(nameof(spacingX));
        for(var row=0;row<rows;row++) for(var col=0;col<columns;col++) if(row!=0 || col!=0)
        {
            var delta=new CadVectorD(col*spacingX,row*spacingY);
            if(!double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) throw new ArgumentOutOfRangeException(nameof(spacingX));
            _placements.Add((delta,0));
        }
    }
    public ArrayEntitiesCommand(IEnumerable<EntityId> ids,CadPointD center,int count,double sweepRadians,bool rotateCopies=true)
    {
        _ids=ids.Distinct().ToArray(); _center=center; Name="Polar Array";
        ValidateCount(1,count,_ids.Length);
        if(!double.IsFinite(center.X) || !double.IsFinite(center.Y)) throw new ArgumentOutOfRangeException(nameof(center));
        if(!double.IsFinite(sweepRadians) || Math.Abs(sweepRadians)<=1e-10 || Math.Abs(sweepRadians)>Math.PI*2+1e-10) throw new ArgumentOutOfRangeException(nameof(sweepRadians));
        var full=Math.Abs(Math.Abs(sweepRadians)-Math.PI*2)<1e-10;
        for(var i=1;i<count;i++) _placements.Add((default,sweepRadians*i/(full ? count : count-1)));
        RotateCopies=rotateCopies;
    }
    private bool RotateCopies { get; }=true;
    private static void ValidateCount(int rows,int cols,int entities)
    { if(rows<1 || cols<1 || rows>1000 || cols>1000 || entities==0 || (long)rows*cols*entities>10000 || rows*cols<2) throw new ArgumentOutOfRangeException(nameof(rows),"Array requires 2 or more placements and at most 10000 total entities."); }
    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var owner=document.GetEntity(_ids[0]).OwnerBlockId;
        foreach(var id in _ids)
        {
            var e=document.GetEntity(id); CadEntityAccessPolicy.EnsureEditable(document,e);
            if(e.OwnerBlockId!=owner) throw new InvalidOperationException("Array entities must share an owner space.");
            if(Name=="Polar Array" && RotateCopies) foreach(var placement in _placements) CadEntityTransform.ValidateRotation(e,placement.Angle);
        }
        var changes=CadDocumentChangeSet.Empty;
        if(_executed.Count>0)
        {
            var completed=new List<ICadCommand>();
            try { foreach(var command in _executed) { changes=CadDocumentChangeSet.Combine([changes,command.Execute(document)]); completed.Add(command); } return changes; }
            catch { foreach(var command in completed.AsEnumerable().Reverse()) command.Undo(document); throw; }
        }
        var ids=new List<EntityId>();
        var existing=document.Entities.Keys.ToHashSet();
        var existingBlocks=document.Blocks.Keys.ToHashSet();
        try
        {
            foreach(var placement in _placements)
            {
                var duplicate=new DuplicateEntitiesCommand(_ids,placement.Delta,owner);
                changes=CadDocumentChangeSet.Combine([changes,duplicate.Execute(document)]); _executed.Add(duplicate); ids.AddRange(duplicate.CreatedEntityIds);
                if(duplicate.CreatedEntityIds.Count!=_ids.Length) throw new NotSupportedException("Every selected entity must support copying for an array.");
                if(Name=="Polar Array")
                {
                    ICadCommand transform;
                    if(RotateCopies) transform=new RotateEntitiesCommand(duplicate.CreatedEntityIds,_center,placement.Angle);
                    else
                    {
                        var bounds=_ids.Aggregate(CadRectD.Empty,(b,id)=>b.Union(document.GetEntity(id).Bounds));
                        var moved=CadMatrixD.CreateRotation(placement.Angle,_center).TransformPoint(bounds.Center);
                        transform=new MoveEntitiesCommand(duplicate.CreatedEntityIds,moved-bounds.Center);
                    }
                    changes=CadDocumentChangeSet.Combine([changes,transform.Execute(document)]); _executed.Add(transform);
                }
            }
            CreatedEntityIds=ids; return changes;
        }
        catch
        {
            foreach(var command in _executed.AsEnumerable().Reverse()) command.Undo(document);
            foreach(var id in document.Entities.Keys.Where(id=>!existing.Contains(id)).ToArray()) document.RemoveEntity(id);
            foreach(var id in document.Blocks.Keys.Where(id=>!existingBlocks.Contains(id)).ToArray()) document.RemoveBlockDefinition(id);
            _executed.Clear(); CreatedEntityIds=[]; throw;
        }
    }
    public CadDocumentChangeSet Undo(CadDocument document)
    { var changes=CadDocumentChangeSet.Empty; foreach(var command in _executed.AsEnumerable().Reverse()) changes=CadDocumentChangeSet.Combine([changes,command.Undo(document)]); return changes; }
}
