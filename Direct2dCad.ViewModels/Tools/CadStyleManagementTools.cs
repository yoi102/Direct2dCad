using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Microsoft.Win32;

namespace Direct2dCad.ViewModels.Tools;

internal static class CadStyleManagementTools
{
    internal static IReadOnlyList<AiToolDefinition> ToolDefinitions { get; } =
    [
        Tool("list_styles", "List all document styles and reference counts.", ObjectSchema(
            new Dictionary<string, object> { ["document_id"] = StringSchema("Stable open-document ID") })),
        Tool("create_graphic_style", "Create a shared graphic style as one undoable operation.", ObjectSchema(
            new Dictionary<string, object>
            {
                ["document_id"] = StringSchema("Stable open-document ID"),
                ["name"] = StringSchema("Unique style name"),
                ["color"] = StringSchema("Stroke color"),
                ["line_weight"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["line_type_id"] = new { type = "integer", minimum = 1 }
            }, ["name", "color"])),
        Tool("create_line_type", "Create a reusable custom line type. Positive dash values draw and negative values leave gaps.", ObjectSchema(
            new Dictionary<string, object>
            {
                ["document_id"] = StringSchema("Stable open-document ID"),
                ["name"] = StringSchema("Unique line type name"),
                ["description"] = StringSchema("Optional description"),
                ["dash_pattern"] = new { type = "array", items = new { type = "number" } }
            }, ["name"])),
        Tool("rename_line_type", "Rename an existing line type as one undoable operation.", ObjectSchema(
            new Dictionary<string, object>
            {
                ["document_id"] = StringSchema("Stable open-document ID"),
                ["line_type"] = StringSchema("Existing line type name or ID"),
                ["new_name"] = StringSchema("New unique line type name")
            }, ["line_type", "new_name"])),
        Tool("delete_line_type", "Delete an unreferenced custom line type. Requires confirm=true.", ObjectSchema(
            new Dictionary<string, object>
            {
                ["document_id"] = StringSchema("Stable open-document ID"),
                ["line_type"] = StringSchema("Existing line type name or ID"),
                ["confirm"] = new { type = "boolean", @const = true }
            }, ["line_type", "confirm"])),
        Tool("create_text_style", "Create a shared text style as one undoable operation.", ObjectSchema(
            new Dictionary<string, object>
            {
                ["document_id"] = StringSchema("Stable open-document ID"),
                ["name"] = StringSchema("Unique style name"),
                ["font_family"] = StringSchema("Installed font family"),
                ["text_height"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["width_factor"] = new { type = "number", exclusiveMinimum = 0.0 },
                ["oblique_angle_degrees"] = new { type = "number" },
                ["bold"] = new { type = "boolean" },
                ["italic"] = new { type = "boolean" }
            }, ["name", "font_family"])),
        Tool("create_fill_style", "Create a solid, hatch, or gradient fill style as one undoable operation.", FillCreationSchema()),
        Tool("create_hatch_pattern", "Create a custom hatch pattern from line definitions as one undoable operation.", ObjectSchema(
            new Dictionary<string, object>
            {
                ["document_id"] = StringSchema("Stable open-document ID"),
                ["name"] = StringSchema("Unique pattern name"),
                ["description"] = StringSchema("Optional description"),
                ["lines"] = new { type = "array", minItems = 1 }
            }, ["name", "lines"])),
        Tool("rename_style", "Rename a style while preserving all references.", ObjectSchema(
            new Dictionary<string, object>
            {
                ["document_id"] = StringSchema("Stable open-document ID"),
                ["style"] = StringSchema("Existing style name or ID"),
                ["new_name"] = StringSchema("New unique style name")
            }, ["style", "new_name"])),
        Tool("delete_style", "Delete an unreferenced style. Requires confirm=true.", ObjectSchema(
            new Dictionary<string, object>
            {
                ["document_id"] = StringSchema("Stable open-document ID"),
                ["style"] = StringSchema("Existing style name or ID"),
                ["confirm"] = new { type = "boolean", @const = true }
            }, ["style", "confirm"])),
        Tool("delete_hatch_pattern", "Delete an unreferenced hatch pattern. Requires confirm=true.", ObjectSchema(
            new Dictionary<string, object>
            {
                ["document_id"] = StringSchema("Stable open-document ID"),
                ["pattern"] = StringSchema("Existing hatch pattern name or ID"),
                ["confirm"] = new { type = "boolean", @const = true }
            }, ["pattern", "confirm"])),
        Tool("list_system_fonts", "List installed font family names available for CadText styles.",
            new { type = "object", properties = new { }, additionalProperties = false })
    ];

    internal static object Execute(CadDocumentToolExecutor executor, string name, JsonElement arguments) => name switch
    {
        "list_styles" => ListStyles(executor),
        "create_graphic_style" => CreateGraphicStyle(executor, arguments),
        "create_line_type" => CreateLineType(executor, arguments),
        "rename_line_type" => RenameLineType(executor, arguments),
        "delete_line_type" => DeleteLineType(executor, arguments),
        "create_text_style" => CreateTextStyle(executor, arguments),
        "create_fill_style" => CreateFillStyle(executor, arguments),
        "create_hatch_pattern" => CreateHatchPattern(executor, arguments),
        "rename_style" => RenameStyle(executor, arguments),
        "delete_style" => DeleteStyle(executor, arguments),
        "delete_hatch_pattern" => DeleteHatchPattern(executor, arguments),
        _ => throw new ArgumentException($"Unknown style tool: {name}")
    };

    internal static object ListSystemFonts()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
            return new { fonts = Array.Empty<string>() };

        AddRegistryFonts(Registry.CurrentUser, names);
        AddRegistryFonts(Registry.LocalMachine, names);
        return new { fonts = names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray() };
    }

    private static object ListStyles(CadDocumentToolExecutor executor)
    {
        var document = executor.DocumentViewModel.CadEditor.Document;
        return new
        {
            styles = document.Styles.Values.OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase).Select(style => new
            {
                id = style.Id.Value,
                style.Name,
                kind = style.Kind.ToString(),
                reference_count = document.GetStyleReferenceCount(style.Id),
                properties = StyleProperties(style)
            }).ToArray(),
            hatch_patterns = document.HatchPatterns.Values.OrderBy(pattern => pattern.Name, StringComparer.OrdinalIgnoreCase).Select(pattern => new
            {
                id = pattern.Id.Value,
                pattern.Name,
                pattern.Description,
                line_count = pattern.Lines.Count,
                reference_count = document.GetHatchPatternReferenceCount(pattern.Id)
            }).ToArray(),
            line_types = document.LineTypes.Values.OrderBy(lineType => lineType.Name, StringComparer.OrdinalIgnoreCase).Select(lineType => new
            {
                id = lineType.Id.Value,
                lineType.Name,
                lineType.Description,
                dash_pattern = lineType.DashPattern.ToArray(),
                reference_count = document.GetLineTypeReferenceCount(lineType.Id)
            }).ToArray()
        };
    }

    private static object CreateLineType(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var dashPattern = OptionalArray(arguments, "dash_pattern");
        var pattern = dashPattern is { } dash
            ? dash.EnumerateArray().Select(RequiredFinite).ToArray()
            : [];
        var command = new CreateLineTypeCommand(
            RequiredString(arguments, "name"),
            pattern,
            OptionalString(arguments, "description") ?? string.Empty);
        executor.ExecuteCommand(command);
        return new { line_type_id = command.CreatedLineTypeId!.Value.Value };
    }

    private static object RenameLineType(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var document = executor.DocumentViewModel.CadEditor.Document;
        var lineType = ResolveLineType(document, RequiredString(arguments, "line_type"));
        var oldName = lineType.Name;
        executor.ExecuteCommand(new RenameLineTypeCommand(lineType.Id, RequiredString(arguments, "new_name")));
        return new { line_type_id = lineType.Id.Value, old_name = oldName, new_name = document.GetLineType(lineType.Id).Name };
    }

    private static object DeleteLineType(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        if (!OptionalBool(arguments, "confirm", false))
            throw new ArgumentException("delete_line_type requires confirm=true.");
        var document = executor.DocumentViewModel.CadEditor.Document;
        var lineType = ResolveLineType(document, RequiredString(arguments, "line_type"));
        if (document.GetLineTypeReferenceCount(lineType.Id) > 0)
            throw new InvalidOperationException($"Line type '{lineType.Name}' is still referenced.");
        executor.ExecuteCommand(new DeleteLineTypeCommand(lineType.Id));
        return new { line_type_id = lineType.Id.Value, line_type_name = lineType.Name };
    }

    private static object CreateGraphicStyle(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var document = executor.DocumentViewModel.CadEditor.Document;
        var lineTypeId = OptionalLong(arguments, "line_type_id", LineTypeId.Continuous.Value);
        var command = new CreateGraphicStyleCommand(
            RequiredString(arguments, "name"),
            CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "color")),
            ParseLineWeight(arguments, 0.25),
            new LineTypeId(lineTypeId));
        EnsureLineTypeExists(document, command, lineTypeId);
        executor.ExecuteCommand(command);
        return new { style_id = command.CreatedStyleId!.Value.Value, style = document.Styles[command.CreatedStyleId.Value].Name };
    }

    private static object CreateTextStyle(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var command = new CreateTextStyleCommand(
            RequiredString(arguments, "name"),
            RequiredString(arguments, "font_family"),
            OptionalPositive(arguments, "text_height", 1),
            OptionalPositive(arguments, "width_factor", 1),
            OptionalFinite(arguments, "oblique_angle_degrees", 0) * Math.PI / 180.0,
            OptionalBool(arguments, "bold", false),
            OptionalBool(arguments, "italic", false));
        executor.ExecuteCommand(command);
        return new { style_id = command.CreatedStyleId!.Value.Value };
    }

    private static object CreateFillStyle(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var mode = RequiredString(arguments, "mode").ToLowerInvariant();
        var name = RequiredString(arguments, "name");
        var command = mode switch
        {
            "solid" => CreateFillStyleCommand.Solid(name, CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "color"))),
            "hatch" => CreateHatchCommand(executor, name, arguments),
            "gradient" => CreateGradientCommand(name, arguments),
            _ => throw new ArgumentException("mode must be solid, hatch, or gradient.")
        };
        executor.ExecuteCommand(command);
        return new { style_id = command.CreatedStyleId!.Value.Value, mode };
    }

    private static CreateFillStyleCommand CreateHatchCommand(
        CadDocumentToolExecutor executor,
        string name,
        JsonElement arguments)
    {
        var document = executor.DocumentViewModel.CadEditor.Document;
        var pattern = ResolvePattern(document, RequiredString(arguments, "pattern"));
        return CreateFillStyleCommand.Hatch(
            name,
            pattern.Id,
            CadWorkspaceToolExecutor.ParseColor(RequiredString(arguments, "color")),
            OptionalPositive(arguments, "scale", 1),
            OptionalFinite(arguments, "angle_degrees", 0) * Math.PI / 180.0,
            new CadPointD(OptionalFinite(arguments, "origin_x", 0), OptionalFinite(arguments, "origin_y", 0)),
            OptionalBool(arguments, "annotative", false));
    }

    private static CreateFillStyleCommand CreateGradientCommand(string name, JsonElement arguments)
    {
        var stops = RequiredArray(arguments, "stops").EnumerateArray().Select(item => new CadGradientStop(
            RequiredFinite(item, "offset"),
            CadWorkspaceToolExecutor.ParseColor(RequiredString(item, "color")))).ToArray();
        return CreateFillStyleCommand.Gradient(
            name,
            string.Equals(OptionalString(arguments, "gradient_kind") ?? "linear", "radial", StringComparison.OrdinalIgnoreCase)
                ? CadGradientKind.Radial
                : CadGradientKind.Linear,
            stops,
            OptionalFinite(arguments, "angle_degrees", 0) * Math.PI / 180.0,
            OptionalPositive(arguments, "scale", 1),
            new CadPointD(OptionalFinite(arguments, "origin_x", 0), OptionalFinite(arguments, "origin_y", 0)),
            OptionalBool(arguments, "centered", true));
    }

    private static object CreateHatchPattern(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var lines = RequiredArray(arguments, "lines").EnumerateArray().Select(item =>
        {
            var dashPattern = OptionalArray(item, "dash_pattern");
            return new CadHatchLineDefinition(
                RequiredFinite(item, "angle_degrees"),
                Point(item, "origin"),
                Vector(item, "offset"),
                dashPattern is { } dash
                    ? dash.EnumerateArray().Select(RequiredFinite).ToArray()
                    : null);
        }).ToArray();
        var command = new CreateHatchPatternCommand(
            RequiredString(arguments, "name"),
            lines,
            OptionalString(arguments, "description") ?? string.Empty);
        executor.ExecuteCommand(command);
        return new { pattern_id = command.CreatedPatternId!.Value.Value };
    }

    private static object RenameStyle(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var document = executor.DocumentViewModel.CadEditor.Document;
        var style = ResolveStyle(document, RequiredString(arguments, "style"));
        var oldName = style.Name;
        executor.ExecuteCommand(new RenameStyleCommand(style.Id, RequiredString(arguments, "new_name")));
        return new { style_id = style.Id.Value, old_name = oldName, new_name = document.Styles[style.Id].Name };
    }

    private static object DeleteStyle(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        if (!OptionalBool(arguments, "confirm", false))
            throw new ArgumentException("delete_style requires confirm=true.");
        var document = executor.DocumentViewModel.CadEditor.Document;
        var style = ResolveStyle(document, RequiredString(arguments, "style"));
        var references = document.GetStyleReferenceCount(style.Id);
        if (references > 0)
            throw new InvalidOperationException($"Style '{style.Name}' has {references} reference(s).");
        executor.ExecuteCommand(new DeleteStyleCommand(style.Id));
        return new { style_id = style.Id.Value, style_name = style.Name };
    }

    private static object DeleteHatchPattern(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        if (!OptionalBool(arguments, "confirm", false))
            throw new ArgumentException("delete_hatch_pattern requires confirm=true.");
        var document = executor.DocumentViewModel.CadEditor.Document;
        var pattern = ResolvePattern(document, RequiredString(arguments, "pattern"));
        var references = document.GetHatchPatternReferenceCount(pattern.Id);
        if (references > 0)
            throw new InvalidOperationException($"Hatch pattern '{pattern.Name}' has {references} reference(s).");
        executor.ExecuteCommand(new DeleteHatchPatternCommand(pattern.Id));
        return new { pattern_id = pattern.Id.Value, pattern_name = pattern.Name };
    }

    private static object StyleProperties(CadStyle style) => style switch
    {
        CadGraphicStyle graphic => new { color = ColorText(graphic.StrokeColor), line_weight = graphic.LineWeight.Value, line_type_id = graphic.LineTypeId.Value },
        CadTextStyle text => new { text.FontFamily, text.TextHeight, text.WidthFactor, oblique_angle_degrees = text.ObliqueAngle * 180 / Math.PI, text.IsBold, text.IsItalic },
        CadHatchFillStyle hatch => new { fill_kind = "hatch", pattern_id = hatch.PatternId.Value, foreground_color = ColorText(hatch.ForegroundColor), hatch.HatchScale, angle_degrees = hatch.HatchAngle * 180 / Math.PI },
        CadGradientFillStyle gradient => new { fill_kind = "gradient", gradient_kind = gradient.GradientKind.ToString(), gradient.Stops },
        _ => new { }
    };

    private static CadStyle ResolveStyle(CadDocument document, string value) =>
        document.Styles.Values.FirstOrDefault(style =>
            style.Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
            style.Id.Value.ToString(CultureInfo.InvariantCulture) == value)
        ?? throw new KeyNotFoundException($"Style not found: {value}");

    private static CadHatchPatternDefinition ResolvePattern(CadDocument document, string value) =>
        document.HatchPatterns.Values.FirstOrDefault(pattern =>
            pattern.Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
            pattern.Id.Value.ToString(CultureInfo.InvariantCulture) == value)
        ?? throw new KeyNotFoundException($"Hatch pattern not found: {value}");

    private static void EnsureLineTypeExists(CadDocument document, CreateGraphicStyleCommand command, long lineTypeId)
    {
        if (!document.LineTypes.ContainsKey(new LineTypeId(lineTypeId)))
            throw new KeyNotFoundException($"Line type not found: {lineTypeId}");
    }

    private static CadLineTypeDefinition ResolveLineType(CadDocument document, string value) =>
        document.LineTypes.Values.FirstOrDefault(lineType =>
            lineType.Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
            lineType.Id.Value.ToString(CultureInfo.InvariantCulture) == value)
        ?? throw new KeyNotFoundException($"Line type not found: {value}");

    [SupportedOSPlatform("windows")]
    private static void AddRegistryFonts(RegistryKey? root, ISet<string> names)
    {
        using (var key = root?.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts"))
        {
            if (key is null)
                return;
            foreach (var name in key.GetValueNames())
            {
                var family = name.Split('(', 2)[0].Trim();
                if (family.EndsWith("TrueType", StringComparison.OrdinalIgnoreCase))
                    family = family[..^8].Trim();
                if (!string.IsNullOrWhiteSpace(family))
                    names.Add(family);
            }
        }
    }

    private static object FillCreationSchema() => new
    {
        type = "object",
        properties = new Dictionary<string, object>
        {
            ["document_id"] = StringSchema("Stable open-document ID"),
            ["name"] = StringSchema("Unique fill style name"),
            ["mode"] = new { type = "string", @enum = new[] { "solid", "hatch", "gradient" } },
            ["color"] = StringSchema("Solid or hatch foreground color"),
            ["pattern"] = StringSchema("Existing hatch pattern name or ID"),
            ["scale"] = new { type = "number", exclusiveMinimum = 0.0 },
            ["angle_degrees"] = new { type = "number" },
            ["origin_x"] = new { type = "number" }, ["origin_y"] = new { type = "number" },
            ["annotative"] = new { type = "boolean" },
            ["gradient_kind"] = new { type = "string", @enum = new[] { "linear", "radial" } },
            ["stops"] = new { type = "array", minItems = 2 },
            ["centered"] = new { type = "boolean" }
        },
        required = new[] { "name", "mode" },
        additionalProperties = false
    };

    private static JsonElement? OptionalArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value : null;

    private static JsonElement RequiredArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new ArgumentException($"{name} must be an array.");

    private static CadPointD Point(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        return new CadPointD(RequiredFinite(value, "x"), RequiredFinite(value, "y"));
    }

    private static CadVectorD Vector(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        return new CadVectorD(RequiredFinite(value, "x"), RequiredFinite(value, "y"));
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : throw new ArgumentException($"{name} is required.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static double RequiredFinite(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) && double.IsFinite(result)
            ? result
            : throw new ArgumentException($"{name} must be a finite number.");

    private static double RequiredFinite(JsonElement value) =>
        value.TryGetDouble(out var result) && double.IsFinite(result)
            ? result
            : throw new ArgumentException("Array values must be finite numbers.");

    private static double OptionalFinite(JsonElement element, string name, double fallback) =>
        !element.TryGetProperty(name, out var value) ? fallback : RequiredFinite(element, name);

    private static double OptionalPositive(JsonElement element, string name, double fallback)
    {
        var value = OptionalFinite(element, name, fallback);
        return value > 0 ? value : throw new ArgumentOutOfRangeException(name);
    }

    private static long OptionalLong(JsonElement element, string name, long fallback) =>
        !element.TryGetProperty(name, out var value) ? fallback : value.TryGetInt64(out var result) && result > 0 ? result : throw new ArgumentException($"{name} must be a positive integer.");

    private static bool OptionalBool(JsonElement element, string name, bool fallback) =>
        !element.TryGetProperty(name, out var value) ? fallback : value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new ArgumentException($"{name} must be boolean.");

    private static CadLineWeight ParseLineWeight(JsonElement element, double fallback) =>
        new(OptionalPositive(element, "line_weight", fallback));

    private static string ColorText(CadColor color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    private static object ObjectSchema(IReadOnlyDictionary<string, object> properties, IReadOnlyList<string>? required = null) => new { type = "object", properties, required = required ?? [], additionalProperties = false };
    private static object StringSchema(string description) => new { type = "string", description };
    private static AiToolDefinition Tool(string name, string description, object schema) => new(name, description, JsonSerializer.SerializeToElement(schema));
}
