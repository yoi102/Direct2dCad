using Direct2dCad.AI.Contracts;

namespace Direct2dCad.Application.Tools;

internal static class CadApplicationSettingsTools
{
    internal static IReadOnlyList<AiToolDefinition> ToolDefinitions { get; } =
    [
        new("get_application_settings", "Read all application preferences, the supported radial-menu action names and mcp_service runtime status (available, state, actual endpoint and error). Preferences apply across documents. settings.mcp contains configured preferences, and mcp_service reports the current HTTP listener.",
            CadJson.SerializeToElement(ObjectSchema([]))),
        new("set_application_settings", "Patch application preferences and persist/apply them through the host settings pipeline. Unspecified values are preserved. reset=true starts from defaults before applying settings. Preferences are outside drawing undo history. Colors use byte channels a/r/g/b. Radial gesture arrays require exactly eight supported action names. MCP preferences start, stop or rebind the loopback HTTP listener after this response completes. After a port change, reconnect at the configured port. Query get_application_settings.mcp_service for the actual listener status and startup error.",
            CadJson.SerializeToElement(ObjectSchema(new()
            {
                ["settings"] = ObjectSchema(new()
                {
                    ["general"] = ObjectSchema(new()
                    {
                        ["history_budget_megabytes"] = new { type = "integer", minimum = 16, maximum = 4096 },
                        ["is_auto_recovery_enabled"] = Boolean(), ["is_dark_theme"] = Boolean(),
                        ["culture_lcid"] = new { type = "integer", @enum = new[] { 1033, 1041, 2052 } },
                        ["primary_color"] = Color(), ["secondary_color"] = Color()
                    }),
                    ["rendering"] = ObjectSchema(new()
                    {
                        ["is_antialiasing_enabled"] = Boolean(), ["is_text_antialiasing_enabled"] = Boolean(),
                        ["graphics_device_mode"] = new { type = "string", @enum = new[] { "Automatic", "Hardware", "Warp" } },
                        ["show_frames_per_second"] = Boolean(), ["is_zoom_snapshot_preview_enabled"] = Boolean(),
                        ["is_level_of_detail_enabled"] = Boolean(), ["allow_approximate_tile_scale_fallback"] = Boolean(),
                        ["is_background_chunk_recording_enabled"] = Boolean(), ["is_parallel_rendering_enabled"] = Boolean(),
                        ["parallel_rendering_mode"] = new { type = "string", @enum = new[] { "MultipleDevices", "SharedDeviceContexts" } },
                        ["parallel_rendering_worker_count"] = new { type = "integer", minimum = 2, maximum = 4 }
                    }),
                    ["interaction"] = InteractionSchema(),
                    ["mcp"] = ObjectSchema(new()
                    {
                        ["is_enabled"] = Boolean(),
                        ["port"] = new { type = "integer", minimum = 1, maximum = 65535 }
                    })
                }),
                ["reset"] = Boolean()
            }, ["settings"])))
    ];

    private static object InteractionSchema()
    {
        var properties = new Dictionary<string, object>();
        foreach (var name in new[] { "selected_entity_stroke_color", "grip_stroke_color", "grip_fill_color",
            "grip_preview_stroke_color", "grip_preview_fill_color", "selection_window_stroke_color", "selection_window_fill_color",
            "selection_crossing_stroke_color", "selection_crossing_fill_color" }) properties[name] = Color();
        foreach (var name in new[] { "selected_entity_stroke_width", "grip_size", "grip_stroke_width", "grip_preview_stroke_width",
            "selection_window_stroke_width", "selection_crossing_stroke_width" }) properties[name] = new { type = "number", exclusiveMinimum = 0 };
        var radial = new Dictionary<string, object> { ["is_enabled"] = Boolean() };
        foreach (var name in new[] { "middle_actions", "shift_middle_actions", "control_middle_actions", "alt_middle_actions" })
            radial[name] = new { type = "array", minItems = 8, maxItems = 8,
                items = new { type = "string", description = "An action name returned by get_application_settings.radial_actions." } };
        properties["radial_menu"] = ObjectSchema(radial);
        return ObjectSchema(properties);
    }

    private static object Boolean() => new { type = "boolean" };
    private static object Color() => ObjectSchema(new Dictionary<string, object>
    {
        ["a"] = new { type = "integer", minimum = 0, maximum = 255 },
        ["r"] = new { type = "integer", minimum = 0, maximum = 255 },
        ["g"] = new { type = "integer", minimum = 0, maximum = 255 },
        ["b"] = new { type = "integer", minimum = 0, maximum = 255 }
    }, ["a", "r", "g", "b"]);
    private static object ObjectSchema(Dictionary<string, object> properties, string[]? required = null) =>
        new { type = "object", properties, required = required ?? [], additionalProperties = false };
}
