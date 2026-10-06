using Direct2dCad.AI.Contracts;

namespace Direct2dCad.Application.Tools;

/// <summary>Derives discovery groups from the registered definitions, including future tools.</summary>
internal static class CadToolCatalog
{
    internal static IReadOnlyDictionary<string, string[]> Groups(IReadOnlyList<AiToolDefinition> definitions) =>
        definitions.GroupBy(tool => Group(tool.Name), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

    private static string Group(string name)
    {
        if (name is "undo" or "redo" or "undo_view" or "redo_view") return "history";
        if (name.Contains("dimension", StringComparison.Ordinal)) return "annotations";
        if (name.Contains("layout", StringComparison.Ordinal) || name is "activate_space" or "capture_view" or "print_document") return "presentation";
        if (name.Contains("dxf", StringComparison.Ordinal)) return "exchange";
        if (name is "list_documents" or "create_document" or "open_document" or "activate_document" or "rename_document" or "save_document" or "close_document") return "workspace";
        if (name.StartsWith("select_", StringComparison.Ordinal) || name == "clear_selection") return "selection";
        if (name is "get_view_settings" or "set_view_settings" or "set_viewport" or "manage_grid_presets" or "set_drawing_layer") return "view";
        if (name is "get_agent_capabilities" or "get_document_summary" or "get_entity_statistics" or "list_entities" or "list_document_catalog" or "measure_geometry") return "inspect";
        if (name.StartsWith("add_", StringComparison.Ordinal) || name == "insert_image_from_file") return "create";
        if (name.Contains("layer", StringComparison.Ordinal) || name.Contains("block", StringComparison.Ordinal)) return "organization";
        if (name.Contains("style", StringComparison.Ordinal) || name.Contains("hatch", StringComparison.Ordinal) || name.Contains("line_type", StringComparison.Ordinal) ||
            name is "list_system_fonts" or "set_entity_common_properties" or "set_entity_fill" or "set_entity_specific_properties" or "set_ole_object_data") return "appearance";
        if (name is "get_entity_geometry" or "set_entity_geometry" or "transform_entities" or "duplicate_entities" or "move_entities" or "delete_entities" or "edit_curves" or "array_entities" or "boolean_regions") return "geometry";
        return "other";
    }
}
