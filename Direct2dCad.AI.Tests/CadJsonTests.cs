using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Direct2dCad.AI.Contracts;
namespace Direct2dCad.AI.Tests;

public sealed class CadJsonTests
{
    [Fact]
    public void HeterogeneousSchemasAndResultsMatchTheExistingProtocol()
    {
        var value = new { success = true, result = new { id = 42L, geometry = new Dictionary<string, object> { ["center"] = new[] { 1.25, 2.5 }, ["radius"] = 4.0 }, items = new[] { new { name = "图层", enabled = false }, new { name = "寸法", enabled = true } } } };
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(value), CadJson.SerializeToElement(value)));
    }
    [Fact]
    public void ProviderPayloadUsesJsonPropertyNamesAndOmitsNullProperties()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var value = new Payload("模型", null, JsonNode.Parse("{\"type\":\"object\",\"properties\":{}}")!);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(value, options), CadJson.SerializeToElement(value, options)));
    }
    [Fact]
    public void CyclesAreRejectedWithoutRecursingIndefinitely()
    {
        var dictionary = new Dictionary<string, object>(); dictionary["self"] = dictionary;
        Assert.Throws<JsonException>(() => CadJson.Serialize(dictionary));
    }
    private sealed record Payload([property: JsonPropertyName("model")] string Name, string? Optional, JsonNode Parameters);
}
