using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Direct2dCad.AI.Contracts;

/// <summary>
/// Writes heterogeneous tool schemas and results without constructing generic
/// System.Text.Json converters at runtime. Native clients preserve the tool DTO
/// property metadata in their linker descriptor. Settings and storage use generated contexts.
/// </summary>
public static class CadJson
{
    public static string Serialize<T>(T value, JsonSerializerOptions? options = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = options?.WriteIndented ?? false }))
            Write(writer, value, options, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public static JsonElement SerializeToElement<T>(T value, JsonSerializerOptions? options = null)
    {
        using var document = JsonDocument.Parse(Serialize(value, options)); return document.RootElement.Clone();
    }
    public static JsonNode? SerializeToNode<T>(T value, JsonSerializerOptions? options = null) => JsonNode.Parse(Serialize(value, options));

    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Native client roots the tool schema/result DTO assemblies through NativeRoots.xml; getters use reflection invocation without runtime code generation.")]
    private static void Write(Utf8JsonWriter writer, object? value, JsonSerializerOptions? options, HashSet<object> visited, int depth)
    {
        if (depth > (options?.MaxDepth is > 0 ? options.MaxDepth : 64)) throw new JsonException("JSON depth budget exceeded.");
        switch (value)
        {
            case null: writer.WriteNullValue(); return;
            case JsonElement element: element.WriteTo(writer); return;
            case JsonNode node: node.WriteTo(writer, options); return;
            case string text: writer.WriteStringValue(text); return;
            case char character: writer.WriteStringValue(character.ToString()); return;
            case bool boolean: writer.WriteBooleanValue(boolean); return;
            case byte number: writer.WriteNumberValue(number); return;
            case sbyte number: writer.WriteNumberValue(number); return;
            case short number: writer.WriteNumberValue(number); return;
            case ushort number: writer.WriteNumberValue(number); return;
            case int number: writer.WriteNumberValue(number); return;
            case uint number: writer.WriteNumberValue(number); return;
            case long number: writer.WriteNumberValue(number); return;
            case ulong number: writer.WriteNumberValue(number); return;
            case float number: writer.WriteNumberValue(number); return;
            case double number: writer.WriteNumberValue(number); return;
            case decimal number: writer.WriteNumberValue(number); return;
            case Guid guid: writer.WriteStringValue(guid); return;
            case DateTime date: writer.WriteStringValue(date); return;
            case DateTimeOffset date: writer.WriteStringValue(date); return;
            case byte[] bytes: writer.WriteBase64StringValue(bytes); return;
            case Enum enumeration: writer.WriteNumberValue(Convert.ToInt64(enumeration, CultureInfo.InvariantCulture)); return;
        }
        if (!value.GetType().IsValueType && !visited.Add(value)) throw new JsonException("A JSON object cycle was detected.");
        try
        {
            if (value is IDictionary dictionary)
            {
                writer.WriteStartObject();
                foreach (DictionaryEntry entry in dictionary)
                {
                    writer.WritePropertyName(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? throw new JsonException("Null dictionary key."));
                    Write(writer, entry.Value, options, visited, depth + 1);
                }
                writer.WriteEndObject(); return;
            }
            if (value is IEnumerable sequence)
            {
                writer.WriteStartArray(); foreach (var item in sequence) Write(writer, item, options, visited, depth + 1); writer.WriteEndArray(); return;
            }
            writer.WriteStartObject();
            foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length != 0 || property.GetMethod?.IsPublic != true) continue;
                var ignored = property.GetCustomAttribute<JsonIgnoreAttribute>(); if (ignored?.Condition == JsonIgnoreCondition.Always) continue;
                var item = property.GetValue(value);
                if (item is null && (ignored?.Condition ?? options?.DefaultIgnoreCondition) == JsonIgnoreCondition.WhenWritingNull) continue;
                var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? options?.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
                writer.WritePropertyName(name); Write(writer, item, options, visited, depth + 1);
            }
            writer.WriteEndObject();
        }
        finally { if (!value.GetType().IsValueType) visited.Remove(value); }
    }
}
