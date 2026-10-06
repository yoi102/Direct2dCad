using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Db.Geometry;

public readonly record struct CadCurveMeasurement(double Length,double LengthErrorEstimate,double? Area,bool Approximate,bool ReachedBudget=false);

public static class CadCurveMeasurements
{
    public const double DefaultError = 1e-6;
    public static CadCurveMeasurement Measure(Curve curve,double error=DefaultError,CancellationToken token=default)
    {
        if(!double.IsFinite(error) || error<=0) throw new ArgumentOutOfRangeException(nameof(error));
        token.ThrowIfCancellationRequested();
        switch(curve)
        {
            case CadRegion region:
                var ellipticalCount = region.Contours.Sum(contour => contour.Edges.Count(edge => edge.IsEllipse));
                if (ellipticalCount == 0) return new(region.Length, 0, region.Area, false);
                var regionLength = 0.0; var regionError = 0.0; var regionBudget = false;
                foreach (var edge in region.Contours.SelectMany(contour => contour.Edges))
                {
                    token.ThrowIfCancellationRequested();
                    if (!edge.IsEllipse) { regionLength += edge.Length; continue; }
                    var measured = EllipseLength(edge.RadiusX, edge.RadiusY, edge.StartAngle, edge.Sweep, error / ellipticalCount, token);
                    regionLength += measured.Value; regionError += measured.Error; regionBudget |= measured.Budget;
                }
                return new(regionLength, regionError, region.Area, true, regionBudget);
            case CadLine line: return new(line.Start.DistanceTo(line.End),0,null,false);
            case CadCircle circle: return new(2*Math.PI*circle.Radius,0,Math.PI*circle.Radius*circle.Radius,false);
            case CadArc arc: return new(arc.Radius*Math.Abs(arc.SweepAngleRadians),0,arc.IsFullCircle ? Math.PI*arc.Radius*arc.Radius : null,false);
            case CadEllipse ellipse:
                var el=EllipseLength(ellipse.RadiusX,ellipse.RadiusY,0,Math.PI*2,error,token);
                return new(el.Value,el.Error,Math.PI*ellipse.RadiusX*ellipse.RadiusY,true,el.Budget);
            case CadEllipseArc arc:
                var ea=EllipseLength(arc.RadiusX,arc.RadiusY,arc.StartAngleRadians,arc.SweepAngleRadians,error,token);
                return new(ea.Value,ea.Error,null,true,ea.Budget);
            case CadRectangle rectangle:
                var b=rectangle.FrameBounds;
                if(!rectangle.HasRoundedCorners) return new(2*(b.Width+b.Height),0,b.Width*b.Height,false);
                var corner=EllipseLength(rectangle.CornerRadiusX,rectangle.CornerRadiusY,0,Math.PI*2,error,token);
                return new(2*(b.Width-2*rectangle.CornerRadiusX+b.Height-2*rectangle.CornerRadiusY)+corner.Value,corner.Error,
                    b.Width*b.Height-(4-Math.PI)*rectangle.CornerRadiusX*rectangle.CornerRadiusY,true,corner.Budget);
            case CadPolyline polyline:
                var length=0.0;var area=0.0;
                for(var i=0;i<polyline.Points.Count-(polyline.Closed ? 0 : 1);i++)
                { token.ThrowIfCancellationRequested();var a=polyline.Points[i];var next=polyline.Points[(i+1)%polyline.Points.Count];length+=a.DistanceTo(next);area+=(a.X*next.Y-next.X*a.Y)/2; }
                return new(length,0,polyline.Closed ? Math.Abs(area) : null,false);
            case CadSpline spline: return Beziers(spline.GetBezierSegments(),spline.Closed,error,token);
            case CadCompositePath path:
                var current=path.StartPoint;var total=0.0;var estimate=0.0;var signedArea=0.0;var approximate=false;var budget=false;
                foreach(var segment in path.Segments)
                {
                    token.ThrowIfCancellationRequested();var end=CadCompositePath.GetEndPoint(current,segment);
                    switch(segment)
                    {
                        case CadCompositeLineSegment: total+=current.DistanceTo(end); signedArea+=(current.X*end.Y-end.X*current.Y)/2; break;
                        case CadCompositeArcSegment arc:
                            var radius=current.DistanceTo(arc.Center);total+=radius*Math.Abs(arc.SweepAngleRadians);
                            signedArea+=(arc.Center.X*(end.Y-current.Y)-arc.Center.Y*(end.X-current.X)+radius*radius*arc.SweepAngleRadians)/2;break;
                        case CadCompositeSplineSegment spline:
                            var m=Beziers(CadSpline.CreateBezierSegments(new[]{current}.Concat(spline.FitPoints).ToArray()),false,error/path.Segments.Count,token);
                            total+=m.Length;estimate+=m.LengthErrorEstimate;approximate=true;budget|=m.ReachedBudget;
                            foreach(var bezier in CadSpline.CreateBezierSegments(new[]{current}.Concat(spline.FitPoints).ToArray())) signedArea+=BezierArea(bezier);
                            break;
                    }
                    current=end;
                }
                if(path.Closed && current!=path.StartPoint) { total+=current.DistanceTo(path.StartPoint);signedArea+=(current.X*path.StartPoint.Y-path.StartPoint.X*current.Y)/2; }
                return new(total,estimate,path.Closed ? Math.Abs(signedArea) : null,approximate,budget);
            default: throw new NotSupportedException("No curve measurement is available for this entity.");
        }
    }
    private static CadCurveMeasurement Beziers(IReadOnlyList<CadBezierSegmentD> segments,bool closed,double error,CancellationToken token)
    {
        var total=0.0;var estimate=0.0;var area=0.0;var budget=false;var nodes=0;
        (double Value,double Error) Length(CadBezierSegmentD b,double tolerance,int depth)
        {
            if((++nodes & 255)==0) token.ThrowIfCancellationRequested();
            var chord=b.Start.DistanceTo(b.End);var polygon=b.Start.DistanceTo(b.Control1)+b.Control1.DistanceTo(b.Control2)+b.Control2.DistanceTo(b.End);
            if(polygon-chord<=2*tolerance || depth>=24 || nodes>=1000000)
            { budget|=polygon-chord>2*tolerance;return ((polygon+chord)/2,(polygon-chord)/2); }
            static CadPointD Mid(CadPointD a,CadPointD c) => new((a.X+c.X)/2,(a.Y+c.Y)/2);
            var a=Mid(b.Start,b.Control1);var c=Mid(b.Control1,b.Control2);var d=Mid(b.Control2,b.End);var e=Mid(a,c);var f=Mid(c,d);var m=Mid(e,f);
            var left=Length(new(b.Start,a,e,m),tolerance/2,depth+1);var right=Length(new(m,f,d,b.End),tolerance/2,depth+1);
            return (left.Value+right.Value,left.Error+right.Error);
        }
        foreach(var bezier in segments) { var l=Length(bezier,error/Math.Max(1,segments.Count),0);total+=l.Value;estimate+=l.Error;area+=BezierArea(bezier); }
        return new(total,estimate,closed ? Math.Abs(area) : null,true,budget);
    }
    // Four-point Gauss quadrature integrates the degree-five polynomial x*y'-y*x' exactly.
    private static double BezierArea(CadBezierSegmentD b)
    {
        var sum=0.0;double[] nodes=[-.8611363115940526,-.3399810435848563,.3399810435848563,.8611363115940526];double[] weights=[.3478548451374538,.6521451548625461,.6521451548625461,.3478548451374538];
        for(var i=0;i<4;i++)
        { var t=(nodes[i]+1)/2;var u=1-t;var p=b.Evaluate(t);var derivative=(b.Control1-b.Start)*(3*u*u)+(b.Control2-b.Control1)*(6*u*t)+(b.End-b.Control2)*(3*t*t);sum+=weights[i]*(p.X*derivative.Y-p.Y*derivative.X)/4; }
        return sum;
    }
    internal static (double Value,double Error,bool Budget) EllipseLength(double rx,double ry,double start,double sweep,double error,CancellationToken token)
    {
        double Speed(double t) => Math.Sqrt(rx*rx*Math.Sin(t)*Math.Sin(t)+ry*ry*Math.Cos(t)*Math.Cos(t));
        var budget=false;var nodes=0;
        (double V,double E) Integrate(double a,double b,double fa,double fm,double fb,double whole,double tolerance,int depth)
        {
            if((++nodes & 255)==0) token.ThrowIfCancellationRequested();var m=(a+b)/2;var fl=Speed((a+m)/2);var fr=Speed((m+b)/2);
            var left=(m-a)*(fa+4*fl+fm)/6;var right=(b-m)*(fm+4*fr+fb)/6;var difference=left+right-whole;
            if(Math.Abs(difference)<=15*tolerance || depth>=24 || nodes>=1000000)
            { budget|=Math.Abs(difference)>15*tolerance;return (left+right+difference/15,Math.Abs(difference)/15); }
            var l=Integrate(a,m,fa,fl,fm,left,tolerance/2,depth+1);var r=Integrate(m,b,fm,fr,fb,right,tolerance/2,depth+1);return (l.V+r.V,l.E+r.E);
        }
        var total=0.0;var estimate=0.0;var parts=Math.Max(1,(int)Math.Ceiling(Math.Abs(sweep)/(Math.PI/4)));
        for(var i=0;i<parts;i++) { token.ThrowIfCancellationRequested();var a=start+sweep*i/parts;var b=start+sweep*(i+1)/parts;if(a>b)(a,b)=(b,a);var fa=Speed(a);var fb=Speed(b);var fm=Speed((a+b)/2);var r=Integrate(a,b,fa,fm,fb,(b-a)*(fa+4*fm+fb)/6,error/parts,0);total+=r.V;estimate+=r.E; }
        return (total,estimate,budget);
    }
}
