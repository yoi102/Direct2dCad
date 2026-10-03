using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Db.Geometry;

public sealed record CadCurveTessellationResult(IReadOnlyList<CadPointD> Points,double ErrorBound,bool ReachedBudget);

/// <summary>Adaptive world-space flattening. The curve/chord distance is bounded by the
/// Bezier control hull or the parametric second derivative, including at budget exhaustion.</summary>
public static class CadCurveTessellation
{
    public static CadCurveTessellationResult Create(Curve curve,double error=1e-4,CancellationToken token=default)
    {
        if(!double.IsFinite(error) || error<=0) throw new ArgumentOutOfRangeException(nameof(error));
        var points=new List<CadPointD>();var maximum=0.0;var exhausted=false;var nodes=0;
        void Point(CadPointD p) { if(points.Count==0 || points[^1]!=p) points.Add(p); }
        bool Stop(double bound,int depth)
        {
            token.ThrowIfCancellationRequested();nodes++;
            if(bound<=error || depth>=24 || nodes>=65536) {maximum=Math.Max(maximum,bound);exhausted|=bound>error;return true;} return false;
        }
        static double Distance(CadPointD p,CadPointD a,CadPointD b)
        {var u=b-a;var t=u.LengthSquared==0 ? 0 : Math.Clamp((p-a).Dot(u)/u.LengthSquared,0,1);return p.DistanceTo(a+u*t);}
        void Bezier(CadBezierSegmentD b,int depth=0)
        {
            var bound=Math.Max(Distance(b.Control1,b.Start,b.End),Distance(b.Control2,b.Start,b.End));
            if(Stop(bound,depth)) {Point(b.Start);Point(b.End);return;}
            static CadPointD Mid(CadPointD a,CadPointD c)=>new((a.X+c.X)/2,(a.Y+c.Y)/2);
            var a=Mid(b.Start,b.Control1);var c=Mid(b.Control1,b.Control2);var d=Mid(b.Control2,b.End);var e=Mid(a,c);var f=Mid(c,d);var m=Mid(e,f);
            Bezier(new(b.Start,a,e,m),depth+1);Bezier(new(m,f,d,b.End),depth+1);
        }
        void Ellipse(CadPointD center,double rx,double ry,double start,double sweep,CadMatrixD transform)
        {
            CadPointD At(double a)=>transform.TransformPoint(new(center.X+rx*Math.Cos(a),center.Y+ry*Math.Sin(a)));
            void Arc(double a,double b,int depth)
            {
                var bound=Math.Max(rx,ry)*(b-a)*(b-a)/8;
                if(Stop(bound,depth)) {Point(At(a));Point(At(b));return;}
                var middle=(a+b)/2;Arc(a,middle,depth+1);Arc(middle,b,depth+1);
            }
            var count=Math.Max(1,(int)Math.Ceiling(Math.Abs(sweep)/(Math.PI/2)));
            for(var i=0;i<count;i++) Arc(start+sweep*i/count,start+sweep*(i+1)/count,0);
        }
        switch(curve)
        {
            case CadSpline spline:foreach(var bezier in spline.GetBezierSegments()) Bezier(bezier);break;
            case CadEllipse e:Ellipse(e.Center,e.RadiusX,e.RadiusY,0,Math.PI*2,e.GeometryTransform);break;
            case CadEllipseArc e:Ellipse(e.Center,e.RadiusX,e.RadiusY,e.StartAngleRadians,e.SweepAngleRadians,e.GeometryTransform);break;
            case CadRectangle r when r.HasRoundedCorners:
                var b=r.FrameBounds;var rx=r.CornerRadiusX;var ry=r.CornerRadiusY;
                Ellipse(new(b.MaxX-rx,b.MaxY-ry),rx,ry,0,Math.PI/2,r.GeometryTransform);
                Ellipse(new(b.MinX+rx,b.MaxY-ry),rx,ry,Math.PI/2,Math.PI/2,r.GeometryTransform);
                Ellipse(new(b.MinX+rx,b.MinY+ry),rx,ry,Math.PI,Math.PI/2,r.GeometryTransform);
                Ellipse(new(b.MaxX-rx,b.MinY+ry),rx,ry,Math.PI*1.5,Math.PI/2,r.GeometryTransform);break;
            case CadCompositePath path:
                var current=path.StartPoint;Point(current);
                foreach(var segment in path.Segments)
                {
                    switch(segment)
                    {
                        case CadCompositeLineSegment l:Point(l.End);break;
                        case CadCompositeArcSegment a:Ellipse(a.Center,current.DistanceTo(a.Center),current.DistanceTo(a.Center),Math.Atan2(current.Y-a.Center.Y,current.X-a.Center.X),a.SweepAngleRadians,CadMatrixD.Identity);break;
                        case CadCompositeSplineSegment s:foreach(var bezier in CadSpline.CreateBezierSegments(new[]{current}.Concat(s.FitPoints).ToArray())) Bezier(bezier);break;
                    }
                    current=CadCompositePath.GetEndPoint(current,segment);
                }
                break;
            default:
                foreach(var p in CadPlanarCurves.Get(curve))
                {if(p.IsLine) {Point(p.Start);Point(p.End);} else Ellipse(p.Center,p.Radius,p.Radius,p.StartAngle,p.Sweep,CadMatrixD.Identity);}break;
        }
        if(curve.IsClosed && points.Count>0) Point(points[0]);
        return new(points,maximum,exhausted);
    }
}
