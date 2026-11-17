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
        var settings = JsonSerializer.SerializeToNode(_userSettings, CadToolSettingsJsonContext.Default.CadUserSettings)!;
        // The MCP access token is a secret that guards the tools themselves: agents never read it.
        (settings["mcp"] as JsonObject)?.Remove("access_token");
        var result = new JsonObject
        {
            ["settings"] = settings,
            ["radial_actions"] = new JsonArray(Enum.GetNames<CadRadialMenuAction>().Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
            ["mcp_service"] = new JsonObject
            {
                ["available"] = _mcpService is not null,
                ["state"] = mcpStatus?.State.ToString(),
                ["endpoint"] = mcpStatus?.Endpoint?.AbsoluteUri,
                ["error"] = mcpStatus?.Error,
                ["requires_access_token"] = !string.IsNullOrEmpty(_userSettings.Mcp.AccessToken)
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
        // Neither a patch nor a reset may remove or replace the token from inside the tools it protects.
        settings.Mcp.AccessToken = _userSettings.Mcp.AccessToken;
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
