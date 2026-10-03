using System.Text.Json;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Text;
namespace Direct2dCad.ViewModels.Tools;
internal sealed partial class CadDocumentToolExecutor
{
    private static Dictionary<string, object> DimensionStyleProperties() => new()
    {
        ["style"] = new { type = "string", @enum = new[] { "ISO", "Fine", "Large" } },
        ["shape_font"] = new { type = "string", @enum = CadShapeFontRegistry.Defaults.Select(f => f.Id.Value).ToArray() },
        ["arrow"] = new { type = "string", @enum = Enum.GetNames<CadDimensionArrow>() },
        ["unit"] = new { type = "string", @enum = Enum.GetNames<CadUnit>() },
        ["precision"] = new { type = "integer", minimum = 0, maximum = 8 },
        ["text_height"] = new { type = "number", exclusiveMinimum = 0 },
        ["arrow_size"] = new { type = "number", exclusiveMinimum = 0 },
        ["extension_gap"] = new { type = "number", minimum = 0 },
        ["extension_beyond"] = new { type = "number", minimum = 0 },
        ["line_weight"] = new { type = "number", exclusiveMinimum = 0 },
        ["annotation_scale"] = new { type = "number", exclusiveMinimum = 0, maximum = 1e6 },
        ["text_override"] = new { type = new[] { "string", "null" }, maxLength = 4096 },
        ["rotation_degrees"] = Number("Linear dimension rotation in counter-clockwise degrees")
    };

    private static object SetDimensionSchema()
    {
        var properties = DimensionStyleProperties();
        properties["entity_id"] = new { type = "integer", minimum = 1 };
        properties["x"] = Number("Placement X in millimetres; y is required together");
        properties["y"] = Number("Placement Y in millimetres; x is required together");
        return new { type = "object", properties, required = new[] { "entity_id" }, additionalProperties = false,
            anyOf = properties.Keys.Where(k => k != "entity_id").Select(k => new { required = new[] { k } }).ToArray(),
            dependentRequired = new { x = new[] { "y" }, y = new[] { "x" } } };
    }

    private static object DimensionSchema()
    {
        var properties = DimensionStyleProperties();
        foreach (var entry in new Dictionary<string, object>
        {
            ["kind"]=new{type="string",@enum=Enum.GetNames<CadDimensionKind>()},
            ["source_entity_id"]=new{type="integer",minimum=1},
            ["anchors"]=new{type="array",minItems=2,maxItems=3,items=new{type="object",properties=new{x=new{type="number"},y=new{type="number"}},required=new[]{"x","y"},additionalProperties=false}},
            ["x"]=Number("Annotation position X"),["y"]=Number("Annotation position Y"),
        }) properties[entry.Key] = entry.Value;
        return new { type="object", properties, required=new[]{"kind","x","y"}, additionalProperties=false,
            oneOf = new[] { new { required = new[] { "source_entity_id" } }, new { required = new[] { "anchors" } } } };
    }
    private string AddDimension(JsonElement args)
    {
        if(!Enum.TryParse<CadDimensionKind>(args.GetProperty("kind").GetString(),out var kind) || !Enum.IsDefined(kind))throw new ArgumentException("Unknown dimension kind.");
        if (args.TryGetProperty("source_entity_id", out _) == args.TryGetProperty("anchors", out _))
            throw new ArgumentException("Supply either source_entity_id or anchors.");
        var doc=documentViewModel.CadEditor.Document;CadDimensionAnchor[] anchors;
        if(args.TryGetProperty("source_entity_id",out var source))
        {
            var entity=doc.GetEntity(new(source.GetInt64()));
            if(entity.IsErased || entity.OwnerBlockId!=documentViewModel.CadEditor.ActiveOwnerBlockId)throw new ArgumentException("Source must be live in the current space.");
            CadDimensionAnchor Anchor(CadPointD p,CadReferenceFeature f)=>new(p,new(entity.Id.Value,entity.OwnerBlockId.Value,f));
            anchors=entity switch
            {
                CadLine line when kind is CadDimensionKind.LinearX or CadDimensionKind.LinearY or CadDimensionKind.Aligned=>[Anchor(line.Start,CadReferenceFeature.LineStart),Anchor(line.End,CadReferenceFeature.LineEnd)],
                CadCircle circle when kind is CadDimensionKind.Radius or CadDimensionKind.Diameter=>[Anchor(circle.Center,CadReferenceFeature.CircleCenter),Anchor(circle.Center+new CadVectorD(circle.Radius,0),CadReferenceFeature.CirclePoint)],
                CadArc arc when kind is CadDimensionKind.Radius or CadDimensionKind.Diameter=>[Anchor(arc.Center,CadReferenceFeature.CircleCenter),Anchor(arc.Center+new CadVectorD(arc.Radius,0),CadReferenceFeature.CirclePoint)],
                _=>throw new ArgumentException("Source geometry does not match dimension kind.")
            };
        }
        else anchors=args.GetProperty("anchors").EnumerateArray().Select(p=>new CadDimensionAnchor(new(EditNumber(p,"x"),EditNumber(p,"y")))).ToArray();
        var definition = ApplyDimensionProperties(args, new CadDimensionDefinition(kind, anchors,
            new(EditNumber(args,"x"),EditNumber(args,"y")), CadDimensionStyles.Get("ISO", doc.DocumentSettings.Unit)));
        var command=new AddDimensionCommand(definition,documentViewModel.DrawingLayerId,documentViewModel.CadEditor.ActiveOwnerBlockId);
        ExecuteCommand(command);var d=(CadDimension)doc.GetEntity(command.CreatedEntityId!.Value);
        return Success(new{entity_id=d.Id.Value,measurement=d.Measurement,text=d.DisplayText,association=d.AssociationState.ToString()});
    }

    private string SetDimension(JsonElement args)
    {
        var dimension = RequireDimension(args);
        var supported = DimensionStyleProperties().Keys.Concat(["entity_id", "document_id", "x", "y"]).ToHashSet(StringComparer.Ordinal);
        if (args.EnumerateObject().Any(p => !supported.Contains(p.Name))) throw new ArgumentException("Unknown dimension property. Type TOOLHELP set_dimension.");
        if (!args.EnumerateObject().Any(p => p.Name is not ("entity_id" or "document_id")))
            throw new ArgumentException("Supply at least one dimension property.");
        var definition = ApplyDimensionProperties(args, dimension.Definition);
        definition.Validate();
        ExecuteCommand(new SetDimensionCommand(dimension.Id, definition));
        return Success(new { entity_id = dimension.Id.Value, measurement = dimension.Measurement,
            text = dimension.DisplayText, association = dimension.AssociationState.ToString() });
    }

    private string DetachDimension(JsonElement args)
    {
        var dimension = RequireDimension(args);
        ExecuteCommand(new SetDimensionCommand(dimension.Id, dimension.Definition with
        { Anchors = dimension.Definition.Anchors.Select(a => a with { Reference = null }).ToArray() }));
        return Success(new { entity_id = dimension.Id.Value, association = dimension.AssociationState.ToString() });
    }

    private CadDimension RequireDimension(JsonElement args) =>
        GetEntityForTool(new(args.GetProperty("entity_id").GetInt64())) as CadDimension ??
        throw new ArgumentException("entity_id must identify a dimension in the current editing space.");

    private static CadDimensionDefinition ApplyDimensionProperties(JsonElement args, CadDimensionDefinition original)
    {
        T EnumValue<T>(string key, T fallback) where T : struct, Enum => !args.TryGetProperty(key, out var value) ? fallback :
            Enum.TryParse<T>(value.GetString(), true, out var parsed) && Enum.IsDefined(parsed) ? parsed :
            throw new ArgumentException($"Unknown {key}.");
        var style = original.Style;
        if (args.TryGetProperty("style", out var preset))
        {
            if (preset.GetString() is not ("ISO" or "Fine" or "Large")) throw new ArgumentException("Unknown dimension style preset.");
            style = CadDimensionStyles.Get(preset.GetString()!, original.Style.Unit);
        }
        style = style with
        {
            Unit = EnumValue("unit", style.Unit), Precision = args.TryGetProperty("precision", out var precision) ? precision.GetInt32() : style.Precision,
            ShapeFont = args.TryGetProperty("shape_font", out var font) ? font.GetString()! : style.ShapeFont,
            Arrow = EnumValue("arrow", style.Arrow), TextHeight = DimensionNumber(args,"text_height",style.TextHeight),
            ArrowSize = DimensionNumber(args,"arrow_size",style.ArrowSize), ExtensionGap = DimensionNumber(args,"extension_gap",style.ExtensionGap),
            ExtensionBeyond = DimensionNumber(args,"extension_beyond",style.ExtensionBeyond), LineWeight = DimensionNumber(args,"line_weight",style.LineWeight)
        };
        var hasX = args.TryGetProperty("x", out _); var hasY = args.TryGetProperty("y", out _);
        if (hasX != hasY) throw new ArgumentException("Supply both x and y for dimension placement.");
        return original with { Style = style, Placement = hasX ? new(EditNumber(args,"x"), EditNumber(args,"y")) : original.Placement,
            AnnotationScale = DimensionNumber(args,"annotation_scale",original.AnnotationScale),
            TextOverride = args.TryGetProperty("text_override",out var text) ? text.GetString() : original.TextOverride,
            LinearRotationRadians = DimensionNumber(args,"rotation_degrees",original.LinearRotationRadians * 180 / Math.PI) * Math.PI / 180 };
    }

    private static double DimensionNumber(JsonElement args, string key, double fallback)
    {
        if (!args.TryGetProperty(key, out var value)) return fallback;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
            throw new ArgumentException($"{key} must be a finite number.");
        return number;
    }
}
