using System.Text.Json;
using Direct2dCad.AI.Contracts;

namespace Direct2dCad.Agent;

/// <summary>Provides an on-demand path to tools omitted by intent or context budgeting.</summary>
internal static class AgentToolDiscovery
{
    internal const string Name = "discover_tools";
    internal static readonly AiToolDefinition Definition = new(Name,
        "Find and load tools for a task before calling them. Supply names for exact tools, or a query describing the operation (any supported language). An empty query lists the complete tool-name catalog. Loaded tools are prioritized on the next request; narrow to one name if context is limited.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                query = new { type = "string" },
                names = new { type = "array", minItems = 1, maxItems = 8, items = new { type = "string" } }
            },
            additionalProperties = false
        }));

    internal static (string Result, IReadOnlyList<AiToolDefinition> Tools) Execute(IAgentToolset toolset, string arguments)
    {
        try
        {
            using var document = JsonDocument.Parse(arguments);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(property => property.Name is not ("query" or "names")))
                throw new ArgumentException("Expected an object containing query or names.");
            if (root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != root.EnumerateObject().Count())
                throw new ArgumentException("Duplicate discovery properties are not allowed.");
            if (root.TryGetProperty("query", out var query) && query.ValueKind != JsonValueKind.String)
                throw new ArgumentException("query must be a string.");
            var tools = toolset.ToolDefinitions;
            if (root.TryGetProperty("names", out var names))
            {
                if (names.ValueKind != JsonValueKind.Array || names.GetArrayLength() is < 1 or > 8 ||
                    names.EnumerateArray().Any(name => name.ValueKind != JsonValueKind.String))
                    throw new ArgumentException("names must contain one to eight tool names.");
                var loaded = names.EnumerateArray().Select(name => tools.FirstOrDefault(tool => tool.Name.Equals(name.GetString(), StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException($"Unknown tool: {name.GetString()}"))
                    .DistinctBy(tool => tool.Name).ToArray();
                return Loaded(loaded);
            }
            if (!root.TryGetProperty("query", out query) || string.IsNullOrWhiteSpace(query.GetString()))
                return (JsonSerializer.Serialize(new { success = true, tool_names = tools.Select(tool => tool.Name).Order(StringComparer.Ordinal), next_step = "Call discover_tools with exact names to load their schemas." }), []);
            var text = query.GetString()!;
            var words = text.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var direct = tools.Select(tool => (Tool: tool, Score: words.Sum(word => tool.Name.Contains(word, StringComparison.OrdinalIgnoreCase) ? 10 :
                tool.Description.Contains(word, StringComparison.OrdinalIgnoreCase) ? 1 : 0)))
                .Where(item => item.Score > 0).OrderByDescending(item => item.Score).Select(item => item.Tool);
            var inferred = toolset.SelectTools(text);
            return Loaded(direct.Concat(inferred).DistinctBy(tool => tool.Name).Take(8).ToArray());
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return (JsonSerializer.Serialize(new { success = false, error = exception.Message }), []);
        }
    }

    private static (string, IReadOnlyList<AiToolDefinition>) Loaded(IReadOnlyList<AiToolDefinition> tools) =>
        (JsonSerializer.Serialize(new { success = true, tools = tools.Select(tool => new { name = tool.Name, description = tool.Description }),
            next_step = "Use these tools on the next request. For a tool still unavailable in a small context, load its exact name alone." }), tools);
}
