using System.Text.Json;
using System.Text.Json.Nodes;
using Direct2dCad.AI.Contracts;

namespace Direct2dCad.Application.Tools;

/// <summary>Validates the published tool schema before dispatch can mutate a document.</summary>
internal static class CadToolSchemaValidator
{
    public static void Validate(string toolName, JsonElement arguments, IReadOnlyList<AiToolDefinition> definitions)
    {
        var definition = definitions.FirstOrDefault(tool => tool.Name == toolName)
            ?? throw new ArgumentException($"Unknown CAD tool: {toolName}");
        Validate(arguments, definition.Parameters);
    }

    internal static JsonElement NormalizeAndValidate(string toolName, JsonElement arguments, IReadOnlyList<AiToolDefinition> definitions)
    {
        var definition = definitions.FirstOrDefault(tool => tool.Name == toolName)
            ?? throw new ArgumentException($"Unknown CAD tool: {toolName}");
        EnsureUnambiguousProperties(arguments, "$");
        var node = Normalize(JsonNode.Parse(arguments.GetRawText()), definition.Parameters);
        var normalized = Direct2dCad.AI.Contracts.CadJson.SerializeToElement(node);
        Validate(normalized, definition.Parameters);
        return normalized;
    }

    private static void EnsureUnambiguousProperties(JsonElement value, string path)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException($"{path}.{property.Name}: duplicate property.");
                EnsureUnambiguousProperties(property.Value, path + "." + property.Name);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            for (var index = 0; index < value.GetArrayLength(); index++) EnsureUnambiguousProperties(value[index], $"{path}[{index}]");
    }

    private static JsonNode? Normalize(JsonNode? value, JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object) return value;
        if (value is JsonValue scalar && scalar.TryGetValue<string>(out var text) && schema.TryGetProperty("enum", out var choices))
        {
            static string EnumKey(string text) => text.Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
            var matches = choices.EnumerateArray().Where(choice => choice.ValueKind == JsonValueKind.String &&
                string.Equals(EnumKey(choice.GetString()!), EnumKey(text), StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 1) value = JsonValue.Create(matches[0].GetString());
        }
        if (value is JsonObject obj && schema.TryGetProperty("properties", out var properties))
        {
            var required = schema.TryGetProperty("required", out var fields)
                ? fields.EnumerateArray().Select(field => field.GetString()).ToHashSet(StringComparer.Ordinal) : [];
            foreach (var property in properties.EnumerateObject())
            {
                if (!obj.TryGetPropertyValue(property.Name, out var child)) continue;
                // Legacy optional null means omitted, unless the published schema gives
                // null an explicit meaning (for example clearing dimension text_override).
                if (child is null && !required.Contains(property.Name) &&
                    FindError(Direct2dCad.AI.Contracts.CadJson.SerializeToElement<object?>(null), property.Value, "$", 0) is not null)
                { obj.Remove(property.Name); continue; }
                var normalized = Normalize(child, property.Value);
                if (!ReferenceEquals(child, normalized)) obj[property.Name] = normalized;
            }
        }
        if (value is JsonArray array && schema.TryGetProperty("items", out var itemSchema))
            for (var index = 0; index < array.Count; index++)
            {
                var normalized = Normalize(array[index], itemSchema);
                if (!ReferenceEquals(array[index], normalized)) array[index] = normalized;
            }
        foreach (var keyword in new[] { "allOf", "anyOf", "oneOf" })
            if (schema.TryGetProperty(keyword, out var branches))
                foreach (var branch in branches.EnumerateArray()) value = Normalize(value, branch);
        return value;
    }

    internal static void Validate(JsonElement value, JsonElement schema)
    {
        if (FindError(value, schema, "$", 0) is { } error)
            throw new ArgumentException(error);
    }

    private static string? FindError(JsonElement value, JsonElement schema, string path, int depth)
    {
        if (depth > 64) return $"{path}: nesting exceeds the validation limit.";
        if (schema.ValueKind == JsonValueKind.True) return null;
        if (schema.ValueKind == JsonValueKind.False) return $"{path}: this value is not allowed.";
        if (schema.ValueKind != JsonValueKind.Object) return $"{path}: invalid tool schema.";

        if (schema.TryGetProperty("type", out var type))
        {
            var matches = type.ValueKind == JsonValueKind.Array
                ? type.EnumerateArray().Any(item => MatchesType(value, item.GetString()))
                : MatchesType(value, type.GetString());
            if (!matches) return $"{path}: expected {type.GetRawText()}.";
        }
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(item => JsonElement.DeepEquals(value, item)))
            return $"{path}: expected one of {choices.GetRawText()}.";
        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(value, constant))
            return $"{path}: expected {constant.GetRawText()}.";

        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var hasProperties = schema.TryGetProperty("properties", out var properties);
            var hasAdditional = schema.TryGetProperty("additionalProperties", out var additional);
            foreach (var property in value.EnumerateObject())
            {
                var propertyPath = path + "." + property.Name;
                if (!names.Add(property.Name)) return $"{propertyPath}: duplicate property.";
                if (hasProperties && properties.TryGetProperty(property.Name, out var propertySchema))
                {
                    if (FindError(property.Value, propertySchema, propertyPath, depth + 1) is { } error) return error;
                }
                else if (hasAdditional)
                {
                    if (additional.ValueKind == JsonValueKind.False) return $"{propertyPath}: unknown property.";
                    if (additional.ValueKind == JsonValueKind.Object &&
                        FindError(property.Value, additional, propertyPath, depth + 1) is { } error) return error;
                }
            }
            if (schema.TryGetProperty("required", out var required))
                foreach (var item in required.EnumerateArray())
                    if (!names.Contains(item.GetString()!)) return $"{path}.{item.GetString()}: required property is missing.";
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var length = value.GetArrayLength();
            if (schema.TryGetProperty("minItems", out var minimum) && length < minimum.GetInt32())
                return $"{path}: requires at least {minimum} items.";
            if (schema.TryGetProperty("maxItems", out var maximum) && length > maximum.GetInt32())
                return $"{path}: allows at most {maximum} items.";
            if (schema.TryGetProperty("items", out var itemSchema))
                for (var index = 0; index < length; index++)
                    if (FindError(value[index], itemSchema, $"{path}[{index}]", depth + 1) is { } error) return error;
            if (schema.TryGetProperty("uniqueItems", out var unique) && unique.ValueKind == JsonValueKind.True)
            {
                var seen = new Dictionary<int, List<JsonElement>>();
                foreach (var item in value.EnumerateArray())
                {
                    var hash = ValueHash(item);
                    if (!seen.TryGetValue(hash, out var bucket)) seen[hash] = bucket = [];
                    if (bucket.Any(previous => JsonElement.DeepEquals(previous, item))) return $"{path}: duplicate items are not allowed.";
                    bucket.Add(item);
                }
            }
        }
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (!value.TryGetDouble(out var number) || !double.IsFinite(number)) return $"{path}: number must be finite.";
            if (schema.TryGetProperty("minimum", out var minimum) && number < minimum.GetDouble()) return $"{path}: must be at least {minimum}.";
            if (schema.TryGetProperty("maximum", out var maximum) && number > maximum.GetDouble()) return $"{path}: must be at most {maximum}.";
            if (schema.TryGetProperty("exclusiveMinimum", out var exclusiveMinimum) && number <= exclusiveMinimum.GetDouble()) return $"{path}: must be greater than {exclusiveMinimum}.";
            if (schema.TryGetProperty("exclusiveMaximum", out var exclusiveMaximum) && number >= exclusiveMaximum.GetDouble()) return $"{path}: must be less than {exclusiveMaximum}.";
        }
        if (value.ValueKind == JsonValueKind.String)
        {
            var length = value.GetString()!.EnumerateRunes().Count();
            if (schema.TryGetProperty("minLength", out var minimum) && length < minimum.GetInt32()) return $"{path}: requires at least {minimum} characters.";
            if (schema.TryGetProperty("maxLength", out var maximum) && length > maximum.GetInt32()) return $"{path}: allows at most {maximum} characters.";
        }
        if (schema.TryGetProperty("allOf", out var all))
            foreach (var branch in all.EnumerateArray())
                if (FindError(value, branch, path, depth + 1) is { } error) return error;
        if (schema.TryGetProperty("anyOf", out var any) && !any.EnumerateArray().Any(branch => FindError(value, branch, path, depth + 1) is null))
            return $"{path}: does not match any allowed argument combination.";
        if (schema.TryGetProperty("oneOf", out var one) && one.EnumerateArray().Count(branch => FindError(value, branch, path, depth + 1) is null) != 1)
            return $"{path}: must match exactly one allowed argument combination.";
        if (schema.TryGetProperty("not", out var not) && FindError(value, not, path, depth + 1) is null)
            return $"{path}: incompatible argument combination.";
        if (schema.TryGetProperty("if", out var condition))
        {
            var branchName = FindError(value, condition, path, depth + 1) is null ? "then" : "else";
            if (schema.TryGetProperty(branchName, out var branch) && FindError(value, branch, path, depth + 1) is { } error) return error;
        }
        return null;
    }

    private static bool MatchesType(JsonElement value, string? type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && decimal.Truncate(number) == number,
        _ => false
    };

    private static int ValueHash(JsonElement value)
    {
        var hash = new HashCode();
        hash.Add(value.ValueKind);
        switch (value.ValueKind)
        {
            case JsonValueKind.Number: hash.Add(value.GetDouble()); break;
            case JsonValueKind.String: hash.Add(value.GetString(), StringComparer.Ordinal); break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray()) hash.Add(ValueHash(item));
                break;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                { hash.Add(property.Name, StringComparer.Ordinal); hash.Add(ValueHash(property.Value)); }
                break;
        }
        return hash.ToHashCode();
    }
}
