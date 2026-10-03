using System.Globalization;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public enum CadDimensionKind { LinearX, LinearY, Aligned, Radius, Diameter, Angular, Leader }
public enum CadReferenceFeature { LineStart, LineEnd, CircleCenter, CirclePoint, ArcStart, ArcEnd, PolylineVertex }
public enum CadAssociationState { Detached, Valid, Broken }
public enum CadDimensionArrow { Open, Closed, Slash }

public sealed record CadGeometryReference(long EntityId, long OwnerBlockId, CadReferenceFeature Feature,
    double Parameter = 0, int TopologyCount = 0)
{
    public bool TryResolve(CadDocument document, out CadPointD point)
    {
        point = default;
        if (!document.TryGetEntity(new EntityId(EntityId), out var entity) ||
            entity is null || entity.IsErased || entity.OwnerBlockId.Value != OwnerBlockId) return false;
        switch (entity, Feature)
        {
            case (CadLine line, CadReferenceFeature.LineStart): point = line.Start; return true;
            case (CadLine line, CadReferenceFeature.LineEnd): point = line.End; return true;
            case (CadCircle circle, CadReferenceFeature.CircleCenter): point = circle.Center; return true;
            case (CadCircle circle, CadReferenceFeature.CirclePoint):
                point = circle.Center + new CadVectorD(Math.Cos(Parameter) * circle.Radius, Math.Sin(Parameter) * circle.Radius); return true;
            case (CadArc arc, CadReferenceFeature.CircleCenter): point = arc.Center; return true;
            case (CadArc arc, CadReferenceFeature.CirclePoint):
                point = arc.Center + new CadVectorD(Math.Cos(Parameter) * arc.Radius, Math.Sin(Parameter) * arc.Radius); return true;
            case (CadArc arc, CadReferenceFeature.ArcStart): point = arc.StartPoint; return true;
            case (CadArc arc, CadReferenceFeature.ArcEnd): point = arc.EndPoint; return true;
            case (CadPolyline polyline, CadReferenceFeature.PolylineVertex)
                when polyline.Points.Count == TopologyCount && Parameter >= 0 && Parameter < TopologyCount && Parameter == (int)Parameter:
                point = polyline.Points[(int)Parameter]; return true;
            default: return false;
        }
    }
}

public sealed record CadDimensionAnchor(CadPointD Point, CadGeometryReference? Reference = null);
public sealed record CadDimensionStyle(string Name = "ISO", CadUnit Unit = CadUnit.Millimeter, int Precision = 2,
    double TextHeight = 2.5, double ArrowSize = 2.5, double ExtensionGap = 1,
    double ExtensionBeyond = 1.5, double LineWeight = 0.18, string ShapeFont = "unicode", CadDimensionArrow Arrow = CadDimensionArrow.Open)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || !Enum.IsDefined(Arrow) || !CadShapeFontRegistry.Defaults.Any(f=>f.Id.Value==ShapeFont) || !Enum.IsDefined(Unit) || Precision is < 0 or > 8 ||
            !double.IsFinite(TextHeight) || TextHeight <= 0 || !double.IsFinite(ArrowSize) || ArrowSize <= 0 ||
            !double.IsFinite(ExtensionGap) || ExtensionGap < 0 || !double.IsFinite(ExtensionBeyond) || ExtensionBeyond < 0 ||
            !double.IsFinite(LineWeight) || LineWeight <= 0) throw new ArgumentException("Invalid dimension style.");
    }
}

public sealed record CadDimensionDefinition(CadDimensionKind Kind, CadDimensionAnchor[] Anchors, CadPointD Placement,
    CadDimensionStyle Style, double AnnotationScale = 1, string? TextOverride = null, double LinearRotationRadians = 0)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Style);
        Style.Validate();
        var count = Kind == CadDimensionKind.Angular ? 3 : 2;
        if (!Enum.IsDefined(Kind) || Anchors is null || Anchors.Length != count ||
            !double.IsFinite(AnnotationScale) || AnnotationScale <= 0 || AnnotationScale > 1e6 ||
            !double.IsFinite(LinearRotationRadians) || !Finite(Placement) || Anchors.Any(a => a is null || !Finite(a.Point) ||
                a.Reference is { } r && (r.EntityId <= 0 || !Enum.IsDefined(r.Feature) || !double.IsFinite(r.Parameter))) || (TextOverride?.Length ?? 0) > 4096)
            throw new ArgumentException("Invalid dimension geometry.");
        if (Anchors[0].Point.DistanceTo(Anchors[1].Point) < 1e-9) throw new ArgumentException("Dimension baseline cannot be zero.");
        if (Kind == CadDimensionKind.Angular && Anchors[0].Point.DistanceTo(Anchors[2].Point) < 1e-9)
            throw new ArgumentException("Angular dimension rays cannot be zero.");
    }
    private static bool Finite(CadPointD point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    public CadDimensionDefinition Copy() => this with { Anchors = Anchors.ToArray() };
}

/// <summary>A single selectable annotation; strokes are derived, never separate database entities.</summary>
public sealed class CadDimension : CadEntity
{
    private CadStrokeTextSegment[] _strokes = [];
    private CadDimensionDefinition _definition = null!;
    public CadDimensionDefinition Definition { get => _definition.Copy(); private set => _definition = value.Copy(); }
    public CadAssociationState AssociationState { get; private set; }
    public double Measurement { get; private set; }
    public string DisplayText { get; private set; } = "";
    public bool HasTextOverride => !string.IsNullOrEmpty(Definition.TextOverride);
    public IReadOnlyList<CadStrokeTextSegment> Strokes => _strokes;
    private CadRectD _bounds = CadRectD.Empty;
    public override CadRectD Bounds => _bounds;

    internal CadDimension(EntityId id, LayerId layer, BlockId owner, CadDimensionDefinition definition, string name = "")
        : base(id, layer, owner, name)
    {
        definition.Validate(); Definition = definition.Copy();
        AssociationState = Definition.Anchors.Any(a => a.Reference is not null) ? CadAssociationState.Valid : CadAssociationState.Detached;
        Rebuild();
        SetLineWeight(new CadLineWeight(definition.Style.LineWeight));
    }

    public void SetDefinition(CadDimensionDefinition definition)
    {
        definition.Validate(); Definition = definition.Copy();
        AssociationState = Definition.Anchors.Any(a => a.Reference is not null) ? CadAssociationState.Valid : CadAssociationState.Detached;
        Rebuild();
        SetLineWeight(new CadLineWeight(definition.Style.LineWeight));
    }

    public bool RefreshAssociation(CadDocument document)
    {
        var state = Definition.Anchors.Any(a => a.Reference is not null) ? CadAssociationState.Valid : CadAssociationState.Detached;
        var points = Definition.Anchors.ToArray();
        for (var i = 0; i < points.Length; i++)
        {
            if (points[i].Reference is not { } reference) continue;
            if (reference.OwnerBlockId != OwnerBlockId.Value || !reference.TryResolve(document, out var point))
            { state = CadAssociationState.Broken; continue; }
            points[i] = points[i] with { Point = point };
        }
        if (state != CadAssociationState.Broken)
        {
            try { (Definition with { Anchors = points }).Validate(); }
            catch (ArgumentException) { state = CadAssociationState.Broken; }
        }
        // An invalid mapping preserves the complete last accepted annotation rather than mixing old and new points.
        var changed = AssociationState != state || (state != CadAssociationState.Broken && !points.SequenceEqual(Definition.Anchors));
        AssociationState = state;
        if (state != CadAssociationState.Broken) Definition = Definition with { Anchors = points };
        if (changed) Rebuild();
        return changed;
    }

    public void Transform(Func<CadPointD, CadPointD> transform, double scale = 1, double? linearRotation = null)
    {
        // Selection transforms explicitly detach anchors. Association to untransformed source geometry must not silently snap back.
        SetDefinition(Definition with { Anchors = Definition.Anchors.Select(a => new CadDimensionAnchor(transform(a.Point))).ToArray(),
            Placement = transform(Definition.Placement), AnnotationScale = Definition.AnnotationScale * Math.Abs(scale), LinearRotationRadians = linearRotation ?? Definition.LinearRotationRadians });
        AssociationState = CadAssociationState.Detached;
    }

    private void Rebuild()
    {
        var d = Definition; var a = d.Anchors[0].Point; var b = d.Anchors[1].Point; var p = d.Placement;
        var height = d.Style.TextHeight * d.AnnotationScale; var arrow = d.Style.ArrowSize * d.AnnotationScale;
        var lines = new List<CadStrokeTextSegment>();
        void Line(CadPointD x, CadPointD y) { if (x.DistanceTo(y) > 1e-12) lines.Add(new(x,y)); }
        void Arrow(CadPointD tip, CadPointD toward)
        {
            var angle = Math.Atan2(toward.Y-tip.Y,toward.X-tip.X);
            if(d.Style.Arrow==CadDimensionArrow.Slash)
            {var v=new CadVectorD(Math.Cos(angle+Math.PI/4)*arrow/2,Math.Sin(angle+Math.PI/4)*arrow/2);Line(tip-v,tip+v);return;}
            Line(tip, tip + new CadVectorD(Math.Cos(angle+.35)*arrow, Math.Sin(angle+.35)*arrow));
            Line(tip, tip + new CadVectorD(Math.Cos(angle-.35)*arrow, Math.Sin(angle-.35)*arrow));
            if(d.Style.Arrow==CadDimensionArrow.Closed)Line(tip+new CadVectorD(Math.Cos(angle+.35)*arrow,Math.Sin(angle+.35)*arrow),tip+new CadVectorD(Math.Cos(angle-.35)*arrow,Math.Sin(angle-.35)*arrow));
        }
        CadPointD textPoint; string prefix = "";
        if (d.Kind == CadDimensionKind.Leader)
        {
            Measurement = a.DistanceTo(b); Line(a,b); Line(b,p); Arrow(a,b); textPoint = p;
        }
        else if (d.Kind == CadDimensionKind.Angular)
        {
            var c = d.Anchors[2].Point;
            var start = Math.Atan2(b.Y-a.Y,b.X-a.X); var end = Math.Atan2(c.Y-a.Y,c.X-a.X);
            var sweep = (end-start) % (Math.PI*2); if (sweep<0) sweep+=Math.PI*2;
            if(sweep>Math.PI) {start=end;sweep=Math.PI*2-sweep;}
            Measurement=sweep*180/Math.PI; var radius=Math.Max(a.DistanceTo(p),height*3);
            var previous=a+new CadVectorD(Math.Cos(start)*radius,Math.Sin(start)*radius);
            var first=previous;
            for(var i=1;i<=48;i++) { var angle=start+sweep*i/48;var next=a+new CadVectorD(Math.Cos(angle)*radius,Math.Sin(angle)*radius);Line(previous,next);previous=next; }
            Line(a,first);Line(a,previous);
            Arrow(first,a+new CadVectorD(Math.Cos(start+.1)*radius,Math.Sin(start+.1)*radius));
            Arrow(previous,a+new CadVectorD(Math.Cos(start+sweep-.1)*radius,Math.Sin(start+sweep-.1)*radius));textPoint=p;
        }
        else if (d.Kind is CadDimensionKind.Radius or CadDimensionKind.Diameter)
        {
            var radius=a.DistanceTo(b);Measurement=radius*(d.Kind==CadDimensionKind.Diameter?2:1);
            prefix=d.Kind==CadDimensionKind.Diameter?"D ":"R ";
            var start=d.Kind==CadDimensionKind.Diameter ? a+(a-b) : a;
            Line(start,b);Line(b,p);Arrow(b,start);if(d.Kind==CadDimensionKind.Diameter)Arrow(start,b);textPoint=p;
        }
        else
        {
            var angle=d.Kind==CadDimensionKind.LinearX?d.LinearRotationRadians:d.Kind==CadDimensionKind.LinearY?d.LinearRotationRadians+Math.PI/2:Math.Atan2(b.Y-a.Y,b.X-a.X);
            var u=new CadVectorD(Math.Cos(angle),Math.Sin(angle));var n=new CadVectorD(-u.Y,u.X);
            double Dot(CadVectorD v,CadVectorD w)=>v.X*w.X+v.Y*w.Y;
            var offset=Dot(p-a,n);var x=a+n*offset;var y=b+n*(offset-Dot(b-a,n));
            Measurement=Math.Abs(Dot(b-a,u)); Line(x,y);
            var sign=offset<0?-1:1;var gap=d.Style.ExtensionGap*d.AnnotationScale;var beyond=d.Style.ExtensionBeyond*d.AnnotationScale;
            Line(a+n*(sign*gap),x+n*(sign*beyond));Line(b+n*(sign*gap),y+n*(sign*beyond));
            Arrow(x,y);Arrow(y,x);textPoint=new((x.X+y.X)/2,(x.Y+y.Y)/2);
        }
        var value=d.Kind==CadDimensionKind.Angular?Measurement:CadUnitConversion.FromMillimeters(Measurement,d.Style.Unit);
        var suffix=d.Kind==CadDimensionKind.Angular?" deg":CadUnitConversion.GetSymbol(d.Style.Unit);
        var measured=prefix+value.ToString("F"+d.Style.Precision,CultureInfo.InvariantCulture)+(suffix.Length==0?"":" "+suffix);
        DisplayText=(HasTextOverride ? "* "+d.TextOverride : measured)+(AssociationState==CadAssociationState.Broken?" !":"");
        if(d.Kind==CadDimensionKind.Leader && !HasTextOverride) DisplayText="Leader";
        // Font glyphs use screen Y-down coordinates; annotation strokes live in world Y-up.
        var glyphs=CadStrokeFont.CreateSegments(DisplayText,CadPointD.Origin,height,shapeFontId:new(d.Style.ShapeFont));
        var textBounds=glyphs.Aggregate(CadRectD.Empty,(bounds,s)=>bounds.ExpandToInclude(s.Start).ExpandToInclude(s.End));
        var centerText=d.Kind is CadDimensionKind.LinearX or CadDimensionKind.LinearY or CadDimensionKind.Aligned or CadDimensionKind.Angular;
        var originX=textPoint.X-(centerText && !textBounds.IsEmpty?(textBounds.MinX+textBounds.MaxX)/2:0);
        CadPointD TextPoint(CadPointD v)=>new(originX+v.X,textPoint.Y+height*.4+(textBounds.IsEmpty?height:textBounds.MaxY)-v.Y);
        lines.AddRange(glyphs.Select(s=>new CadStrokeTextSegment(TextPoint(s.Start),TextPoint(s.End))));
        if(AssociationState==CadAssociationState.Broken)
        {Line(p+new CadVectorD(-height,-height),p+new CadVectorD(height,height));Line(p+new CadVectorD(-height,height),p+new CadVectorD(height,-height));}
        _strokes=lines.ToArray(); _bounds=_strokes.Aggregate(CadRectD.Empty,(bounds,s)=>bounds.ExpandToInclude(s.Start).ExpandToInclude(s.End));
    }
}
