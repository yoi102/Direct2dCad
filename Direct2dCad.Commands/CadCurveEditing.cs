using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed record CadCurveShape(IReadOnlyList<CadPlanarPrimitive> Segments, bool Closed = false)
{
    public static CadCurveShape From(CadEntity entity) => entity is CadRegion
        ? throw new NotSupportedException("Region boundary editing is not supported by this curve tool. Use Boolean operations or whole-entity transforms.")
        : new(CadPlanarCurves.Get(entity), entity is Curve { IsClosed:true });
    public CadCurveShape Validate()
    {
        if (Segments.Count==0) throw new InvalidOperationException("The operation produced an empty curve.");
        foreach(var p in Segments)
            if (!double.IsFinite(p.Start.X) || !double.IsFinite(p.Start.Y) || !double.IsFinite(p.End.X) || !double.IsFinite(p.End.Y) ||
                (p.IsLine ? p.Start.DistanceTo(p.End) : p.Radius*Math.Abs(p.Sweep)) <= CadGeometryTolerance.Absolute || !double.IsFinite(p.Radius))
                throw new InvalidOperationException("The operation would create a degenerate curve.");
        for(var i=1;i<Segments.Count;i++) if(!CadGeometryTolerance.Coincident(Segments[i-1].End,Segments[i].Start))
            throw new InvalidOperationException("The operation would create a disconnected path.");
        if(Closed && !CadGeometryTolerance.Coincident(Segments[^1].End,Segments[0].Start))
            throw new InvalidOperationException("The operation would create an open closed-path boundary.");
        return this;
    }
}
public sealed record CadCurveReplacement(EntityId SourceId, IReadOnlyList<CadCurveShape> Shapes);
public sealed record CadCurveCreation(EntityId StyleSourceId, CadCurveShape Shape);
public sealed record CadCurveEditPlan(IReadOnlyList<CadCurveReplacement> Replacements, IReadOnlyList<CadCurveCreation> Creations)
{
    public IReadOnlyList<CadCurveShape>? PreviewGeometry { get; init; }
    public static CadCurveEditPlan Replace(EntityId id, params CadCurveShape[] shapes) => new([new(id,shapes)],[]);
}

/// <summary>Pure geometry plans are shared by pointer previews, Terminal and workspace tools.</summary>
public static partial class CadCurveEditing
{
    public static CadCurveEditPlan Offset(CadEntity entity, double distance, CadPointD side)
    {
        if (!double.IsFinite(distance) || distance<=CadGeometryTolerance.Absolute) throw new ArgumentOutOfRangeException(nameof(distance));
        var shape=CadCurveShape.From(entity).Validate(); var source=shape.Segments;
        var nearest=source.OrderBy(p=>p.At(Math.Clamp(p.Parameter(side),0,1)).DistanceTo(side)).First();
        var signed = nearest.IsLine ? Math.Sign((nearest.End-nearest.Start).Cross(side-nearest.Start)) : Math.Sign(nearest.Radius-nearest.Center.DistanceTo(side))*Math.Sign(nearest.Sweep);
        if(signed==0) throw new InvalidOperationException("Choose a side away from the source curve.");
        var amount=distance*signed;
        var shifted=source.Select(p=>OffsetPrimitive(p,amount)).ToArray();
        var corners=shape.Closed ? shifted.Length : shifted.Length-1;
        for(var i=0;i<corners;i++)
        {
            var next=(i+1)%shifted.Length;
            if(shifted.Length==1) break;
            var joins=CadPlanarGeometry.Intersections(shifted[i],shifted[next],true,true);
            if(joins.Count==0) throw new InvalidOperationException("This offset has no valid corner connection; reduce the distance.");
            var point=joins.OrderBy(p=>p.DistanceTo(source[i].End)).First();
            if(point.DistanceTo(source[i].End)>distance*1000) throw new InvalidOperationException("The offset would create an excessively sharp corner.");
            shifted[i]=EndAt(shifted[i],point); shifted[next]=StartAt(shifted[next],point);
        }
        var result=new CadCurveShape(shifted,shape.Closed).Validate();
        // Reject self-intersections rather than silently keep an ambiguous side.
        for(var i=0;i<shifted.Length;i++) for(var j=i+2;j<shifted.Length;j++)
            if(!(shape.Closed && i==0 && j==shifted.Length-1) && CadPlanarGeometry.Intersections(shifted[i],shifted[j]).Count>0)
                throw new InvalidOperationException("The offset would self-intersect; reduce the distance.");
        if(shape.Closed && (Math.Sign(SignedArea(source))!=Math.Sign(SignedArea(shifted)) ||
            source.Where((p,i)=>p.IsLine && (p.End-p.Start).Dot(shifted[i].End-shifted[i].Start)<=0).Any()))
            throw new InvalidOperationException("The offset distance collapses this closed path.");
        return new([], [new(entity.Id,result)]);
    }
    private static double SignedArea(IReadOnlyList<CadPlanarPrimitive> segments) => segments.Sum(p=>p.IsLine ?
        p.Start.X*p.End.Y-p.End.X*p.Start.Y : p.Center.X*(p.End.Y-p.Start.Y)-p.Center.Y*(p.End.X-p.Start.X)+p.Radius*p.Radius*p.Sweep)/2;
    private static CadPlanarPrimitive OffsetPrimitive(CadPlanarPrimitive p,double amount)
    {
        if(p.IsLine)
        { var normal=(p.End-p.Start).Normalize().Perpendicular()*amount; return CadPlanarPrimitive.Line(p.Start+normal,p.End+normal); }
        var radius=p.Radius-amount*Math.Sign(p.Sweep);
        if(radius<=CadGeometryTolerance.Absolute) throw new InvalidOperationException("The offset distance collapses the radius.");
        return CadPlanarPrimitive.Arc(p.Center,radius,p.StartAngle,p.Sweep);
    }
    public static CadCurveEditPlan Trim(CadEntity target, IEnumerable<CadEntity> boundaries, CadPointD pick)
    {
        var shape=CadCurveShape.From(target).Validate(); var cuts=new List<double>();
        var edges=boundaries.Where(e=>e.Id!=target.Id).SelectMany(CadPlanarCurves.Get).ToArray();
        for(var i=0;i<shape.Segments.Count;i++) foreach(var edge in edges)
            foreach(var point in CadPlanarGeometry.Intersections(shape.Segments[i],edge)) cuts.Add(i+shape.Segments[i].Parameter(point));
        var n=shape.Segments.Count; var position=NearestParameter(shape,pick);
        cuts=cuts.Select(t=>shape.Closed && t>=n-1e-9 ? 0 : t)
            .Where(t=>shape.Closed ? t>=0 && t<n : t>1e-9 && t<n-1e-9)
            .OrderBy(t=>t).DistinctBy(t=>Math.Round(t,9)).ToList();
        if(cuts.Count==0) throw new InvalidOperationException("No cutting boundary intersects this curve.");
        var lower=cuts.LastOrDefault(t=>t<position,0); var upper=cuts.FirstOrDefault(t=>t>position,n);
        if(shape.Closed)
        {
            if(cuts.Count<2) throw new InvalidOperationException("A closed curve requires two distinct cutting intersections.");
            lower=cuts.Where(t=>t<position).DefaultIfEmpty(cuts[^1]-n).Max();
            upper=cuts.Where(t=>t>position).DefaultIfEmpty(cuts[0]+n).Min();
            var remaining=SliceWrapped(shape,upper,lower+n);
            var removed=SliceWrapped(shape,lower<0 ? lower+n : lower,lower<0 ? upper+n : upper);
            return CadCurveEditPlan.Replace(target.Id,remaining) with { PreviewGeometry=[removed] };
        }
        var pieces=new List<CadCurveShape>();
        if(lower>1e-9) pieces.Add(Slice(shape,0,lower));
        if(upper<n-1e-9) pieces.Add(Slice(shape,upper,n));
        if(pieces.Count==0) throw new InvalidOperationException("The picked segment is not bounded by a cutting intersection.");
        return CadCurveEditPlan.Replace(target.Id,pieces.ToArray()) with { PreviewGeometry=[Slice(shape,lower,upper)] };
    }
    public static CadCurveEditPlan Extend(CadEntity target,IEnumerable<CadEntity> boundaries,CadPointD pick)
    {
        var shape=CadCurveShape.From(target).Validate();
        if(shape.Closed) throw new InvalidOperationException("A closed curve has no endpoint to extend.");
        var start=pick.DistanceTo(shape.Segments[0].Start)<pick.DistanceTo(shape.Segments[^1].End);
        var primitive=start ? shape.Segments[0] : shape.Segments[^1];
        var points=boundaries.Where(e=>e.Id!=target.Id).SelectMany(CadPlanarCurves.Get)
            .SelectMany(b=>CadPlanarGeometry.Intersections(primitive,b,true,false));
        CadPointD? candidate=null; double best=double.MaxValue;
        foreach(var p in points)
        {
            var t=primitive.Parameter(p);
            if(primitive.IsLine && (start ? t>=-1e-9 : t<=1+1e-9)) continue;
            if(!primitive.IsLine && primitive.Contains(p)) continue;
            var d=primitive.IsLine ? p.DistanceTo(start ? primitive.Start : primitive.End) :
                primitive.Radius*CadPlanarPrimitive.PositiveAngle(start ? (primitive.StartAngle-Math.Atan2(p.Y-primitive.Center.Y,p.X-primitive.Center.X))*Math.Sign(primitive.Sweep) : (Math.Atan2(p.Y-primitive.Center.Y,p.X-primitive.Center.X)-primitive.StartAngle-primitive.Sweep)*Math.Sign(primitive.Sweep));
            if(d<best && d>CadGeometryTolerance.Absolute) { best=d; candidate=p; }
        }
        if(candidate is not { } point) throw new InvalidOperationException("No boundary lies beyond the selected endpoint.");
        var updated=shape.Segments.ToArray();
        if(start) updated[0]=StartAt(primitive,point); else updated[^1]=EndAt(primitive,point);
        CadPlanarPrimitive added;
        if(primitive.IsLine) added=CadPlanarPrimitive.Line(start ? point : primitive.End,start ? primitive.Start : point);
        else
        {
            var angle=start ? updated[0].StartAngle : primitive.StartAngle+primitive.Sweep;
            var sweep=(start ? updated[0].Sweep : updated[^1].Sweep)-primitive.Sweep;
            added=CadPlanarPrimitive.Arc(primitive.Center,primitive.Radius,angle,sweep);
        }
        return CadCurveEditPlan.Replace(target.Id,new CadCurveShape(updated).Validate()) with { PreviewGeometry=[new([added])] };
    }
    public static CadCurveEditPlan Break(CadEntity target,CadPointD first,CadPointD? second=null)
    {
        var shape=CadCurveShape.From(target).Validate(); var a=NearestParameter(shape,first); var n=shape.Segments.Count;
        if(second is null)
        {
            if(shape.Closed) throw new InvalidOperationException("Choose two distinct points to break a closed curve.");
            if(a<=1e-9 || a>=n-1e-9) throw new InvalidOperationException("The break point must be inside the curve.");
            return CadCurveEditPlan.Replace(target.Id,Slice(shape,0,a),Slice(shape,a,n));
        }
        var b=NearestParameter(shape,second.Value);
        if(Math.Abs(a-b)<1e-9) throw new InvalidOperationException("The two break points must be different.");
        if(a>b) (a,b)=(b,a);
        if(shape.Closed) return CadCurveEditPlan.Replace(target.Id,SliceWrapped(shape,b,a+n));
        var result=new List<CadCurveShape>();
        if(a>1e-9) result.Add(Slice(shape,0,a)); if(b<n-1e-9) result.Add(Slice(shape,b,n));
        if(result.Count==0) throw new InvalidOperationException("Breaking here would remove the entire curve.");
        return CadCurveEditPlan.Replace(target.Id,result.ToArray());
    }
    public static CadCurveEditPlan Join(IReadOnlyList<CadEntity> entities)
    {
        if(entities.Count<2) throw new InvalidOperationException("Select at least two open curves.");
        var first=entities[0];
        if(entities.Any(e=>e.OwnerBlockId!=first.OwnerBlockId || e.LayerId!=first.LayerId || e.StrokeStyle!=first.StrokeStyle || GraphicStyle(e)!=GraphicStyle(first) ||
            e.LineWeight!=first.LineWeight || e.UseLayerLineWeight!=first.UseLayerLineWeight || e.ColorSource!=first.ColorSource || e.ZIndex!=first.ZIndex || e.IsVisible!=first.IsVisible))
            throw new InvalidOperationException("Joined curves must share an owner, layer and stroke appearance.");
        var remaining=entities.Select(CadCurveShape.From).ToList();
        if(remaining.Any(s=>s.Closed)) throw new InvalidOperationException("Join requires open line/arc paths.");
        var segments=remaining[0].Segments.ToList(); remaining.RemoveAt(0);
        while(remaining.Count>0)
        {
            var matches=new List<(int Index,bool Prepend,bool Reverse)>();
            for(var i=0;i<remaining.Count;i++)
            {
                var s=remaining[i];
                if(CadGeometryTolerance.Coincident(segments[^1].End,s.Segments[0].Start)) matches.Add((i,false,false));
                if(CadGeometryTolerance.Coincident(segments[^1].End,s.Segments[^1].End)) matches.Add((i,false,true));
                if(CadGeometryTolerance.Coincident(segments[0].Start,s.Segments[^1].End)) matches.Add((i,true,false));
                if(CadGeometryTolerance.Coincident(segments[0].Start,s.Segments[0].Start)) matches.Add((i,true,true));
            }
            if(matches.Count==0) throw new InvalidOperationException("Selected curves do not form a connected chain.");
            if(matches.GroupBy(m=>m.Prepend).Any(g=>g.Select(m=>m.Index).Distinct().Count()>1)) throw new InvalidOperationException("The selected chain branches; select one unambiguous chain.");
            var match=matches[0]; var addition=remaining[match.Index].Segments;
            if(match.Reverse) addition=addition.Reverse().Select(Reverse).ToArray();
            if(match.Prepend) segments.InsertRange(0,addition); else segments.AddRange(addition);
            remaining.RemoveAt(match.Index);
        }
        var closed=CadGeometryTolerance.Coincident(segments[0].Start,segments[^1].End);
        return new(entities.Select(e=>new CadCurveReplacement(e.Id,[])).ToArray(),[new(first.Id,new CadCurveShape(segments,closed).Validate())]);
    }
    public static CadCurveEditPlan Corner(CadEntity first,CadEntity second,CadPointD firstPick,CadPointD secondPick,double size,double secondDistance,bool fillet,bool trim=true)
    {
        if(first.Id==second.Id || CadCurveShape.From(first).Segments.Count>1 || CadCurveShape.From(second).Segments.Count>1)
            return PathCorner(first,second,firstPick,secondPick,size,secondDistance,fillet,trim);
        if(first is not CadLine || second is not CadLine)
            return CurvedCorner(first,second,firstPick,secondPick,size,secondDistance,fillet,trim);
        var a=(CadLine)first; var b=(CadLine)second;
        if(first.OwnerBlockId!=second.OwnerBlockId) throw new InvalidOperationException("The two lines must share an owner space.");
        if(!double.IsFinite(size) || size<=CadGeometryTolerance.Absolute || !double.IsFinite(secondDistance) || secondDistance<=CadGeometryTolerance.Absolute) throw new ArgumentOutOfRangeException(nameof(size));
        var intersections=CadPlanarGeometry.Intersections(CadPlanarPrimitive.Line(a.Start,a.End),CadPlanarPrimitive.Line(b.Start,b.End),true,true);
        if(intersections.Count==0) throw new InvalidOperationException("Parallel lines cannot form this corner.");
        var intersection=intersections[0];
        var endA=a.Start.DistanceTo(firstPick)<a.End.DistanceTo(firstPick) ? a.Start : a.End;
        var endB=b.Start.DistanceTo(secondPick)<b.End.DistanceTo(secondPick) ? b.Start : b.End;
        var u=(endA-intersection).Normalize(); var v=(endB-intersection).Normalize();
        var angle=Math.Acos(Math.Clamp(u.Dot(v),-1,1));
        if(angle<1e-8 || Math.PI-angle<1e-8) throw new InvalidOperationException("The selected directions do not form a valid corner.");
        var da=fillet ? size/Math.Tan(angle/2) : size; var db=fillet ? da : secondDistance;
        if(da>=endA.DistanceTo(intersection)-CadGeometryTolerance.Absolute || db>=endB.DistanceTo(intersection)-CadGeometryTolerance.Absolute)
            throw new InvalidOperationException("The corner size exceeds the selected line lengths.");
        var pa=intersection+u*da; var pb=intersection+v*db;
        CadPlanarPrimitive connector;
        if(fillet)
        {
            var center=intersection+(u+v).Normalize()*(size/Math.Sin(angle/2));
            var start=Math.Atan2(pa.Y-center.Y,pa.X-center.X); var end=Math.Atan2(pb.Y-center.Y,pb.X-center.X);
            var sweep=CadPlanarPrimitive.PositiveAngle(end-start); if(sweep>Math.PI) sweep-=Math.PI*2;
            connector=CadPlanarPrimitive.Arc(center,size,start,sweep);
        }
        else connector=CadPlanarPrimitive.Line(pa,pb);
        var replacements=trim ? new[]{new CadCurveReplacement(first.Id,[new([CadPlanarPrimitive.Line(endA,pa)])]),new CadCurveReplacement(second.Id,[new([CadPlanarPrimitive.Line(pb,endB)])])} : [];
        return new(replacements,[new(first.Id,new([connector]))]);
    }
    private static CadCurveEditPlan CurvedCorner(CadEntity first,CadEntity second,CadPointD pickA,CadPointD pickB,double size,double secondDistance,bool fillet,bool trim)
    {
        if(first.OwnerBlockId!=second.OwnerBlockId) throw new InvalidOperationException("The curves must share an owner space.");
        if(!double.IsFinite(size) || size<=CadGeometryTolerance.Absolute || !double.IsFinite(secondDistance) || secondDistance<=CadGeometryTolerance.Absolute) throw new ArgumentOutOfRangeException(nameof(size));
        var shapeA=CadCurveShape.From(first).Validate(); var shapeB=CadCurveShape.From(second).Validate();
        if(shapeA.Segments.Count!=1 || shapeB.Segments.Count!=1)
            throw new NotSupportedException("Select individual lines or circular arcs for a curve corner.");
        if(trim && (shapeA.Closed || shapeB.Closed))
            throw new InvalidOperationException("Disable trimming to connect a full circle, or break it into an arc first.");
        var a=shapeA.Segments[0]; var b=shapeB.Segments[0];
        CadPointD pa,pb,center=default;
        if(fillet)
        {
            // Fillet centres lie on exact parallel lines or concentric circles.
            var candidates=new List<(CadPointD Center,CadPointD A,CadPointD B)>();
            foreach(var la in Loci(a,size)) foreach(var lb in Loci(b,size))
                foreach(var c in CadPlanarGeometry.Intersections(la,lb,true,true))
                {
                    var ta=TangentPoint(a,c); var tb=TangentPoint(b,c);
                    if(a.Contains(ta) && b.Contains(tb) && Math.Abs(c.DistanceTo(ta)-size)<=CadGeometryTolerance.For(size)*10 &&
                        Math.Abs(c.DistanceTo(tb)-size)<=CadGeometryTolerance.For(size)*10 && !CadGeometryTolerance.Coincident(ta,tb))
                        candidates.Add((c,ta,tb));
                }
            if(candidates.Count==0) throw new InvalidOperationException("No tangent fillet fits these curves; reduce the radius or change the picked sides.");
            var best=candidates.OrderBy(c=>c.A.DistanceTo(pickA)+c.B.DistanceTo(pickB)).ThenBy(c=>c.Center.X).ThenBy(c=>c.Center.Y).First();
            (center,pa,pb)=best;
        }
        else
        {
            var intersections=CadPlanarGeometry.Intersections(a,b,a.IsLine,b.IsLine);
            if(intersections.Count==0) throw new InvalidOperationException("These curves have no corner intersection.");
            var corner=intersections.OrderBy(p=>p.DistanceTo(pickA)+p.DistanceTo(pickB)).First();
            pa=ChamferPoint(a,corner,pickA,size); pb=ChamferPoint(b,corner,pickB,secondDistance);
        }
        CadPlanarPrimitive connector;
        if(fillet)
        {
            var angle=Math.Atan2(pa.Y-center.Y,pa.X-center.X);
            var sweep=CadPlanarPrimitive.PositiveAngle(Math.Atan2(pb.Y-center.Y,pb.X-center.X)-angle);
            if(sweep>Math.PI) sweep-=Math.PI*2;
            connector=CadPlanarPrimitive.Arc(center,size,angle,sweep);
        }
        else connector=CadPlanarPrimitive.Line(pa,pb);
        var replacements=trim ? new[]{new CadCurveReplacement(first.Id,[KeepPickedEnd(a,pa,pickA)]),new CadCurveReplacement(second.Id,[KeepPickedEnd(b,pb,pickB)])} : [];
        return new(replacements,[new(first.Id,new CadCurveShape([connector]).Validate())]);
    }
    private static IEnumerable<CadPlanarPrimitive> Loci(CadPlanarPrimitive p,double radius)
    {
        if(p.IsLine) { yield return OffsetPrimitive(p,radius); yield return OffsetPrimitive(p,-radius); }
        else
        {
            yield return CadPlanarPrimitive.Arc(p.Center,p.Radius+radius,0,Math.PI*2);
            if(p.Radius>radius+CadGeometryTolerance.Absolute) yield return CadPlanarPrimitive.Arc(p.Center,p.Radius-radius,0,Math.PI*2);
        }
    }
    private static CadPointD TangentPoint(CadPlanarPrimitive p,CadPointD center) => p.IsLine ? p.At(p.Parameter(center)) : p.Center+(center-p.Center).Normalize()*p.Radius;
    private static CadPointD ChamferPoint(CadPlanarPrimitive p,CadPointD intersection,CadPointD pick,double distance)
    {
        var t=p.Parameter(intersection); var retained=pick.DistanceTo(p.Start)<pick.DistanceTo(p.End) ? 0.0 : 1.0;
        var length=p.IsLine ? p.Start.DistanceTo(p.End) : p.Radius*Math.Abs(p.Sweep);
        var cut=t+Math.Sign(retained-t)*distance/length;
        if(cut<=1e-10 || cut>=1-1e-10 || Math.Abs(retained-t)*length<=distance+CadGeometryTolerance.Absolute)
            throw new InvalidOperationException("The chamfer distance exceeds the selected curve length.");
        return p.At(cut);
    }
    private static CadCurveShape KeepPickedEnd(CadPlanarPrimitive p,CadPointD tangent,CadPointD pick)
    {
        var t=p.Parameter(tangent); var shape=new CadCurveShape([p]);
        return pick.DistanceTo(p.Start)<pick.DistanceTo(p.End) ? Slice(shape,0,t) : Slice(shape,t,1);
    }
    public static StyleId? GraphicStyle(CadEntity entity) => entity switch
    { CadRegion e=>e.GraphicStyleId,CadLine e=>e.GraphicStyleId,CadCircle e=>e.GraphicStyleId,CadArc e=>e.GraphicStyleId,CadPolyline e=>e.GraphicStyleId,CadCompositePath e=>e.GraphicStyleId,CadRectangle e=>e.GraphicStyleId,_=>null };
    private static double NearestParameter(CadCurveShape shape,CadPointD pick)
    {
        var best=double.MaxValue; var parameter=0.0;
        for(var i=0;i<shape.Segments.Count;i++)
        {
            var p=shape.Segments[i]; var t=Math.Clamp(p.Parameter(pick),0,1);
            if(!p.IsLine && !p.Contains(pick)) t=p.Start.DistanceTo(pick)<p.End.DistanceTo(pick) ? 0 : 1;
            var d=p.At(t).DistanceTo(pick); if(d<best) { best=d; parameter=i+t; }
        }
        return parameter;
    }
    private static CadPlanarPrimitive Reverse(CadPlanarPrimitive p) => p.IsLine ? CadPlanarPrimitive.Line(p.End,p.Start) : CadPlanarPrimitive.Arc(p.Center,p.Radius,p.StartAngle+p.Sweep,-p.Sweep);
    private static CadPlanarPrimitive EndAt(CadPlanarPrimitive p,CadPointD end)
    {
        if(p.IsLine) return CadPlanarPrimitive.Line(p.Start,end);
        var angle=Math.Atan2(end.Y-p.Center.Y,end.X-p.Center.X);
        var sweep=CadPlanarPrimitive.PositiveAngle((angle-p.StartAngle)*Math.Sign(p.Sweep))*Math.Sign(p.Sweep);
        return CadPlanarPrimitive.Arc(p.Center,p.Radius,p.StartAngle,sweep);
    }
    private static CadPlanarPrimitive StartAt(CadPlanarPrimitive p,CadPointD start) => Reverse(EndAt(Reverse(p),start));
    private static CadCurveShape Slice(CadCurveShape shape,double start,double end)
    {
        var result=new List<CadPlanarPrimitive>();
        for(var i=0;i<shape.Segments.Count;i++)
        {
            var a=Math.Max(0,start-i); var b=Math.Min(1,end-i); if(b-a<=1e-9) continue;
            var p=shape.Segments[i]; result.Add(p.IsLine ? CadPlanarPrimitive.Line(p.At(a),p.At(b)) : CadPlanarPrimitive.Arc(p.Center,p.Radius,p.StartAngle+p.Sweep*a,p.Sweep*(b-a)));
        }
        return new CadCurveShape(result).Validate();
    }
    private static CadCurveShape SliceWrapped(CadCurveShape shape,double start,double end)
    {
        var n=shape.Segments.Count;
        while(start>=n) { start-=n; end-=n; }
        // Crossing the parameter seam does not split one circle into two entities.
        if(n==1 && !shape.Segments[0].IsLine)
        {
            var p=shape.Segments[0];
            return new CadCurveShape([CadPlanarPrimitive.Arc(p.Center,p.Radius,p.StartAngle+p.Sweep*start,p.Sweep*(end-start))]).Validate();
        }
        if(end<=n) return Slice(shape,start,end);
        if(end-n<=1e-9) return Slice(shape,start,n);
        var segments=Slice(shape,start,n).Segments.Concat(Slice(shape,0,end-n).Segments).ToArray();
        return new CadCurveShape(segments).Validate();
    }
}
