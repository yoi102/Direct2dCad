using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

/// <summary>Single surviving curves preserve IDs. Split/join erase old identities and expose new result IDs; clients must not guess reference rebinding.</summary>
public sealed class EditCurvesCommand(string name,CadCurveEditPlan plan) : ICadCommand
{
    private readonly Dictionary<EntityId,CadCurveShape> _originals=[];
    private readonly List<EntityId> _created=[];
    private readonly HashSet<EntityId> _modified=[];
    public string Name => name;
    public IReadOnlyList<EntityId> ResultEntityIds { get; private set; } = [];
    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var sources=plan.Replacements.Select(r=>r.SourceId).Concat(plan.Creations.Select(c=>c.StyleSourceId)).Distinct().ToArray();
        foreach(var id in sources)
        {
            var entity=document.GetEntity(id); CadEntityAccessPolicy.EnsureEditable(document,entity); CadEntityAccessPolicy.EnsureCanAddToLayer(document,entity.LayerId);
            if(!_originals.ContainsKey(id)) _originals[id]=CadCurveShape.From(entity);
        }
        foreach(var shape in plan.Replacements.SelectMany(r=>r.Shapes).Concat(plan.Creations.Select(c=>c.Shape))) shape.Validate();
        if(_created.Count>0 || _modified.Count>0)
        {
            foreach(var id in _created) document.GetEntity(id).Restore();
            foreach(var replacement in plan.Replacements)
            {
                var entity=document.GetEntity(replacement.SourceId);
                if(_modified.Contains(entity.Id)) Apply(entity,replacement.Shapes[0]); else entity.Erase();
            }
            return Changes(document);
        }
        var outputs=new List<EntityId>();
        try
        {
            foreach(var replacement in plan.Replacements)
            {
                var entity=document.GetEntity(replacement.SourceId);
                if(replacement.Shapes.Count==1 && Compatible(entity,replacement.Shapes[0])) { _modified.Add(entity.Id); outputs.Add(entity.Id); }
                else foreach(var shape in replacement.Shapes) outputs.Add(Create(document,entity,shape).Id);
            }
            foreach(var creation in plan.Creations) outputs.Add(Create(document,document.GetEntity(creation.StyleSourceId),creation.Shape).Id);
            foreach(var replacement in plan.Replacements)
            {
                var entity=document.GetEntity(replacement.SourceId);
                if(_modified.Contains(entity.Id)) Apply(entity,replacement.Shapes[0]); else entity.Erase();
            }
            ResultEntityIds=outputs;
            return Changes(document);
        }
        catch
        {
            foreach(var id in _created) document.RemoveEntity(id);
            foreach(var id in _modified) Apply(document.GetEntity(id),_originals[id]);
            foreach(var replacement in plan.Replacements) document.GetEntity(replacement.SourceId).Restore();
            _created.Clear(); _modified.Clear(); throw;
        }
    }
    public CadDocumentChangeSet Undo(CadDocument document)
    {
        foreach(var replacement in plan.Replacements)
        {
            var entity=document.GetEntity(replacement.SourceId); entity.Restore();
            if(_modified.Contains(entity.Id)) Apply(entity,_originals[entity.Id]);
        }
        foreach(var id in _created) document.GetEntity(id).Erase();
        return Changes(document, undo: true);
    }
    private CadDocumentChangeSet Changes(CadDocument document, bool undo = false)
    {
        var changes = plan.Replacements.Select(replacement => new CadEntityChange(replacement.SourceId,
            _modified.Contains(replacement.SourceId)
                ? CadEntityChangeKind.Geometry | CadEntityChangeKind.Appearance
                : undo ? CadEntityChangeKind.Created : CadEntityChangeKind.Deleted));
        var results = _created.Select(id => new CadEntityChange(id,
            undo ? CadEntityChangeKind.Deleted : CadEntityChangeKind.Created));
        return CadCommandGeometryChanges.Resolve(document, changes.Concat(results).ToArray());
    }
    private CadEntity Create(CadDocument document,CadEntity source,CadCurveShape shape)
    {
        var style=CadCurveEditing.GraphicStyle(source); var fill=shape.Closed ? FillStyle(source) : null; CadEntity entity;
        if(shape.Segments.Count==1)
        {
            var p=shape.Segments[0];
            entity=p.IsLine ? document.AddLine(p.Start,p.End,source.LayerId,style,source.Name) :
                Math.Abs(p.Sweep)>=Math.PI*2-1e-10 ? document.AddCircle(p.Center,p.Radius,source.LayerId,style,fillStyleId:fill,name:source.Name) :
                document.AddArc(p.Center,p.Radius,p.StartAngle,p.Sweep,source.LayerId,style,source.Name);
        }
        else if(shape.Segments.All(p=>p.IsLine)) entity=document.AddPolyline(Points(shape),shape.Closed,source.LayerId,style,fillStyleId:fill,name:source.Name);
        else entity=document.AddCompositePath(shape.Segments[0].Start,shape.Segments.Select<CadPlanarPrimitive,CadCompositePathSegment>(p=>p.IsLine ? new CadCompositeLineSegment(p.End) : new CadCompositeArcSegment(p.Center,p.Sweep)),shape.Closed,source.LayerId,style,fillStyleId:fill,name:source.Name);
        _created.Add(entity.Id);
        document.MoveEntityToBlock(entity.Id,source.OwnerBlockId);
        entity.SetLineWeightState(source.LineWeight,source.UseLayerLineWeight); entity.SetColorSource(source.ColorSource); entity.SetStrokeStyle(source.StrokeStyle); entity.SetZIndex(source.ZIndex); entity.SetVisible(source.IsVisible);
        return entity;
    }
    private static bool Compatible(CadEntity entity,CadCurveShape shape) => entity switch
    {
        CadLine => shape.Segments.Count==1 && shape.Segments[0].IsLine,
        CadArc => shape.Segments.Count==1 && !shape.Segments[0].IsLine && Math.Abs(shape.Segments[0].Sweep)<Math.PI*2-1e-10,
        CadCircle => shape.Segments.Count==1 && !shape.Segments[0].IsLine && Math.Abs(shape.Segments[0].Sweep)>=Math.PI*2-1e-10,
        CadPolyline => shape.Segments.All(p=>p.IsLine), CadCompositePath => true, _=>false
    };
    private static IEnumerable<CadPointD> Points(CadCurveShape shape) => shape.Closed ? shape.Segments.Select(s=>s.Start) : shape.Segments.Select(s=>s.Start).Append(shape.Segments[^1].End);
    private static StyleId? FillStyle(CadEntity entity) => entity switch
    { CadCircle e=>e.FillStyleId,CadPolyline e=>e.FillStyleId,CadCompositePath e=>e.FillStyleId,CadRectangle e=>e.FillStyleId,_=>null };
    private static void Apply(CadEntity entity,CadCurveShape shape)
    {
        var p=shape.Segments[0];
        switch(entity)
        {
            case CadLine e: e.SetGeometry(p.Start,p.End); break;
            case CadArc e: e.SetGeometry(p.Center,p.Radius,p.StartAngle,p.Sweep); break;
            case CadCircle e: e.SetGeometry(p.Center,p.Radius); break;
            case CadPolyline e: e.ReplacePoints(Points(shape)); e.SetClosed(shape.Closed); break;
            case CadCompositePath e: e.ReplaceGeometry(p.Start,shape.Segments.Select<CadPlanarPrimitive,CadCompositePathSegment>(s=>s.IsLine ? new CadCompositeLineSegment(s.End) : new CadCompositeArcSegment(s.Center,s.Sweep)),shape.Closed); break;
        }
    }
}
