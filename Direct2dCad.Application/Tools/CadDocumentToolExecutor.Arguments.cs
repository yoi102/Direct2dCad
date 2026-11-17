using System.Globalization;
using System.Text.Json;
using Direct2dCad.AI.Contracts;
using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.Application.Tools;

public sealed partial class CadDocumentToolExecutor
{
    private static string RequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new ArgumentException($"{name} is required.");
        return value.GetString()!.Trim();
    }

    private static string? OptionalString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
    }

    private static double RequiredDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || !value.TryGetDouble(out var result) || !double.IsFinite(result))
            throw new ArgumentException($"{name} must be a finite number.");
        return result;
    }

    private static double RequiredPositive(JsonElement element, string name)
    {
        var value = RequiredDouble(element, name);
        return value > 0 ? value : throw new ArgumentOutOfRangeException(name, "Value must be greater than zero.");
    }

    private static double OptionalDouble(JsonElement element, string name, double fallback)
    {
        return element.TryGetProperty(name, out var value) && value.TryGetDouble(out var result) && double.IsFinite(result)
            ? result
            : fallback;
    }

    private static double OptionalPositive(JsonElement element, string name, double fallback)
    {
        if (!element.TryGetProperty(name, out _))
            return fallback;
        var value = RequiredDouble(element, name);
        return value > 0 ? value : throw new ArgumentOutOfRangeException(name, "Value must be greater than zero.");
    }

    private static double OptionalNonNegative(JsonElement element, string name, double fallback)
    {
        if (!element.TryGetProperty(name, out _))
            return fallback;
        var value = RequiredDouble(element, name);
        return value >= 0 ? value : throw new ArgumentOutOfRangeException(name, "Value must not be negative.");
    }

    private static bool OptionalBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();
    }

    private static bool? OptionalNullableBool(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    }

    internal static CadPointD[] RequiredPoints(JsonElement arguments, string name, int minimumCount)
    {
        if (!arguments.TryGetProperty(name, out var pointsElement) || pointsElement.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"{name} must be an array.");
        var points = pointsElement.EnumerateArray()
            .Select(point => new CadPointD(RequiredDouble(point, "x"), RequiredDouble(point, "y")))
            .ToArray();
        if (points.Length < minimumCount)
            throw new ArgumentException($"{name} requires at least {minimumCount} points.");
        return points;
    }

    private static CadPointD RequiredPoint(JsonElement arguments, string name)
    {
        if (!arguments.TryGetProperty(name, out var point) || point.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"{name} is required.");
        return new CadPointD(RequiredDouble(point, "x"), RequiredDouble(point, "y"));
    }
}
