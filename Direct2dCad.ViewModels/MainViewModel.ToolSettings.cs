using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Direct2dCad.Client.Common.Settings;

namespace Direct2dCad.ViewModels;

public partial class MainViewModel
{
    internal JsonElement GetApplicationSettingsForTools()
    {
        var mcpStatus = _mcpService?.Status;
        var result = new JsonObject
        {
            ["settings"] = JsonSerializer.SerializeToNode(_userSettings, CadToolSettingsJsonContext.Default.CadUserSettings),
            ["radial_actions"] = new JsonArray(Enum.GetNames<CadRadialMenuAction>().Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
            ["mcp_service"] = new JsonObject
            {
                ["available"] = _mcpService is not null,
                ["state"] = mcpStatus?.State.ToString(),
                ["endpoint"] = mcpStatus?.Endpoint?.AbsoluteUri,
                ["error"] = mcpStatus?.Error
            }
        };
        return JsonSerializer.SerializeToElement(result, CadToolSettingsJsonContext.Default.JsonObject);
    }

    internal JsonElement SetApplicationSettingsForTools(JsonElement patch, bool reset)
    {
        var settings = reset ? CadUserSettings.CreateDefault() : _userSettings.Clone();
        var merged = JsonSerializer.SerializeToNode(settings, CadToolSettingsJsonContext.Default.CadUserSettings)!.AsObject();
        static void Merge(JsonObject destination, JsonObject source)
        {
            foreach (var property in source)
            {
                if (property.Value is JsonObject child && destination[property.Key] is JsonObject existing) Merge(existing, child);
                else destination[property.Key] = property.Value?.DeepClone();
            }
        }
        Merge(merged, JsonNode.Parse(patch.GetRawText())!.AsObject());
        settings = merged.Deserialize(CadToolSettingsJsonContext.Default.CadUserSettings)!;
        settings.Normalize();
        _userSettingsStore.Save(settings);
        ApplyUserSettings(settings);
        return GetApplicationSettingsForTools();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true, IgnoreReadOnlyProperties = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(CadUserSettings))]
[JsonSerializable(typeof(JsonObject))]
internal partial class CadToolSettingsJsonContext : JsonSerializerContext;
