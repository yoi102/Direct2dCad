using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;

namespace Direct2dCad.ViewModels.Services.Snapping;

internal sealed record CadSnapCandidate(CadPointD Point, CadObjectSnapModes Kind, int Priority);

/// <summary>Queries the active-space index and retains candidates inside a screen-space hysteresis band.</summary>
internal sealed class CadObjectSnapController
{
    public CadSnapCandidate? Current { get; private set; }
    public IReadOnlyList<CadSnapCandidate> Candidates { get; private set; } = [];
    private int _cycle;
    public void Clear() { Current = null; Candidates = []; _cycle = 0; }
    public void Cycle() { if (Candidates.Count > 1) { var index=Candidates.ToList().IndexOf(Current!); Current=Candidates[(index+1)%Candidates.Count]; _cycle=0; } }

    public CadPointD Resolve(CadDocument document, BlockId blockId, CadViewport viewport,
        Func<BlockId,CadRectD,IReadOnlyList<EntityId>> query, CadPointD pointer, CadPointD? anchor)
    {
        var settings = document.ViewSettings.Snap;
        var tolerance = settings.ScreenTolerance / Math.Max(viewport.Zoom, 1e-9);
        var result = new List<CadSnapCandidate>();
        var primitives = new List<CadPlanarPrimitive>();
        if (settings.ObjectsEnabled)
        {
            var bounds = CadRectD.FromLTRB(pointer.X-tolerance*1.5,pointer.Y-tolerance*1.5,pointer.X+tolerance*1.5,pointer.Y+tolerance*1.5);
            var visited = new HashSet<BlockId>(); var budget = 4096;
            void Add(CadPointD p, CadObjectSnapModes kind, int priority)
            {
                if ((settings.Modes & kind) != 0 && p.DistanceTo(pointer) <= tolerance*1.5 &&
                    !result.Any(c => c.Kind == kind && CadGeometryTolerance.Coincident(c.Point,p))) result.Add(new(p,kind,priority));
            }
            void Visit(CadEntity entity, CadMatrixD transform, int depth)
            {
                if (--budget < 0 || depth > 32 || !CadEntityAccessPolicy.IsSelectable(document,entity) || !entity.Bounds.Transform(transform).Intersects(bounds)) return;
                if (entity is CadBlockReference reference)
                {
                    if (!document.TryGetBlock(reference.DefinitionBlockId,out var definition) || definition is null || !visited.Add(definition.Id)) return;
                    var nested = CadBlockTransform.Create(definition,reference) * transform;
                    if(!nested.TryInvert(out var inverse)) { visited.Remove(definition.Id); return; }
                    foreach (var id in query(definition.Id, bounds.Transform(inverse)))
                        if (document.TryGetEntity(id,out var child) && child is not null) Visit(child,nested,depth+1);
                    visited.Remove(definition.Id); return;
                }
                if (entity is CadEllipse ellipse) Add(transform.TransformPoint(ellipse.Center),CadObjectSnapModes.Center,3);
                if (entity is CadEllipseArc ea)
                {
                    Add(transform.TransformPoint(ea.Center),CadObjectSnapModes.Center,3);
                    Add(transform.TransformPoint(ea.StartPoint),CadObjectSnapModes.Endpoint,0);
                    Add(transform.TransformPoint(ea.EndPoint),CadObjectSnapModes.Endpoint,0);
                }
                if (entity is CadSpline spline)
                {
                    Add(transform.TransformPoint(spline.FitPoints[0]),CadObjectSnapModes.Endpoint,0);
                    Add(transform.TransformPoint(spline.FitPoints[^1]),CadObjectSnapModes.Endpoint,0);
                }
                IReadOnlyList<CadPlanarPrimitive> local;
                try { local = CadPlanarCurves.Get(entity); } catch (NotSupportedException) { return; }
                var x = transform.TransformVector(CadVectorD.UnitX); var y = transform.TransformVector(CadVectorD.UnitY);
                foreach (var p in local)
                {
                    if (p.IsLine || Math.Abs(p.Sweep) < Math.PI*2-1e-10)
                    { Add(transform.TransformPoint(p.Start),CadObjectSnapModes.Endpoint,0); Add(transform.TransformPoint(p.End),CadObjectSnapModes.Endpoint,0); }
                    Add(transform.TransformPoint(p.At(.5)),CadObjectSnapModes.Midpoint,2);
                    if (!p.IsLine) Add(transform.TransformPoint(p.Center),CadObjectSnapModes.Center,3);
                    if (p.IsLine) primitives.Add(CadPlanarPrimitive.Line(transform.TransformPoint(p.Start),transform.TransformPoint(p.End)));
                    else if (Math.Abs(x.Length-y.Length) <= 1e-9*Math.Max(x.Length,y.Length) && Math.Abs(x.Dot(y)) <= 1e-9*x.Length*y.Length)
                    {
                        var c = transform.TransformPoint(p.Center); var start = transform.TransformPoint(p.Start);
                        primitives.Add(CadPlanarPrimitive.Arc(c,p.Radius*x.Length,Math.Atan2(start.Y-c.Y,start.X-c.X),p.Sweep*Math.Sign(x.Cross(y))));
                    }
                }
            }
            foreach (var id in query(blockId,bounds)) if (document.TryGetEntity(id,out var entity) && entity is not null) Visit(entity,CadMatrixD.Identity,0);
            // Bound pair work in dense drawings; basic candidates remain available.
            for (var i=0; i<Math.Min(primitives.Count,128);i++)
            {
                var p = primitives[i];
                for (var j=i+1;j<Math.Min(primitives.Count,128);j++)
                    foreach (var point in CadPlanarGeometry.Intersections(p,primitives[j])) Add(point,CadObjectSnapModes.Intersection,1);
                if (anchor is { } a)
                {
                    if (p.IsLine)
                    {
                        var point = p.At(p.Parameter(a));
                        if (p.Contains(point)) Add(point,CadObjectSnapModes.Perpendicular,4);
                    }
                    else
                    {
                        var distance = a.DistanceTo(p.Center);
                        if (distance > p.Radius+CadGeometryTolerance.Absolute)
                        {
                            var angle = Math.Atan2(a.Y-p.Center.Y,a.X-p.Center.X); var offset = Math.Acos(p.Radius/distance);
                            foreach (var angle2 in new[]{angle-offset,angle+offset})
                            { var point = CadPlanarPrimitive.Point(p.Center,p.Radius,angle2); if(p.Contains(point)) Add(point,CadObjectSnapModes.Tangent,4); }
                        }
                    }
                }
            }
        }
        var sorted = result.Where(c=>c.Point.DistanceTo(pointer)<=tolerance).OrderBy(c=>c.Priority).ThenBy(c=>c.Point.DistanceTo(pointer)).ThenBy(c=>c.Point.X).ThenBy(c=>c.Point.Y).ToList();
        var retained = Current is { } old ? result.FirstOrDefault(c=>c.Kind==old.Kind && CadGeometryTolerance.Coincident(c.Point,old.Point)) : null;
        if (retained is not null && (sorted.Count==0 || retained.Priority<=sorted[0].Priority))
        { sorted.RemoveAll(c=>c==retained); sorted.Insert(0,retained); }
        if (!Candidates.SequenceEqual(sorted)) _cycle=0;
        Candidates=sorted; Current=sorted.Count==0 ? null : sorted[Math.Min(_cycle,sorted.Count-1)];
        if (Current is { } snap) return snap.Point;
        var constrained = new CadSnapInteractionService(document,viewport).SnapWorld(pointer);
        if (anchor is not { } origin) return constrained;
        var delta = constrained-origin;
        if (settings.OrthoEnabled) return Math.Abs(delta.X)>=Math.Abs(delta.Y) ? new(constrained.X,origin.Y) : new(origin.X,constrained.Y);
        if (settings.PolarEnabled && delta.Length>0)
        {
            var step=settings.PolarIncrementDegrees*Math.PI/180;
            var angle=Math.Round(Math.Atan2(delta.Y,delta.X)/step)*step;
            var direction=new CadVectorD(Math.Cos(angle),Math.Sin(angle));
            var point=origin+direction*delta.Dot(direction);
            if(point.DistanceTo(pointer)<=tolerance) return point;
        }
        return constrained;
    }
}
