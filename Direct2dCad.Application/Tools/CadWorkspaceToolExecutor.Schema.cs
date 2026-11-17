using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Application.Platform;
using Direct2dCad.IO;

using MessagePipe;


namespace Direct2dCad.Application.Tools;

/// <summary>
/// Routes one tool execution session across the open-document workspace. Each touched document
/// receives its own command batch so its undo history remains independent.
/// </summary>

public sealed partial class CadWorkspaceToolExecutor
{
    private static IReadOnlyList<AiToolDefinition> BuildToolDefinitions()
    {
        var tools = CadDocumentToolExecutor.ToolDefinitions.Select(AddDocumentAndAppearanceParameters).ToList();
        tools.AddRange(WorkspaceToolDefinitions());
        tools.Add(Tool("boolean_regions", "Create one undoable Region from at least two explicit closed operands: union, intersection, or difference. Difference requires subject_entity_id; sources are replaced only after a non-empty result succeeds. Supports circles, ellipses, rectangles, closed line/circular paths and Regions including exact elliptical arcs; unsupported geometry fails without edits.", BooleanRegionsSchema()));
        tools.Add(Tool("insert_image_from_file", "Import an image file and add it as an undoable CadImage in the active editing space. The image is stored in the document; it is not a live external link.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["file_path"] = StringSchema("Absolute image file path"),
                ["bounds"] = RectSchema("Target CAD world-coordinate image frame"),
                ["layer"] = StringSchema("Existing target layer name or ID"),
                ["name"] = StringSchema("Optional image entity name"),
                ["rotation_degrees"] = NumberSchema("Counter-clockwise image rotation"),
                ["opacity"] = new { type = "number", minimum = 0.0, maximum = 1.0 },
                ["z_index"] = new { type = "integer" },
                ["visible"] = new { type = "boolean" },
                ["locked"] = new { type = "boolean" }
            }, ["file_path", "bounds"])));
        tools.Add(Tool("add_ole_object", "Add an embedded OLE object from persisted OLE storage bytes as an undoable CadOleObject.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["ole_base64"] = Base64Schema("Base64-encoded persisted OLE storage"),
                ["bounds"] = RectSchema("Target CAD world-coordinate OLE frame"),
                ["layer"] = StringSchema("Existing target layer name or ID"),
                ["name"] = StringSchema("Optional OLE entity name"),
                ["source_name"] = StringSchema("Optional source or application name"),
                ["content_type"] = StringSchema("Optional OLE content type"),
                ["opacity"] = new { type = "number", minimum = 0.0, maximum = 1.0 },
                ["z_index"] = new { type = "integer" },
                ["visible"] = new { type = "boolean" },
                ["locked"] = new { type = "boolean" }
            }, ["ole_base64", "bounds"])));
        tools.AddRange(CadGeometryTools.ToolDefinitions);
        tools.Add(CadEntitySpecificPropertyTools.ToolDefinition);
        tools.Add(Tool("set_ole_object_data", "Replace persisted OLE storage bytes as one undoable entity update.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["entity_id"] = new { type = "integer", minimum = 1 },
                ["ole_base64"] = Base64Schema("Base64-encoded persisted OLE storage"),
                ["source_name"] = StringSchema("Optional source or application name"),
                ["content_type"] = StringSchema("Optional OLE content type")
            }, ["entity_id", "ole_base64"])));
        tools.Add(CadTextStyleTools.ToolDefinition);
        tools.Add(CadGraphicStyleTools.ToolDefinition);
        tools.AddRange(CadStyleManagementTools.ToolDefinitions);
        tools.AddRange(CadLayerTools.ToolDefinitions);
        tools.Add(CadBulkCreationTools.ToolDefinition);
        tools.Add(CadCompositePathTools.ToolDefinition);
        tools.Add(Tool("list_document_catalog", "List layers and reusable graphic, fill, hatch, and text styles for a document.",
            ObjectSchema(new Dictionary<string, object> { ["document_id"] = DocumentIdSchema() })));
        tools.Add(Tool("set_entity_common_properties", "Set undoable common entity properties in one document batch. color and graphic_style are mutually exclusive; use one or the other.",
            ObjectSchema(
                CommonPropertySchema(includeEntityIds: true),
                ["entity_ids"],
                not: MutuallyExclusive("color", "graphic_style"),
                allOf: [AtLeastOneRequired("layer", "name", "color_source", "color", "line_weight", "graphic_style", "z_index", "visible", "locked")] )));
        tools.Add(Tool("set_entity_fill", "Set no fill, an existing fill style, solid fill, hatch fill, or gradient fill on fill-capable entities.",
            ObjectSchema(
                FillSchema(includeEntityIds: true),
                ["entity_ids", "mode"],
                allOf: FillRequirements())));
        tools.Add(Tool("set_entity_stroke_style", "Set one or more stroke cap, dash, or join properties while preserving omitted values. Query stroke_style capability first; start/end caps and line_join are entity-type dependent.",
            ObjectSchema(
                StrokeSchema(includeEntityIds: true),
                ["entity_ids"],
                allOf: [AtLeastOneRequired("start_cap", "end_cap", "dash_cap", "dash_style", "line_join")] )));
        tools.Add(Tool("create_block", "Create an undoable reusable Block from entities in the active drawing space and replace them with one reference. Uses the current selection when entity_ids is omitted.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["entity_ids"] = EntityIdsSchema(),
                ["name"] = StringSchema("Unique Block definition name"),
                ["base_x"] = new { type = "number" },
                ["base_y"] = new { type = "number" },
                ["reference_layer"] = StringSchema("Existing layer name or ID for the created reference"),
                ["reference_name"] = StringSchema("Optional name for the created reference entity"),
                ["color_source"] = EnumSchema("by_layer", "explicit", "by_block"),
                ["color"] = StringSchema("Reference stroke color"),
                ["line_weight"] = new
                {
                    oneOf = new object[]
                    {
                        new { type = "number", exclusiveMinimum = 0.0 },
                        new { type = "string", @enum = new[] { "by_layer" } }
                    }
                },
                ["graphic_style"] = StringSchema("Existing graphic style name or ID; use none to clear"),
                ["z_index"] = new { type = "integer" },
                ["visible"] = new { type = "boolean" },
                ["locked"] = new { type = "boolean" }
            }, ["name", "base_x", "base_y"], not: MutuallyExclusive("color", "graphic_style"))));
        tools.Add(Tool("insert_block", "Insert an undoable reference to an existing user Block.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["block"] = StringSchema("Existing user Block name or ID"),
                ["x"] = new { type = "number" },
                ["y"] = new { type = "number" },
                ["layer"] = StringSchema("Existing target layer name or ID"),
                ["rotation_degrees"] = new { type = "number" },
                ["scale_x"] = new { type = "number" },
                ["scale_y"] = new { type = "number" },
                ["name"] = StringSchema("Optional Block reference entity name"),
                ["color_source"] = EnumSchema("by_layer", "explicit", "by_block"),
                ["color"] = StringSchema("Reference stroke color"),
                ["line_weight"] = new
                {
                    oneOf = new object[]
                    {
                        new { type = "number", exclusiveMinimum = 0.0 },
                        new { type = "string", @enum = new[] { "by_layer" } }
                    }
                },
                ["graphic_style"] = StringSchema("Existing graphic style name or ID; use none to clear"),
                ["z_index"] = new { type = "integer" },
                ["visible"] = new { type = "boolean" },
                ["locked"] = new { type = "boolean" }
            }, ["block", "x", "y"], not: MutuallyExclusive("color", "graphic_style"))));
        tools.Add(Tool("list_blocks", "List user block definitions, their base points, entity counts, and reference counts.",
            ObjectSchema(new Dictionary<string, object> { ["document_id"] = DocumentIdSchema() })));
        tools.Add(Tool("rename_block", "Rename an existing user block definition as one undoable operation.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["block"] = StringSchema("Existing user Block name or ID"),
                ["new_name"] = StringSchema("New unique Block name")
            }, ["block", "new_name"])));
        tools.Add(Tool("delete_block", "Delete an unreferenced user block definition and its owned entities. Requires confirm=true.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["block"] = StringSchema("Existing user Block name or ID"),
                ["confirm"] = new { type = "boolean", @const = true }
            }, ["block", "confirm"])));
        tools.Add(Tool("edit_block", "Enter the selected user block definition as the active editable space.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(),
                ["block"] = StringSchema("Existing user Block name or ID")
            }, ["block"])));
        tools.Add(Tool("exit_block_edit", "Leave block editing and return to model space.",
            ObjectSchema(new Dictionary<string, object> { ["document_id"] = DocumentIdSchema() })));
        AddPresentationToolDefinitions(tools);
        tools.Add(Tool("set_block_base_point", "Change a user block definition's base point with undo. Coordinates are millimetres. Existing block references follow the CAD definition semantics.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(), ["block"] = StringSchema("User block name or ID"),
                ["x"] = NumberSchema("Base point X"), ["y"] = NumberSchema("Base point Y")
            }, ["block", "x", "y"])));
        tools.AddRange(CadApplicationSettingsTools.ToolDefinitions);
        var clipboardImageSchema = JsonNode.Parse(tools.Single(tool => tool.Name == "insert_image_from_file").Parameters.GetRawText())!.AsObject();
        clipboardImageSchema["properties"]!.AsObject().Remove("file_path");
        clipboardImageSchema["required"] = new JsonArray("bounds");
        tools.Add(Tool("insert_image_from_clipboard", "Import the current system clipboard image into an undoable CadImage at explicit world bounds. Fails without creating geometry if no image is available.", clipboardImageSchema));
        return tools;
    }

    private static AiToolDefinition AddDocumentAndAppearanceParameters(AiToolDefinition definition)
    {
        var schema = JsonNode.Parse(definition.Parameters.GetRawText())!.AsObject();
        var properties = schema["properties"]!.AsObject();
        properties["document_id"] = Direct2dCad.AI.Contracts.CadJson.SerializeToNode(DocumentIdSchema());
        if (CreationToolNames.Contains(definition.Name))
        {
            foreach (var (name, value) in CommonPropertySchema(includeEntityIds: false))
                if (name is not ("layer" or "name"))
                    properties[name] = Direct2dCad.AI.Contracts.CadJson.SerializeToNode(value);
            if (definition.Name is not ("add_text" or "add_shape_text"))
                properties["stroke_style"] = Direct2dCad.AI.Contracts.CadJson.SerializeToNode(
                    ObjectSchema(CreationStrokeSchema(definition.Name)));
            if (definition.Name is "add_circle" or "add_ellipse" or "add_rectangle" or "add_polygon" or "add_polyline" or "add_spline" or "add_composite_path")
                properties["fill"] = Direct2dCad.AI.Contracts.CadJson.SerializeToNode(ObjectSchema(
                    FillSchema(false),
                    ["mode"],
                    allOf: FillRequirements()));
            if (definition.Name == "add_text")
            {
                properties["text_style"] = Direct2dCad.AI.Contracts.CadJson.SerializeToNode(StringSchema("Existing Text style name or ID; none selects the default"));
                properties["font_family"] = Direct2dCad.AI.Contracts.CadJson.SerializeToNode(StringSchema("Font family; creates or reuses a matching Text style"));
                properties["inverted"] = Direct2dCad.AI.Contracts.CadJson.SerializeToNode(new { type = "boolean" });
                properties["inverted_margin_factor"] = Direct2dCad.AI.Contracts.CadJson.SerializeToNode(new { type = "number", minimum = 0.0 });
            }
            else if (definition.Name == "add_shape_text")
            {
                properties["shape_font"] = Direct2dCad.AI.Contracts.CadJson.SerializeToNode(StringSchema("Shape font ID or name"));
                properties["inverted"] = Direct2dCad.AI.Contracts.CadJson.SerializeToNode(new { type = "boolean" });
                properties["inverted_margin_factor"] = Direct2dCad.AI.Contracts.CadJson.SerializeToNode(new { type = "number", minimum = 0.0 });
            }
        }

        if (CreationToolNames.Contains(definition.Name))
        {
            schema["not"] = Direct2dCad.AI.Contracts.CadJson.SerializeToNode(MutuallyExclusive("color", "graphic_style"));
            AddCreationSchemaRules(schema, definition.Name);
        }

        return new AiToolDefinition(
            definition.Name,
            definition.Description,
            Direct2dCad.AI.Contracts.CadJson.SerializeToElement(schema));
    }

    private static Dictionary<string, object> CreationStrokeSchema(string toolName)
    {
        var properties = StrokeSchema(false);
        if (toolName is "add_circle" or "add_ellipse" or "add_rectangle" or "add_polygon")
        {
            properties.Remove("start_cap");
            properties.Remove("end_cap");
        }
        if (toolName is "add_line" or "add_circle" or "add_ellipse" or "add_arc" or "add_ellipse_arc")
            properties.Remove("line_join");
        return properties;
    }

    private static void AddCreationSchemaRules(JsonObject schema, string toolName)
    {
        if (toolName is not ("add_polyline" or "add_spline" or "add_composite_path"))
            return;

        var allOf = schema["allOf"] as JsonArray ?? [];
        allOf.Add(Direct2dCad.AI.Contracts.CadJson.SerializeToNode(new Dictionary<string, object>
        {
            ["if"] = new Dictionary<string, object>
            {
                ["properties"] = new Dictionary<string, object>
                {
                    ["closed"] = new Dictionary<string, object> { ["const"] = true }
                },
                ["required"] = new[] { "closed" }
            },
            ["then"] = new Dictionary<string, object>
            {
                ["properties"] = new Dictionary<string, object>
                {
                    ["stroke_style"] = new Dictionary<string, object>
                    {
                        ["not"] = new Dictionary<string, object>
                        {
                            ["anyOf"] = new object[]
                            {
                                new Dictionary<string, object> { ["required"] = new[] { "start_cap" } },
                                new Dictionary<string, object> { ["required"] = new[] { "end_cap" } }
                            }
                        }
                    }
                }
            }
        }));
        allOf.Add(Direct2dCad.AI.Contracts.CadJson.SerializeToNode(new Dictionary<string, object>
        {
            ["if"] = new Dictionary<string, object>
            {
                ["anyOf"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["not"] = new Dictionary<string, object>
                        {
                            ["required"] = new[] { "closed" }
                        }
                    },
                    new Dictionary<string, object>
                    {
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["closed"] = new Dictionary<string, object> { ["const"] = false }
                        },
                        ["required"] = new[] { "closed" }
                    }
                }
            },
            ["then"] = new Dictionary<string, object>
            {
                ["not"] = new Dictionary<string, object>
                {
                    ["required"] = new[] { "fill" }
                }
            }
        }));
        schema["allOf"] = allOf;
    }

    private static IEnumerable<AiToolDefinition> WorkspaceToolDefinitions()
    {
        yield return Tool("get_agent_capabilities", "Return the CAD Agent Contract: coordinate rules, editable-space rules, undo and selection behavior, tool groups, and examples.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["include_examples"] = new { type = "boolean", description = "Include representative tool-call examples; defaults to true." }
            }));
        yield return Tool("list_documents", "List all open CAD documents and their stable document_id values.",
            ObjectSchema(new Dictionary<string, object>()));
        yield return Tool("create_document", "Create and activate a new CAD document, optionally from an engineering template. Use list_document_templates to discover keys.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["name"] = StringSchema("Optional document name"),
                ["template"] = new { type = "string", @enum = CadEngineeringTemplates.Keys }
            }));
        yield return Tool("open_document", "Open and activate a .d2cad file from an absolute path.",
            ObjectSchema(new Dictionary<string, object> { ["file_path"] = StringSchema("Absolute .d2cad file path") }, ["file_path"]));
        yield return Tool("open_dxf", "Import a bounded DXF into a new unsaved document. Units must be declared or explicitly supplied.",
            ObjectSchema(new Dictionary<string,object>{["file_path"]=StringSchema("Absolute DXF path"),["source_unit"]=new{type="string",@enum=Enum.GetNames<Direct2dCad.Db.Cad.Settings.CadUnit>().Where(u=>u!="Unitless").ToArray()}},["file_path"]));
        yield return Tool("export_dxf", "Export a snapshot to a separate DXF. Returns losses without writing until allow_loss is true; existing files require overwrite=true.",
            ObjectSchema(new Dictionary<string,object>{["file_path"]=StringSchema("Absolute DXF destination"),["document_id"]=StringSchema("Open document ID"),["allow_loss"]=new{type="boolean"},["overwrite"]=new{type="boolean"}},["file_path"]));
        yield return Tool("activate_document", "Activate an open CAD document.",
            ObjectSchema(new Dictionary<string, object> { ["document_id"] = DocumentIdSchema() }, ["document_id"]));
        yield return Tool("rename_document", "Rename an open CAD document. This changes document metadata, not drawing command history.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(), ["name"] = StringSchema("New document name")
            }, ["name"]));
        yield return Tool("save_document", "Save a document. interactive=false requires file_path for a new drawing and returns errors without dialogs. overwrite=true explicitly authorizes replacing the destination revision captured at call time; expected_sha256 optionally restricts that authorization.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(), ["file_path"] = StringSchema("Optional absolute destination path"),
                ["interactive"] = new { type = "boolean", description = "Allow the save-path chooser for a new drawing. Defaults to true for compatibility." },
                ["overwrite"] = new { type = "boolean" }, ["expected_sha256"] = StringSchema("Expected existing destination SHA-256. Requires overwrite=true.")
            }, allOf: OverwriteRequirements()));
        yield return Tool("close_document", "Close a document. on_unsaved defaults to ask for compatibility. save, discard and cancel are dialog-free decisions. save requires file_path for an unsaved drawing. Failed saves leave the document open.",
            ObjectSchema(new Dictionary<string, object>
            {
                ["document_id"] = DocumentIdSchema(), ["on_unsaved"] = EnumSchema("ask", "save", "discard", "cancel"),
                ["file_path"] = StringSchema("Save destination when on_unsaved=save"),
                ["overwrite"] = new { type = "boolean" }, ["expected_sha256"] = StringSchema("Expected existing destination SHA-256. Requires overwrite=true.")
            }, allOf: OverwriteRequirements()));
        foreach (var definition in WorkspaceOperationsToolDefinitions()) yield return definition;
    }

    private static Dictionary<string, object> CommonPropertySchema(bool includeEntityIds)
    {
        var properties = new Dictionary<string, object>();
        if (includeEntityIds)
        {
            properties["document_id"] = DocumentIdSchema();
            properties["entity_ids"] = EntityIdsSchema();
            properties["layer"] = StringSchema("Existing layer name or ID");
            properties["name"] = StringSchema("Entity name; only valid for one entity");
        }
        properties["color_source"] = EnumSchema("by_layer", "explicit", "by_block");
        properties["color"] = StringSchema("#RRGGBB, #AARRGGBB, or black/white/red/green/blue/transparent");
        properties["line_weight"] = new
        {
            oneOf = new object[]
            {
                new { type = "number", exclusiveMinimum = 0.0 },
                new { type = "string", @enum = new[] { "by_layer" } }
            }
        };
        properties["graphic_style"] = StringSchema("Existing graphic style name or ID; use none to clear");
        properties["z_index"] = new { type = "integer" };
        properties["visible"] = new { type = "boolean" };
        properties["locked"] = new { type = "boolean", description = "Prevent direct entity edits when true; unlocking is undoable." };
        return properties;
    }

    private static Dictionary<string, object> FillSchema(bool includeEntityIds)
    {
        var properties = new Dictionary<string, object>();
        if (includeEntityIds)
        {
            properties["document_id"] = DocumentIdSchema();
            properties["entity_ids"] = EntityIdsSchema();
        }
        properties["mode"] = EnumSchema("none", "style", "solid", "hatch", "gradient");
        properties["style"] = StringSchema("Existing fill style name or ID when mode is style");
        properties["color"] = StringSchema("Solid or hatch foreground color");
        properties["pattern"] = StringSchema("Hatch pattern name or ID; defaults to ANSI31");
        properties["scale"] = new { type = "number", exclusiveMinimum = 0.0 };
        properties["angle_degrees"] = new { type = "number" };
        properties["origin_x"] = new { type = "number" };
        properties["origin_y"] = new { type = "number" };
        properties["gradient_kind"] = EnumSchema("linear", "radial");
        properties["stops"] = new
        {
            type = "array",
            minItems = 2,
            items = new
            {
                type = "object",
                properties = new
                {
                    offset = new { type = "number", minimum = 0.0, maximum = 1.0 },
                    color = StringSchema("Stop color: #RRGGBB, #AARRGGBB, or a named protocol color")
                },
                required = new[] { "offset", "color" },
                additionalProperties = false
            }
        };
        properties["gradient_scale"] = new { type = "number", exclusiveMinimum = 0.0 };
        properties["gradient_angle_degrees"] = new { type = "number" };
        properties["gradient_origin_x"] = new { type = "number" };
        properties["gradient_origin_y"] = new { type = "number" };
        properties["gradient_centered"] = new { type = "boolean" };
        return properties;
    }

    private static object[] FillRequirements() =>

    [
        new Dictionary<string, object>
        {
            ["if"] = new Dictionary<string, object>
            {
                ["properties"] = new Dictionary<string, object>
                {
                ["mode"] = new Dictionary<string, object> { ["const"] = "style" }
                }
            },
            ["then"] = new Dictionary<string, object> { ["required"] = new[] { "style" } }
        },
        new Dictionary<string, object>
        {
            ["if"] = new Dictionary<string, object>
            {
                ["properties"] = new Dictionary<string, object>
                {
                    ["mode"] = new Dictionary<string, object> { ["const"] = "gradient" }
                }
            },
            ["then"] = new Dictionary<string, object> { ["required"] = new[] { "stops" } }
        },
        FillModeForbids("none", "style", "color", "pattern", "scale", "angle_degrees", "origin_x", "origin_y", "gradient_kind", "stops", "gradient_scale", "gradient_angle_degrees", "gradient_origin_x", "gradient_origin_y", "gradient_centered"),
        FillModeForbids("style", "color", "pattern", "scale", "angle_degrees", "origin_x", "origin_y", "gradient_kind", "stops", "gradient_scale", "gradient_angle_degrees", "gradient_origin_x", "gradient_origin_y", "gradient_centered"),
        FillModeForbids("solid", "style", "pattern", "scale", "angle_degrees", "origin_x", "origin_y", "gradient_kind", "stops", "gradient_scale", "gradient_angle_degrees", "gradient_origin_x", "gradient_origin_y", "gradient_centered"),
        FillModeForbids("hatch", "style", "gradient_kind", "stops", "gradient_scale", "gradient_angle_degrees", "gradient_origin_x", "gradient_origin_y", "gradient_centered"),
        FillModeForbids("gradient", "style", "color", "pattern", "scale", "angle_degrees", "origin_x", "origin_y")
    ];

    private static object FillModeForbids(string mode, params string[] properties) => new Dictionary<string, object>
    {
        ["if"] = new Dictionary<string, object>
        {
            ["properties"] = new Dictionary<string, object>
            {
                ["mode"] = new Dictionary<string, object> { ["const"] = mode }
            }
        },
        ["then"] = new Dictionary<string, object>
        {
            ["not"] = new Dictionary<string, object>
            {
                ["anyOf"] = properties.Select(property => (object)new Dictionary<string, object>
                {
                    ["required"] = new[] { property }
                }).ToArray()
            }
        }
    };

    private static Dictionary<string, object> StrokeSchema(bool includeEntityIds)
    {
        var properties = new Dictionary<string, object>();
        if (includeEntityIds)
        {
            properties["document_id"] = DocumentIdSchema();
            properties["entity_ids"] = EntityIdsSchema();
        }
        properties["start_cap"] = new
        {
            type = "string",
            @enum = new[] { "flat", "square", "round", "triangle" },
            description = "Open-curve start cap only. Do not use for circles, ellipses, polygons, rectangles, or closed paths."
        };
        properties["end_cap"] = new
        {
            type = "string",
            @enum = new[] { "flat", "square", "round", "triangle" },
            description = "Open-curve end cap only. Do not use for circles, ellipses, polygons, rectangles, or closed paths."
        };
        properties["dash_cap"] = new
        {
            type = "string",
            @enum = new[] { "flat", "square", "round", "triangle" },
            description = "Dash cap; available on stroked curve entities."
        };
        properties["dash_style"] = new
        {
            type = "string",
            @enum = new[] { "solid", "dash", "dot", "dash_dot", "dash_dot_dot" },
            description = "Dash pattern for stroked curve entities."
        };
        properties["line_join"] = new
        {
            type = "string",
            @enum = new[] { "miter", "bevel", "round", "miter_or_bevel" },
            description = "Join style for rectangles, polygons, polylines, splines, and composite paths."
        };
        return properties;
    }

    private static object ObjectSchema(
        IReadOnlyDictionary<string, object> properties,
        IReadOnlyList<string>? required = null,
        object? not = null,
        IReadOnlyList<object>? allOf = null)
    {
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required ?? [],
            ["additionalProperties"] = false
        };
        if (not is not null)
            schema["not"] = not;
        if (allOf is { Count: > 0 })
            schema["allOf"] = allOf;
        return schema;
    }

    private static object AtLeastOneRequired(params string[] properties) => new
    {
        anyOf = properties.Select(property => new
        {
            required = new[] { property }
        }).ToArray()
    };

    private static object MutuallyExclusive(params string[] properties) => new Dictionary<string, object>
    {
        ["required"] = properties
    };

    private static object RectDto(CadRectD bounds) => new
    {
        min_x = bounds.MinX,
        min_y = bounds.MinY,
        max_x = bounds.MaxX,
        max_y = bounds.MaxY
    };

    private static object DocumentIdSchema() => StringSchema("Stable open-document ID from list_documents");

    private static object StringSchema(string description) => new { type = "string", description };

    private static object Base64Schema(string description) => new
    {
        type = "string",
        description,
        contentEncoding = "base64",
        minLength = 4
    };

    private static object NumberSchema(string description) => new { type = "number", description };

    private static object EnumSchema(params string[] values) => new { type = "string", @enum = values };

    private static object RectSchema(string description) => new
    {
        type = "object",
        description,
        properties = new
        {
            min_x = NumberSchema("Minimum X"),
            min_y = NumberSchema("Minimum Y"),
            max_x = NumberSchema("Maximum X"),
            max_y = NumberSchema("Maximum Y")
        },
        required = new[] { "min_x", "min_y", "max_x", "max_y" },
        additionalProperties = false
    };

    private static object EntityIdsSchema() => new
    {
        type = "array",
        minItems = 1,
        uniqueItems = true,
        items = new { type = "integer" }
    };

    private static AiToolDefinition Tool(string name, string description, object parameters) =>
        new(name, description, Direct2dCad.AI.Contracts.CadJson.SerializeToElement(parameters));
}
