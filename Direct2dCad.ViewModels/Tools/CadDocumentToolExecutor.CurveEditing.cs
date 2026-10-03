using System.Text.Json;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Tools;

internal sealed partial class CadDocumentToolExecutor
{
    private static object CurveEditSchema() => new
    {
        type="object", properties=new Dictionary<string,object>
        {
            ["operation"]=new {type="string", @enum=new[]{"offset","trim","extend","fillet","chamfer","join","break"}},
            ["entity_ids"]=new {type="array",minItems=1,items=new {type="integer"}},
            ["boundary_ids"]=new {type="array",minItems=1,items=new {type="integer"}},
            ["x"]=Number("Side or picked segment X; millimetres"),["y"]=Number("Side or picked segment Y; millimetres"),
            ["x2"]=Number("Second corner/break pick X"),["y2"]=Number("Second corner/break pick Y"),
            ["distance"]=Number("Offset distance, fillet radius or first chamfer distance; positive"),
            ["second_distance"]=Number("Second chamfer distance; positive"),["trim"]=new {type="boolean"},
            ["all_corners"]=new {type="boolean",description="Apply fillet/chamfer to all sharp corners of one open or closed path. Otherwise use two picks on adjacent segments of one path or two different curves."}
        }, required=new[]{"operation","entity_ids"},additionalProperties=false
    };
    private static object ArraySchema() => new
    {
        type="object",properties=new Dictionary<string,object>
        {
            ["operation"]=new {type="string",@enum=new[]{"rectangular","polar"}},
            ["entity_ids"]=new {type="array",minItems=1,items=new {type="integer"}},
            ["rows"]=new {type="integer",minimum=1},["columns"]=new {type="integer",minimum=1},
            ["spacing_x"]=Number("X spacing in millimetres"),["spacing_y"]=Number("Y spacing in millimetres"),
            ["center_x"]=Number("Center X in millimetres"),["center_y"]=Number("Center Y in millimetres"),
            ["count"]=new {type="integer",minimum=2},["sweep_degrees"]=Number("Total angle; full 360 excludes duplicate final placement"),["rotate_copies"]=new {type="boolean"}
        },required=new[]{"operation","entity_ids"},additionalProperties=false
    };
    private static double EditNumber(JsonElement args,string name,double? fallback=null)
    { if(args.TryGetProperty(name,out var e) && e.TryGetDouble(out var n) && double.IsFinite(n)) return n; return fallback ?? throw new ArgumentException($"{name} must be a finite number."); }
    private string EditCurves(JsonElement args)
    {
        var ids=ResolveEntityIds(args,false); var document=documentViewModel.CadEditor.Document;
        var entities=ids.Select(document.GetEntity).ToArray();
        var operation=args.GetProperty("operation").GetString();
        var allCorners=args.TryGetProperty("all_corners",out var all) && all.GetBoolean();
        if(allCorners && operation is not ("fillet" or "chamfer")) throw new ArgumentException("all_corners only applies to fillet/chamfer.");
        var point=operation=="join" || allCorners ? default : new CadPointD(EditNumber(args,"x"),EditNumber(args,"y"));
        var second=args.TryGetProperty("x2",out _) ? new CadPointD(EditNumber(args,"x2"),EditNumber(args,"y2")) : (CadPointD?)null;
        var boundaries=args.TryGetProperty("boundary_ids",out var b) ? ValidateEntityIds(b.EnumerateArray().Select(e=>new EntityId(e.GetInt64())).ToArray()).Select(document.GetEntity).ToArray() : [];
        if(operation is not "join" && operation is not ("fillet" or "chamfer") && ids.Length!=1) throw new ArgumentException("This operation requires exactly one target.");
        var plan=operation switch
        {
            "offset"=>CadCurveEditing.Offset(entities[0],EditNumber(args,"distance"),point),
            "trim"=>CadCurveEditing.Trim(entities[0],boundaries,point),
            "extend"=>CadCurveEditing.Extend(entities[0],boundaries,point),
            "join"=>CadCurveEditing.Join(entities),
            "break"=>CadCurveEditing.Break(entities[0],point,second),
            "fillet" or "chamfer" when allCorners && entities.Length==1=>CadCurveEditing.AllCorners(entities[0],EditNumber(args,"distance"),EditNumber(args,"second_distance",EditNumber(args,"distance")),operation=="fillet"),
            "fillet" or "chamfer" when !allCorners && entities.Length is 1 or 2=>CadCurveEditing.Corner(entities[0],entities.Length==1 ? entities[0] : entities[1],point,second ?? throw new ArgumentException("The second pick is required."),EditNumber(args,"distance"),EditNumber(args,"second_distance",EditNumber(args,"distance")),operation=="fillet",!args.TryGetProperty("trim",out var t) || t.GetBoolean()),
            _=>throw new ArgumentException("Unsupported operation or target count.")
        };
        var command=new EditCurvesCommand(operation!,plan); ExecuteCommand(command);
        return Success(new {operation,result_entity_ids=command.ResultEntityIds.Select(id=>id.Value),replaced_entity_ids=plan.Replacements.Select(r=>r.SourceId.Value),reference_rule="preserve compatible surviving IDs; type conversions, splits and joins return new IDs without guessing reference rebinding"});
    }
    private string ArrayEntities(JsonElement args)
    {
        var ids=ResolveEntityIds(args,false);
        var op=args.GetProperty("operation").GetString();
        var command=op switch
        {
            "rectangular"=>new ArrayEntitiesCommand(ids,args.GetProperty("rows").GetInt32(),args.GetProperty("columns").GetInt32(),EditNumber(args,"spacing_x"),EditNumber(args,"spacing_y")),
            "polar"=>new ArrayEntitiesCommand(ids,new(EditNumber(args,"center_x"),EditNumber(args,"center_y")),args.GetProperty("count").GetInt32(),EditNumber(args,"sweep_degrees",360)*Math.PI/180,!args.TryGetProperty("rotate_copies",out var rotate) || rotate.GetBoolean()),
            _=>throw new ArgumentException("Unknown array operation.")
        };
        ExecuteCommand(command); return Success(new {operation=op,created_entity_ids=command.CreatedEntityIds.Select(id=>id.Value)});
    }
}
