using System.Reflection;
using Direct2dCad.Application.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Direct2dCad.Mcp;

/// <summary>
/// The MCP view of the CAD tool catalog, shared by the HTTP listener and the stdio bridge so both
/// transports advertise the same names, schemas, annotations and server identity.
/// </summary>
public static class CadMcpToolCatalog
{
    public const string ServerName = "Direct2dCad";

    public const string ServerInstructions = "Control the open Direct2dCad workspace. Start with list_documents and " +
        "get_agent_capabilities. Coordinates are millimetres and angles are counter-clockwise degrees. " +
        "Supply document_id when working across documents. Each MCP tool call has its own undo batch. " +
        "Use add_entities for a multi-part drawing in one undo step. Inspect tool results after edits.";

    // Annotation sets are keyed by tool name; CadMcpAnnotationTests keeps them in step with the tool catalog.
    internal static readonly HashSet<string> ReadOnlyTools = new(StringComparer.Ordinal)
    {
        "get_agent_capabilities", "list_documents", "get_document_summary", "get_view_settings",
        "get_entity_statistics", "list_entities", "list_layers", "get_entity_geometry",
        "list_styles", "list_system_fonts", "list_blocks", "list_document_catalog",
        "measure_geometry", "capture_view", "list_layouts", "list_document_templates", "list_recovery_copies", "get_file_revision",
        "get_application_settings"
    };

    internal static readonly HashSet<string> AdditiveTools = new(StringComparer.Ordinal)
    {
        "create_document", "open_document", "open_dxf", "open_recovery_copy", "create_layer", "create_layout",
        "create_graphic_style", "create_text_style", "create_fill_style", "create_hatch_pattern", "create_line_type",
        "insert_block", "insert_image_from_file", "insert_image_from_clipboard", "duplicate_entities", "array_entities"
    };

    /// <summary>Tools that read or write outside the open drawing (files, clipboard, system fonts, app settings).</summary>
    internal static readonly HashSet<string> OpenWorldTools = new(StringComparer.Ordinal)
    {
        "open_document", "open_dxf", "save_document", "close_document", "export_dxf", "insert_image_from_file",
        "print_document", "list_system_fonts", "get_file_revision", "save_compatible_copy", "insert_image_from_clipboard",
        "list_recovery_copies", "open_recovery_copy", "clear_recovery_copies", "get_application_settings", "set_application_settings"
    };

    public static string ServerVersion { get; } =
        typeof(CadMcpToolCatalog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>Creates fresh protocol tool descriptions; callers own the returned list.</summary>
    public static List<Tool> CreateTools() =>
        CadWorkspaceToolExecutor.ToolDefinitions.Select(definition => new Tool
        {
            Name = definition.Name,
            Description = definition.Description,
            InputSchema = definition.Parameters.Clone(),
            Annotations = new ToolAnnotations
            {
                ReadOnlyHint = ReadOnlyTools.Contains(definition.Name),
                DestructiveHint = !ReadOnlyTools.Contains(definition.Name) && !AdditiveTools.Contains(definition.Name) &&
                    !definition.Name.StartsWith("add_", StringComparison.Ordinal),
                OpenWorldHint = OpenWorldTools.Contains(definition.Name)
            }
        }).ToList();

    /// <summary>The catalog fits in one page, so a continuation cursor can only be a client error.</summary>
    internal static ListToolsResult List(IList<Tool> tools, ListToolsRequestParams? request)
    {
        if (request?.Cursor is not null)
            throw new McpProtocolException("The tool catalog has no continuation cursor.", McpErrorCode.InvalidParams);
        return new ListToolsResult { Tools = tools };
    }

    internal static void ThrowIfUnknown(string name)
    {
        if (!CadWorkspaceToolExecutor.ToolDefinitions.Any(tool => tool.Name == name))
            throw new McpProtocolException($"Unknown CAD tool: {name}", McpErrorCode.InvalidParams);
    }
}
