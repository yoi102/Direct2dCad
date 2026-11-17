using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Application.Platform;
using Direct2dCad.IO;

using MessagePipe;


namespace Direct2dCad.Application.Tools;

/// <summary>
/// Routes one tool execution session across the open-document workspace. Each touched document
/// receives its own command batch so its undo history remains independent.
/// </summary>

public sealed partial class CadWorkspaceToolExecutor
{
    private static CadGradientStop[] ParseGradientStops(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("stops", out var value) || value.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("stops is required for gradient fill and must be an array.");

        var stops = value.EnumerateArray().Select((item, index) =>
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new ArgumentException($"stops[{index}] must be an object.");
            if (!item.TryGetProperty("offset", out var offsetValue) ||
                !offsetValue.TryGetDouble(out var offset) ||
                !double.IsFinite(offset))
            {
                throw new ArgumentException($"stops[{index}].offset must be a finite number.");
            }
            var color = ParseColor(RequiredString(item, "color"));
            return new CadGradientStop(offset, color);
        }).OrderBy(stop => stop.Offset).ToArray();

        if (stops.Length < 2 || stops[0].Offset != 0.0 || stops[^1].Offset != 1.0)
            throw new ArgumentException("Gradient stops require at least two entries with offsets 0 and 1.");
        return stops;
    }

    private static CadLineWeight ParseLineWeight(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!.Trim();
            if (text.Replace("-", "_", StringComparison.Ordinal).Equals("by_layer", StringComparison.OrdinalIgnoreCase))
                return CadLineWeight.ByLayer;
            throw new ArgumentException("line_weight string must be 'by_layer'.");
        }

        if (!value.TryGetDouble(out var weight) || !double.IsFinite(weight) || weight <= 0)
            throw new ArgumentException("line_weight must be 'by_layer' or a finite number greater than zero.");
        return new CadLineWeight(weight);
    }

    internal static CadColor ParseColor(string value)
    {
        var text = value.Trim();
        if (text.StartsWith('#'))
            text = text[1..];
        if (text.Length == 6 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return CadColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        if (text.Length == 8 && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            return CadColor.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        return text.ToLowerInvariant() switch
        {
            "black" => CadColor.Black,
            "white" => CadColor.White,
            "red" => CadColor.Red,
            "green" => CadColor.Green,
            "blue" => CadColor.Blue,
            "transparent" => CadColor.Transparent,
            _ => throw new ArgumentException("color must be #RRGGBB, #AARRGGBB, or a supported named color.")
        };
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum
    {
        var normalized = value.Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        foreach (var candidate in Enum.GetValues<T>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }
        throw new ArgumentException($"Unsupported {typeof(T).Name} value: {value}");
    }

    private static T OptionalEnum<T>(JsonElement arguments, string name, T fallback) where T : struct, Enum =>
        HasValue(arguments, name) ? ParseEnum<T>(RequiredString(arguments, name)) : fallback;

    private static string NextEntityName(CadDocument document, string prefix)
    {
        var names = document.Entities.Values
            .Select(entity => entity.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; ; index++)
        {
            var candidate = $"{prefix}{index}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    private static string ColorText(CadColor color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    private static bool HasValue(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"{name} is required.");
        return value.GetString()!.Trim();
    }

    private static EntityId RequiredEntityId(JsonElement element)
    {
        if (!element.TryGetProperty("entity_id", out var value) ||
            !value.TryGetInt64(out var id) ||
            id <= 0)
        {
            throw new ArgumentException("entity_id must be a positive integer.");
        }

        return new EntityId(id);
    }

    private static byte[] DecodeBase64(string value, string name)
    {
        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length == 0
                ? throw new ArgumentException($"{name} cannot be empty.")
                : bytes;
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"{name} must be valid base64.", name, exception);
        }
    }

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static int OptionalInt(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : fallback;

    private static bool OptionalBool(JsonElement element, string name, bool fallback)
    {
        if (!element.TryGetProperty(name, out var value))
            return fallback;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException($"{name} must be a boolean.");
        return value.GetBoolean();
    }

    private static double OptionalUnitInterval(JsonElement element, string name, double fallback)
    {
        var result = OptionalFinite(element, name, fallback);
        return result is >= 0 and <= 1
            ? result
            : throw new ArgumentOutOfRangeException(name, "Value must be between 0 and 1.");
    }

    private static CadRectD RequiredRect(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"{name} must be an object with min_x, min_y, max_x, and max_y.");

        var minX = RequiredFinite(value, "min_x");
        var minY = RequiredFinite(value, "min_y");
        var maxX = RequiredFinite(value, "max_x");
        var maxY = RequiredFinite(value, "max_y");
        if (maxX <= minX || maxY <= minY)
            throw new ArgumentException($"{name} max values must be greater than min values.");
        return CadRectD.FromLTRB(minX, minY, maxX, maxY);
    }

    private static double OptionalFinite(JsonElement element, string name, double fallback)
    {
        if (!element.TryGetProperty(name, out var value))
            return fallback;
        if (!value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new ArgumentException($"{name} must be a finite number.");
        return result;
    }

    private static double RequiredFinite(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            !value.TryGetDouble(out var result) ||
            !double.IsFinite(result))
        {
            throw new ArgumentException($"{name} must be a finite number.");
        }

        return result;
    }

    private static double OptionalNonZero(JsonElement element, string name, double fallback)
    {
        var result = OptionalFinite(element, name, fallback);
        if (Math.Abs(result) <= 1e-9)
            throw new ArgumentOutOfRangeException(name, "Value must not be zero.");
        return result;
    }

    private static CadBlockDefinition ResolveBlock(CadDocument document, string value)
    {
        CadBlockDefinition? block = null;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId))
            document.Blocks.TryGetValue(new BlockId(numericId), out block);

        block ??= document.Blocks.Values.FirstOrDefault(candidate =>
            candidate.Kind == CadBlockKind.User &&
            string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase));

        if (block is null || block.Kind != CadBlockKind.User)
            throw new KeyNotFoundException($"User block not found: {value}");
        return block;
    }

    private static double OptionalPositive(JsonElement element, string name, double fallback)
    {
        var result = OptionalFinite(element, name, fallback);
        return result > 0 ? result : throw new ArgumentOutOfRangeException(name, "Value must be greater than zero.");
    }

    private static bool IsNone(string value) => value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                                                 value.Equals("by_layer", StringComparison.OrdinalIgnoreCase);
}
