using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.IO.Dxf;

public sealed record CadDxfResult(CadDocument Document,int Imported,IReadOnlyDictionary<string,int> Unsupported);
public sealed class CadDxfUnitRequiredException(int declaredCode) : IOException("DXF units are missing or unsupported. Choose the source drawing unit once before importing.")
{public int DeclaredCode {get;}=declaredCode;}
public sealed record CadDxfLimits(long MaximumBytes=64*1024*1024,int MaximumGroups=3_000_000,int MaximumEntities=500_000,int MaximumBlockDepth=32);

/// <summary>Bounded ASCII DXF exchange, exported as R2004. Unsupported objects are reported; invalid structure is rejected.</summary>
public sealed partial class CadDxfStorage
{
    public CadDxfLimits Limits {get;init;}=new();
    private static readonly CultureInfo Culture=CultureInfo.InvariantCulture;
    private sealed record Pair(int Code,string Value);
    private sealed record Record(string Type,List<Pair> Values)
    {
        public string Get(int code,string fallback="")=>Values.FirstOrDefault(p=>p.Code==code)?.Value??fallback;
        public double Number(int code,double fallback=0)
        {
            var value=Get(code);if(value.Length==0)return fallback;
            return double.TryParse(value,NumberStyles.Float,Culture,out var n)&&double.IsFinite(n)?n:throw new InvalidDataException($"Invalid DXF number in {Type}, group {code}.");
        }
        public int Integer(int code,int fallback=0)
        {
            var value=Number(code,fallback);
            if(value!=Math.Truncate(value) || value<int.MinValue || value>int.MaxValue)throw new InvalidDataException($"Invalid DXF integer in {Type}, group {code}.");
            return (int)value;
        }
    }
    private static void Count(Dictionary<string,int> report,string key)=>report[key]=report.GetValueOrDefault(key)+1;
    public Task<CadDxfResult> ImportAsync(string path,CadUnit? sourceUnit=null,CancellationToken token=default)=>Task.Run(()=>Import(path,sourceUnit,token),token);

    public CadDxfResult Import(string path,CadUnit? sourceUnit=null,CancellationToken token=default)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(stream.Length>Limits.MaximumBytes)throw new InvalidDataException("DXF exceeds the file budget.");
        using var reader=new StreamReader(stream,new UTF8Encoding(false,true),true);
        var pairs=new List<Pair>();string? line;
        while((line=reader.ReadLine()) is not null)
        {
            token.ThrowIfCancellationRequested();
            if(line.Length>65536 || !int.TryParse(line.Trim(),out var code) || code is <0 or >1071)throw new InvalidDataException("Invalid or binary DXF group stream.");
            var value=reader.ReadLine()??throw new InvalidDataException("Truncated DXF group pair.");
            if(value.Length>65536 || pairs.Count>=Limits.MaximumGroups)throw new InvalidDataException("DXF exceeds its group/text budget.");
            pairs.Add(new(code,value.TrimEnd()));
        }
        var sections=new Dictionary<string,List<Pair>>(StringComparer.OrdinalIgnoreCase);
        var end=false;
        for(var i=0;i<pairs.Count;)
        {
            token.ThrowIfCancellationRequested();var pair=pairs[i++];
            if(pair is {Code:0,Value:"EOF"}){end=true;if(i!=pairs.Count)throw new InvalidDataException("Trailing data after DXF EOF.");break;}
            if(pair.Code!=0 || pair.Value!="SECTION" || i>=pairs.Count || pairs[i].Code!=2)throw new InvalidDataException("Expected a DXF SECTION.");
            var name=pairs[i++].Value;var contents=new List<Pair>();
            while(i<pairs.Count && pairs[i] is not {Code:0,Value:"ENDSEC"})contents.Add(pairs[i++]);
            if(i==pairs.Count || !sections.TryAdd(name,contents))throw new InvalidDataException("Truncated or repeated DXF section.");i++;
        }
        if(!end || !sections.ContainsKey("ENTITIES"))throw new InvalidDataException("DXF is incomplete.");
        var header=sections.GetValueOrDefault("HEADER")??[];
        string Header(string key)=>header.SkipWhile(p=>p.Code!=9 || p.Value!=key).Skip(1).FirstOrDefault()?.Value??"";
        var version=Header("$ACADVER");
        if(version.Length>0 && version is not ("AC1009" or "AC1015" or "AC1018" or "AC1021" or "AC1024" or "AC1027" or "AC1032"))throw new InvalidDataException("Unsupported DXF version: "+version);
        var unitCode=int.TryParse(Header("$INSUNITS"),out var declared)?declared:0;
        var unit=sourceUnit??UnitFromCode(unitCode)??throw new CadDxfUnitRequiredException(unitCode);
        if(!Enum.IsDefined(unit) || unit==CadUnit.Unitless)throw new CadDxfUnitRequiredException(unitCode);
        var factor=CadUnitConversion.ToMillimeters(1,unit);var document=CadDocument.Create(Path.GetFileNameWithoutExtension(path));document.DocumentSettings.SetUnit(unit);
        var report=new Dictionary<string,int>();var layers=new Dictionary<string,LayerId>(StringComparer.OrdinalIgnoreCase){["0"]=LayerId.Default};
        LayerId Layer(string name)
        {name=Decode(name);if(!layers.TryGetValue(name,out var id))layers[name]=id=document.CreateLayer(name,CadColor.White,CadLineWeight.Default);return id;}
        var total=0;
        List<Record> Records(IEnumerable<Pair> contents)
        {
            var result=new List<Record>();Record? current=null;
            foreach(var p in contents)
            {
                token.ThrowIfCancellationRequested();
                if(p.Code==0){if(++total>Limits.MaximumEntities)throw new InvalidDataException("DXF exceeds its object budget.");current=new(p.Value,[]);result.Add(current);}
                else current?.Values.Add(p);
            }
            return result;
        }
        var layerLineTypes=new Dictionary<LayerId,string>();
        foreach(var record in Records(sections.GetValueOrDefault("TABLES")??[]))
        {
            if(record.Type!="LAYER")continue;
            var layer=document.GetLayer(Layer(record.Get(2,"0")));var flags=record.Integer(70);layer.SetVisible(record.Number(62,7)>=0);layer.SetFrozen((flags&1)!=0);layer.SetLocked((flags&4)!=0);
            layer.SetColor(ReadColor(record));
            ReportColor(record,report);
            layerLineTypes[layer.Id]=record.Get(6,"CONTINUOUS").ToUpperInvariant();
            if(record.Number(370)>0)layer.SetLineWeight(new(record.Number(370)/100));
        }
        var blocks=new Dictionary<string,BlockId>(StringComparer.OrdinalIgnoreCase);
        var blockItems=new Dictionary<string,List<Record>>(StringComparer.OrdinalIgnoreCase);var blockRecords=Records(sections.GetValueOrDefault("BLOCKS")??[]);string? active=null;
        CadPointD Point(Record r,int x=10)=>new(r.Number(x)*factor,r.Number(x+10)*factor);
        foreach(var r in blockRecords)
        {
            if(r.Type=="BLOCK")
            {
                active=Decode(r.Get(2));if(active.Length==0 || blockItems.ContainsKey(active))throw new InvalidDataException("Invalid or duplicate block name.");
                blockItems[active]=[];
                if(!active.StartsWith('*'))blocks[active]=document.CreateBlockDefinition(active,Point(r));
            }
            else if(r.Type=="ENDBLK")active=null;
            else if(active is not null)blockItems[active].Add(r);
        }
        if(active is not null)throw new InvalidDataException("Unterminated DXF block.");
        var blockDepths=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        int CheckBlock(string name,HashSet<string> visiting)
        {
            token.ThrowIfCancellationRequested();
            if(blockDepths.TryGetValue(name,out var completed))return completed;
            if(visiting.Count>=Limits.MaximumBlockDepth || !visiting.Add(name))throw new InvalidDataException("DXF block graph is cyclic or too deep.");
            var depth=1;
            foreach(var insert in blockItems[name].Where(r=>r.Type=="INSERT"))
            {var child=Decode(insert.Get(2));if(blockItems.ContainsKey(child))depth=Math.Max(depth,1+CheckBlock(child,visiting));}
            visiting.Remove(name);
            if(depth>Limits.MaximumBlockDepth)throw new InvalidDataException("DXF block graph is too deep.");
            return blockDepths[name]=depth;
        }
        foreach(var name in blocks.Keys)CheckBlock(name,[]);
        var imported=0;
        void Restore(Record r,BlockId owner)
        {
            token.ThrowIfCancellationRequested();
            if(r.Number(67)!=0){Count(report,"Paper space");return;}
            if(r.Number(30)!=0 || r.Number(31)!=0 || r.Number(38)!=0 || r.Number(39)!=0 || r.Number(210)!=0 || r.Number(220)!=0 || r.Number(230,1)!=1)
            {Count(report,"3D/extrusion");return;}
            var layer=Layer(r.Get(8,"0"));CadEntity? entity;
            switch(r.Type)
            {
                case "LINE":entity=document.AddLine(Point(r),Point(r,11),layer);break;
                case "CIRCLE":entity=document.AddCircle(Point(r),r.Number(40)*factor,layer);break;
                case "ELLIPSE":
                    var major = Point(r,11); var majorLength = major.DistanceTo(default);
                    var ratio = r.Number(40); var ellipseStart = r.Number(41); var ellipseEnd = r.Number(42, Math.PI*2);
                    if (majorLength <= CadGeometryTolerance.Absolute || ratio <= 0 || ratio > 1 ||
                        ellipseEnd-ellipseStart > Math.PI*2+1e-10 || ellipseEnd-ellipseStart < -Math.PI*2-1e-10)
                        throw new InvalidDataException("Invalid DXF ellipse axes or parameter interval.");
                    var ellipseSweep = Math.Abs(ellipseEnd-ellipseStart) >= Math.PI*2-1e-10 ? Math.PI*2 : CadPlanarPrimitive.PositiveAngle(ellipseEnd-ellipseStart);
                    if (ellipseSweep < 1e-12) { Count(report,"Degenerate ELLIPSE"); return; }
                    var ellipseRotation = Math.Atan2(major.Y,major.X);
                    if (ellipseSweep >= Math.PI*2-1e-10)
                    {
                        var ellipse = document.AddEllipse(Point(r),majorLength,majorLength*ratio,layer);
                        ellipse.SetRotation(ellipseRotation); entity=ellipse;
                    }
                    else
                    {
                        var ellipseArc = document.AddEllipseArc(Point(r),majorLength,majorLength*ratio,ellipseStart,ellipseSweep,layer);
                        ellipseArc.SetRotation(ellipseRotation); entity=ellipseArc;
                    }
                    break;
                case "ARC":
                    var start=r.Number(50)*Math.PI/180;var sweep=((r.Number(51)-r.Number(50)+360)%360)*Math.PI/180;
                    if(sweep<1e-12){Count(report,"Degenerate ARC");return;}
                    entity=document.AddArc(Point(r),r.Number(40)*factor,start,sweep,layer);break;
                case "LWPOLYLINE":
                    if(r.Values.Any(p=>p.Code is 40 or 41 or 43 && Math.Abs(double.Parse(p.Value,Culture))>1e-12)){Count(report,"Wide polyline");return;}
                    var vertices=new List<(CadPointD Point,double Bulge)>();
                    for(var i=0;i<r.Values.Count;i++)
                    {
                        if(r.Values[i].Code!=10)continue;
                        var x=double.Parse(r.Values[i].Value,Culture)*factor;double? y=null;var bulge=0d;
                        for(var j=i+1;j<r.Values.Count && r.Values[j].Code!=10;j++)
                        {if(r.Values[j].Code==20)y=double.Parse(r.Values[j].Value,Culture)*factor;if(r.Values[j].Code==42)bulge=double.Parse(r.Values[j].Value,Culture);}
                        if(y is null || !double.IsFinite(x) || !double.IsFinite(y.Value) || !double.IsFinite(bulge))throw new InvalidDataException("Invalid polyline vertex.");
                        vertices.Add((new(x,y.Value),bulge));
                    }
                    if(vertices.Count!=r.Integer(90) || vertices.Count<2)throw new InvalidDataException("Invalid polyline vertex count.");
                    var closed=(r.Integer(70)&1)!=0;
                    if(vertices.All(v=>Math.Abs(v.Bulge)<1e-12))entity=document.AddPolyline(vertices.Select(v=>v.Point),closed,layer);
                    else
                    {
                        var segments=new List<CadCompositePathSegment>();
                        for(var i=0;i<(closed?vertices.Count:vertices.Count-1);i++)
                        {
                            var a=vertices[i];var b=vertices[(i+1)%vertices.Count].Point;
                            if(Math.Abs(a.Bulge)<1e-12)segments.Add(new CadCompositeLineSegment(b));
                            else
                            {
                                var chord=b-a.Point;var offset=(1-a.Bulge*a.Bulge)/(4*a.Bulge);
                                var center=new CadPointD((a.Point.X+b.X)/2-chord.Y*offset,(a.Point.Y+b.Y)/2+chord.X*offset);
                                segments.Add(new CadCompositeArcSegment(center,4*Math.Atan(a.Bulge)));
                            }
                        }
                        entity=document.AddCompositePath(vertices[0].Point,segments,closed,layer);
                    }
                    break;
                case "TEXT":
                    if(r.Integer(72)!=0 || r.Integer(73)!=0 || r.Integer(71)!=0 || r.Number(51)!=0 || Math.Abs(r.Number(41,1)-1)>1e-12)
                    {Count(report,"TEXT alignment/shape");return;}
                    entity=document.AddText(Decode(r.Get(1)),Point(r),r.Number(40)*factor,r.Number(50)*Math.PI/180,layer);
                    Count(report,"TEXT font substituted");break;
                case "INSERT":
                    if(r.Integer(70,1)!=1 || r.Integer(71,1)!=1 || !blocks.TryGetValue(Decode(r.Get(2)),out var definition)){Count(report,"INSERT array/missing block");return;}
                    entity=document.AddBlockReference(definition,Point(r),layer,rotationRadians:r.Number(50)*Math.PI/180,scaleX:r.Number(41,1),scaleY:r.Number(42,1),ownerBlockId:owner);break;
                default:Count(report,r.Type);return;
            }
            if(entity.OwnerBlockId!=owner)document.MoveEntityToBlock(entity.Id,owner);
            entity.SetVisible(r.Integer(60)==0);
            ReportColor(r,report);
            if(r.Get(420).Length>0 || r.Integer(62,256) is not (0 or 256))
            {
                var style=document.CreateGraphicStyle("DXF-"+entity.Id.Value,ReadColor(r),CadLineWeight.Default,LineTypeId.Continuous);
                SetGraphicStyle(entity,style);entity.SetColorSource(CadColorSource.Explicit);
            }
            else if(r.Integer(62,256)==0)entity.SetColorSource(CadColorSource.ByBlock);
            if(r.Number(370)>0)entity.SetLineWeight(new(r.Number(370)/100));
            var ltype=r.Get(6,"BYLAYER").ToUpperInvariant();
            if(ltype=="BYLAYER")ltype=layerLineTypes.GetValueOrDefault(layer,"CONTINUOUS");
            var dash=ltype.ToUpperInvariant() switch {"DASHED"=>CadStrokeDashStyle.Dash,"DOTTED"=>CadStrokeDashStyle.Dot,"DASHDOT"=>CadStrokeDashStyle.DashDot,_=>CadStrokeDashStyle.Solid};
            if(ltype is not ("BYLAYER" or "BYBLOCK" or "CONTINUOUS" or "DASHED" or "DOTTED" or "DASHDOT"))Count(report,"Custom line type substituted");
            entity.SetStrokeStyle(entity.StrokeStyle with {DashStyle=dash});imported++;
        }
        foreach(var (name,id) in blocks)foreach(var r in blockItems[name])Restore(r,id);
        foreach(var r in Records(sections["ENTITIES"]))Restore(r,BlockId.ModelSpace);
        document.RefreshBlockReferenceBounds();return new(document,imported,report);
    }

    private static CadUnit? UnitFromCode(int code)=>code switch {1=>CadUnit.Inch,2=>CadUnit.Foot,4=>CadUnit.Millimeter,5=>CadUnit.Centimeter,6=>CadUnit.Meter,9=>CadUnit.Mil,_=>null};
    private static int UnitCode(CadUnit unit)=>unit switch {CadUnit.Inch=>1,CadUnit.Foot=>2,CadUnit.Millimeter=>4,CadUnit.Centimeter=>5,CadUnit.Meter=>6,CadUnit.Mil=>9,_=>0};
    private static CadColor ReadColor(Record r)
    {
        if(r.Get(420).Length>0){var rgb=r.Integer(420);return CadColor.FromRgb((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb);}
        return Math.Abs(r.Integer(62,7)) switch {1=>CadColor.Red,2=>CadColor.FromRgb(255,255,0),3=>CadColor.Green,4=>CadColor.FromRgb(0,255,255),5=>CadColor.Blue,6=>CadColor.FromRgb(255,0,255),_=>CadColor.White};
    }
    private static void ReportColor(Record r,Dictionary<string,int> report)
    {
        if(r.Get(420).Length==0 && Math.Abs((long)r.Integer(62,7)) is >7 and <256)Count(report,"ACI color substituted");
    }
    private static string Decode(string text)=>Regex.Replace(text,@"\\U\+([0-9A-Fa-f]{4})",m=>((char)int.Parse(m.Groups[1].Value,NumberStyles.HexNumber,Culture)).ToString(),RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
    private static string Encode(string text)
    {
        if(text.Length>65536)throw new InvalidDataException("DXF text exceeds the group budget.");
        var builder=new StringBuilder();foreach(var c in text)builder.Append(c is <' ' or >'~' || c=='\\'?"\\U+"+((int)c).ToString("X4",Culture):c.ToString());return builder.ToString();
    }

    public IReadOnlyDictionary<string,int> AnalyzeExport(CadDocument doc,CancellationToken token=default)
    {
        var report=new Dictionary<string,int>();
        foreach(var e in doc.Entities.Values.Where(e=>!e.IsErased))
        {
            token.ThrowIfCancellationRequested();
            if(doc.GetBlock(e.OwnerBlockId).Kind==CadBlockKind.SystemSpace && e.OwnerBlockId!=BlockId.ModelSpace){Count(report,"Paper space");continue;}
            if(e is CadDimension)Count(report,"Dimension association flattened to strokes");
            else if(e is CadRegion region)
            {
                Count(report,region.Contours.Any(c=>c.Edges.Any(p=>p.IsEllipse)) ? "Region converted to exact boundary curves" : "Region converted to boundary polylines");
                if(region.FillStyleId is not null && !TryRegionSolidFill(doc,region,out _))Count(report,"Fill omitted");
            }
            else if(e is not (CadLine or CadCircle or CadArc or CadPolyline or CadText or CadBlockReference or CadCompositePath) || e is CadCompositePath p && p.Segments.Any(s=>s is CadCompositeSplineSegment))Count(report,e.GetType().Name);
            if(e is CadText t)Count(report,t.IsInverted?"TEXT font/effects":"TEXT font substituted");
            if(e.IsLocked)Count(report,"Entity lock omitted");
            if(e.StrokeStyle.StartCap!=CadStrokeCap.Flat || e.StrokeStyle.EndCap!=CadStrokeCap.Flat || e.StrokeStyle.DashCap!=CadStrokeCap.Flat || e.StrokeStyle.LineJoin!=CadStrokeLineJoin.Miter)Count(report,"Stroke caps/joins omitted");
            if(e.StrokeStyle.DashStyle==CadStrokeDashStyle.DashDotDot)Count(report,"DashDotDot substituted");
            if(e switch {CadCircle c=>c.FillStyleId,CadPolyline p=>p.FillStyleId,CadCompositePath p=>p.FillStyleId,_=>null} is not null)Count(report,"Fill omitted");
            var style=GraphicStyle(e)??doc.GetLayer(e.LayerId).DefaultGraphicStyleId;
            if(style is { } id && doc.TryGetStyle(id,out var value) && value is CadGraphicStyle g && g.LineTypeId!=LineTypeId.Continuous)Count(report,"Custom line type omitted");
        }
        if(doc.DocumentSettings.Unit==CadUnit.Unitless)Count(report,"Unitless drawing");return report;
    }

    public Task ExportAsync(CadDocument snapshot,string path,bool allowLoss=false,CadFileRevision? expected=null,CancellationToken token=default)=>Task.Run(()=>Export(snapshot,path,allowLoss,expected,token),token);
    public void Export(CadDocument doc,string path,bool allowLoss=false,CadFileRevision? expected=null,CancellationToken token=default)
    {
        if(!allowLoss && AnalyzeExport(doc,token).Count>0)throw new InvalidOperationException("DXF export has losses. Review the summary before writing.");
        path=Path.GetFullPath(path);expected??=new(path,false,0,"");if(!string.Equals(path,expected.FullPath,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Overwrite authorization belongs to another path.");expected.Verify();
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))
            {
                var groups=0;var records=0;
                void Pair(int code,object value)
                {
                    token.ThrowIfCancellationRequested();if(++groups>Limits.MaximumGroups || stream.Position>Limits.MaximumBytes)throw new InvalidDataException("DXF export exceeds its budget.");
                    if(value is string text && text.Length>65536)throw new InvalidDataException("DXF text exceeds the group budget.");
                    if(code==0 && value is string type && type is not ("SECTION" or "ENDSEC" or "EOF") && ++records>Limits.MaximumEntities)throw new InvalidDataException("DXF export exceeds its record budget.");
                    writer.WriteLine(code.ToString(Culture));writer.WriteLine(value is double d?d.ToString("G17",Culture):Convert.ToString(value,Culture));
                }
                void Section(string name){Pair(0,"SECTION");Pair(2,name);}
                var unit=doc.DocumentSettings.Unit;var factor=CadUnitConversion.FromMillimeters(1,unit);
                void Point(CadPointD p,int x=10){Pair(x,p.X*factor);Pair(x+10,p.Y*factor);Pair(x+20,0);}
                var nextHandle=16;string Handle()=>(nextHandle++).ToString("X",Culture);
                var blockHandles=doc.Blocks.Values.ToDictionary(b=>b.Id,_=>Handle());
                string BlockName(CadBlockDefinition b)=>b.Id==BlockId.ModelSpace?"*Model_Space":b.Kind==CadBlockKind.SystemSpace?"*Paper_Space"+(b.Id==BlockId.PaperSpace?"":b.Id.Value.ToString(Culture)):Encode(b.Name);
                string Table(string name,int count){var handle=Handle();Pair(0,"TABLE");Pair(2,name);Pair(5,handle);Pair(100,"AcDbSymbolTable");Pair(70,count);return handle;}
                void TableRecord(string type,string subclass,string owner,string? handle=null){Pair(0,type);Pair(5,handle??Handle());Pair(330,owner);Pair(100,"AcDbSymbolTableRecord");Pair(100,subclass);}
                Section("HEADER");Pair(9,"$ACADVER");Pair(1,"AC1018");Pair(9,"$DWGCODEPAGE");Pair(3,"ANSI_1252");Pair(9,"$INSUNITS");Pair(70,UnitCode(unit));Pair(0,"ENDSEC");
                Section("TABLES");var ltypeTable=Table("LTYPE",4);
                foreach(var (name,pattern) in new[]{("CONTINUOUS",Array.Empty<double>()),("DASHED",new[]{4d,-2}), ("DOTTED",new[]{0d,-2}),("DASHDOT",new[]{4d,-2,0,-2})})
                {TableRecord("LTYPE","AcDbLinetypeTableRecord",ltypeTable);Pair(2,name);Pair(70,0);Pair(3,name);Pair(72,65);Pair(73,pattern.Length);Pair(40,pattern.Sum(Math.Abs));foreach(var dash in pattern){Pair(49,dash);Pair(74,0);}}
                Pair(0,"ENDTAB");var layerTable=Table("LAYER",doc.Layers.Count);
                string LayerName(LayerId id)=>id==LayerId.Default?"0":Encode(doc.GetLayer(id).Name);
                foreach(var layer in doc.Layers.Values)
                {TableRecord("LAYER","AcDbLayerTableRecord",layerTable);Pair(2,LayerName(layer.Id));Pair(70,(layer.IsFrozen?1:0)|(layer.IsLocked?4:0));Pair(62,layer.IsVisible?7:-7);Pair(420,(layer.Color.R<<16)|(layer.Color.G<<8)|layer.Color.B);Pair(370,(int)Math.Round(layer.LineWeight.Value*100));Pair(6,"CONTINUOUS");}
                Pair(0,"ENDTAB");var styleTable=Table("STYLE",1);TableRecord("STYLE","AcDbTextStyleTableRecord",styleTable);Pair(2,"STANDARD");Pair(70,0);Pair(40,0);Pair(41,1);Pair(50,0);Pair(71,0);Pair(42,2.5);Pair(3,"txt");Pair(4,"");Pair(0,"ENDTAB");
                var blockTable=Table("BLOCK_RECORD",blockHandles.Count);foreach(var block in doc.Blocks.Values){TableRecord("BLOCK_RECORD","AcDbBlockTableRecord",blockTable,blockHandles[block.Id]);Pair(2,BlockName(block));Pair(70,UnitCode(unit));Pair(280,0);Pair(281,1);}Pair(0,"ENDTAB");Pair(0,"ENDSEC");
                var entityOwner=blockHandles[BlockId.ModelSpace];
                void Common(CadEntity e,string type,CadColor? fillColor=null)
                {
                    Pair(0,type);Pair(5,Handle());Pair(330,entityOwner);Pair(100,"AcDbEntity");Pair(8,LayerName(e.LayerId));Pair(60,e.IsVisible?0:1);
                    if(fillColor is { } fill){Pair(420,(fill.R<<16)|(fill.G<<8)|fill.B);if(fill.A!=255)Pair(440,0x02000000|(255-fill.A));}
                    else if(e.ColorSource==CadColorSource.Explicit){var style=GraphicStyle(e);var color=style is { } id && doc.TryGetStyle(id,out var s) && s is CadGraphicStyle g?g.StrokeColor:doc.GetLayer(e.LayerId).Color;Pair(420,(color.R<<16)|(color.G<<8)|color.B);}
                    else Pair(62,e.ColorSource==CadColorSource.ByBlock?0:256);
                    Pair(370,e.UseLayerLineWeight?-1:(int)Math.Round((e.LineWeight?.Value??.18)*100));
                    Pair(6,e.StrokeStyle.DashStyle switch {CadStrokeDashStyle.Dash=>"DASHED",CadStrokeDashStyle.Dot=>"DOTTED",CadStrokeDashStyle.DashDot=>"DASHDOT",_=>"CONTINUOUS"});
                    Pair(100,type switch {"LINE"=>"AcDbLine","CIRCLE" or "ARC"=>"AcDbCircle","ELLIPSE"=>"AcDbEllipse","LWPOLYLINE"=>"AcDbPolyline","TEXT"=>"AcDbText","INSERT"=>"AcDbBlockReference","HATCH"=>"AcDbHatch",_=>throw new InvalidOperationException("Unsupported DXF class.")});
                }
                void Polyline(CadEntity e,IReadOnlyList<(CadPointD Point,double Bulge)> points,bool closed)
                {Common(e,"LWPOLYLINE");Pair(90,points.Count);Pair(70,closed?1:0);foreach(var v in points){Pair(10,v.Point.X*factor);Pair(20,v.Point.Y*factor);if(v.Bulge!=0)Pair(42,v.Bulge);}}
                void Region(CadRegion region)
                {
                    if (region.Contours.Any(c=>c.Edges.Any(p=>p.IsEllipse)))
                    {
                        WriteEllipticalRegion(region, factor, TryRegionSolidFill(doc,region,out var solid) ? solid : null, Pair, Common, token);
                        return;
                    }
                    var boundaries=region.Contours.Select(c=>RegionVertices(c,token)).ToArray();
                    if(TryRegionSolidFill(doc,region,out var color))
                    {
                        // Standard non-associative SOLID HATCH; style 0 evaluates all loops by even-odd nesting.
                        // Write the fill first so the independent stroked boundaries remain above it.
                        Common(region,"HATCH",color);Point(default);Pair(210,0);Pair(220,0);Pair(230,1);
                        Pair(2,"SOLID");Pair(70,1);Pair(71,0);Pair(91,boundaries.Length);
                        foreach(var boundary in boundaries)
                        {
                            Pair(92,2);Pair(72,boundary.Any(v=>v.Bulge!=0)?1:0);Pair(73,1);Pair(93,boundary.Count);
                            foreach(var v in boundary){Pair(10,v.Point.X*factor);Pair(20,v.Point.Y*factor);Pair(42,v.Bulge);}
                            Pair(97,0);
                        }
                        Pair(75,0);Pair(76,1);Pair(98,0);
                    }
                    foreach(var boundary in boundaries)Polyline(region,boundary,true);
                }
                void Write(CadEntity e)
                {
                    token.ThrowIfCancellationRequested();if(e.IsErased)return;
                    switch(e)
                    {
                        case CadLine l:Common(e,"LINE");Point(l.Start);Point(l.End,11);break;
                        case CadCircle c:Common(e,"CIRCLE");Point(c.Center);Pair(40,c.Radius*factor);break;
                        case CadArc a:
                            if(a.IsFullCircle){Common(e,"CIRCLE");Point(a.Center);Pair(40,a.Radius*factor);break;}
                            Common(e,"ARC");Point(a.Center);Pair(40,a.Radius*factor);var start=a.SweepAngleRadians<0?a.StartAngleRadians+a.SweepAngleRadians:a.StartAngleRadians;
                            Pair(100,"AcDbArc");Pair(50,(start*180/Math.PI%360+360)%360);Pair(51,((start+Math.Abs(a.SweepAngleRadians))*180/Math.PI%360+360)%360);break;
                        case CadPolyline p:Polyline(e,p.Points.Select(p=>(p,0d)).ToArray(),p.Closed);break;
                        case CadCompositePath p when p.Segments.All(s=>s is not CadCompositeSplineSegment):
                            var vertices=new List<(CadPointD,double)>();var current=p.StartPoint;
                            foreach(var segment in p.Segments)
                            {
                                if(segment is CadCompositeLineSegment l){vertices.Add((current,0));current=l.End;}
                                else if(segment is CadCompositeArcSegment arc){vertices.Add((current,Math.Tan(arc.SweepAngleRadians/4)));current=CadMatrixD.CreateRotation(arc.SweepAngleRadians,arc.Center).TransformPoint(current);}
                            }
                            if(!p.Closed || !current.NearEquals(p.StartPoint))vertices.Add((current,0));Polyline(e,vertices,p.Closed);break;
                        case CadText t:Common(e,"TEXT");Point(t.Position);Pair(40,t.Height*factor);Pair(1,Encode(t.Text));Pair(50,t.RotationRadians*180/Math.PI);Pair(7,"STANDARD");Pair(100,"AcDbText");break;
                        case CadBlockReference b:Common(e,"INSERT");Pair(2,BlockName(doc.GetBlock(b.DefinitionBlockId)));Point(b.Position);Pair(41,b.ScaleX);Pair(42,b.ScaleY);Pair(43,1);Pair(50,b.RotationRadians*180/Math.PI);break;
                        case CadDimension d:foreach(var s in d.Strokes){Common(e,"LINE");Point(s.Start);Point(s.End,11);}break;
                        case CadRegion region:Region(region);break;
                    }
                }
                Section("BLOCKS");foreach(var block in doc.Blocks.Values)
                {entityOwner=blockHandles[block.Id];Pair(0,"BLOCK");Pair(5,Handle());Pair(330,entityOwner);Pair(100,"AcDbEntity");Pair(8,"0");Pair(100,"AcDbBlockBegin");Pair(2,BlockName(block));Pair(70,0);Point(block.BasePoint);Pair(3,BlockName(block));Pair(1,"");if(block.Kind!=CadBlockKind.SystemSpace)foreach(var e in doc.GetEntitiesInBlock(block.Id))Write(e);Pair(0,"ENDBLK");Pair(5,Handle());Pair(330,entityOwner);Pair(100,"AcDbEntity");Pair(8,"0");Pair(100,"AcDbBlockEnd");}
                Pair(0,"ENDSEC");Section("ENTITIES");entityOwner=blockHandles[BlockId.ModelSpace];foreach(var e in doc.GetEntitiesInBlock(BlockId.ModelSpace))Write(e);Pair(0,"ENDSEC");Pair(0,"EOF");writer.Flush();if(stream.Length>Limits.MaximumBytes)throw new InvalidDataException("DXF export exceeds its byte budget.");stream.Flush(true);
            }
            token.ThrowIfCancellationRequested();expected.Verify();File.Move(temp,path,true);
        }
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    private static StyleId? GraphicStyle(CadEntity e)=>e switch
    {CadLine v=>v.GraphicStyleId,CadCircle v=>v.GraphicStyleId,CadArc v=>v.GraphicStyleId,CadEllipse v=>v.GraphicStyleId,CadEllipseArc v=>v.GraphicStyleId,CadPolyline v=>v.GraphicStyleId,CadCompositePath v=>v.GraphicStyleId,CadText v=>v.GraphicStyleId,CadBlockReference v=>v.GraphicStyleId,CadRegion v=>v.GraphicStyleId,_=>null};
    private static void SetGraphicStyle(CadEntity e,StyleId id)
    {
        switch(e)
        {case CadLine v:v.SetGraphicStyleInternal(id);break;case CadCircle v:v.SetGraphicStyleInternal(id);break;case CadArc v:v.SetGraphicStyleInternal(id);break;case CadEllipse v:v.SetGraphicStyleInternal(id);break;case CadEllipseArc v:v.SetGraphicStyleInternal(id);break;case CadPolyline v:v.SetGraphicStyleInternal(id);break;case CadCompositePath v:v.SetGraphicStyleInternal(id);break;case CadText v:v.SetGraphicStyleInternal(id);break;case CadBlockReference v:v.SetGraphicStyleInternal(id);break;}
    }
}
