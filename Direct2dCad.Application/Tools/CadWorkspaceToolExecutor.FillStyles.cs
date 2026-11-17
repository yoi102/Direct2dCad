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
    private static StyleId? ResolveGraphicStyle(CadDocument document, string value)
    {
        if (IsNone(value))
            return null;
        var style = document.Styles.Values.OfType<CadGraphicStyle>().FirstOrDefault(candidate =>
            string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Id.Value.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal));
        return style?.Id ?? throw new ArgumentException($"Graphic style not found: {value}");
    }

    private static StyleId? ResolveFillStyle(CadDocumentToolExecutor executor, JsonElement arguments)
    {
        var document = executor.Session.CadEditor.Document;
        var mode = RequiredString(arguments, "mode").Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant();
        if (mode is "none" or "no_fill")
            return null;
        if (mode == "style")
            return ResolveExistingFillStyle(document, RequiredString(arguments, "style"));

        var color = HasValue(arguments, "color")
            ? ParseColor(RequiredString(arguments, "color"))
            : FillStyleCatalog.DefaultFillColor;
        var options = FillStyleCatalog.BuildFillStyleOptions(document);
        if (mode == "solid")
        {
            var option = options.First(candidate => candidate.Kind == FillStyleOptionKind.Solid);
            var solidStyle = document.Styles.Values.OfType<CadGradientFillStyle>().FirstOrDefault(style =>
                style.IsSolid && style.Stops.Count > 0 && style.Stops[0].Color == color);
            if (solidStyle is not null)
                return solidStyle.Id;

            var command = CreateFillStyleCommand.Solid(
                option.StyleName.Length == 0 ? option.Name : option.StyleName,
                color);
            executor.ExecuteCommand(command);
            return command.CreatedStyleId;
        }
        if (mode == "gradient")
        {
            var kind = ParseGradientKind(arguments);
            var stops = ParseGradientStops(arguments);
            var gradientScale = OptionalPositive(arguments, "gradient_scale", 1.0);
            var gradientAngle = OptionalFinite(arguments, "gradient_angle_degrees", 0.0) * Math.PI / 180.0;
            var gradientOrigin = new CadPointD(
                OptionalFinite(arguments, "gradient_origin_x", 0.0),
                OptionalFinite(arguments, "gradient_origin_y", 0.0));
            var centered = OptionalBool(arguments, "gradient_centered", true);
            var existingGradient = document.Styles.Values.OfType<CadGradientFillStyle>().FirstOrDefault(style =>
                style.GradientKind == kind &&
                style.GradientAngle.Equals(gradientAngle) &&
                style.GradientScale.Equals(gradientScale) &&
                style.GradientOrigin.Equals(gradientOrigin) &&
                style.IsCentered == centered &&
                style.Stops.SequenceEqual(stops));
            if (existingGradient is not null)
                return existingGradient.Id;

            var command = CreateFillStyleCommand.Gradient(
                $"{kind} gradient scale {gradientScale:0.###} angle {gradientAngle:0.###}",
                kind,
                stops,
                gradientAngle,
                gradientScale,
                gradientOrigin,
                centered);
            executor.ExecuteCommand(command);
            return command.CreatedStyleId;
        }
        if (mode != "hatch")
            throw new ArgumentException($"Unsupported fill mode: {mode}");

        var requestedPattern = OptionalString(arguments, "pattern") ?? "ANSI31";
        var pattern = ResolveHatchPattern(executor, document, options, requestedPattern, color);
        var scale = OptionalPositive(arguments, "scale", 1.0);
        var angle = OptionalFinite(arguments, "angle_degrees", 0.0) * Math.PI / 180.0;
        var origin = new CadPointD(
            OptionalFinite(arguments, "origin_x", 0.0),
            OptionalFinite(arguments, "origin_y", 0.0));

        var hatchStyle = document.Styles.Values.OfType<CadHatchFillStyle>().FirstOrDefault(style =>
            style.PatternId.Equals(pattern.Id) &&
            style.ForegroundColor.Equals(color) &&
            style.HatchScale.Equals(scale) &&
            style.HatchAngle.Equals(angle) &&
            style.HatchOrigin.Equals(origin) &&
            !style.IsAnnotative);
        if (hatchStyle is not null)
            return hatchStyle.Id;

        var hatchCommand = CreateFillStyleCommand.Hatch(
            $"{pattern.Name} {ColorText(color)} scale {scale:0.###} angle {angle:0.###}",
            pattern.Id,
            color,
            scale,
            angle,
            origin);
        executor.ExecuteCommand(hatchCommand);
        return hatchCommand.CreatedStyleId;
    }

    private static StyleId ResolveExistingFillStyle(CadDocument document, string value)
    {
        var style = document.Styles.Values.OfType<CadFillStyle>().FirstOrDefault(candidate =>
            string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Id.Value.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal));
        return style?.Id ?? throw new ArgumentException($"Fill style not found: {value}");
    }

    private static CadHatchPatternDefinition ResolveHatchPattern(
        CadDocumentToolExecutor executor,
        CadDocument document,
        IReadOnlyList<FillStyleOption> options,
        string requestedPattern,
        CadColor color)
    {
        var existing = document.HatchPatterns.Values.FirstOrDefault(pattern =>
            string.Equals(pattern.Name, requestedPattern, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(pattern.Id.Value.ToString(CultureInfo.InvariantCulture), requestedPattern, StringComparison.Ordinal));
        if (existing is not null)
            return existing;

        var option = options.FirstOrDefault(candidate =>
            candidate.Kind == FillStyleOptionKind.Hatch &&
            (string.Equals(candidate.StyleName, requestedPattern, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(candidate.Name, requestedPattern, StringComparison.OrdinalIgnoreCase)));
        if (option is null)
            throw new ArgumentException($"Hatch pattern not found: {requestedPattern}");

        var definition = FillStyleCatalog.GetDefaultHatchDefinition(option.StyleName)
            ?? throw new InvalidOperationException($"Hatch pattern could not be created: {requestedPattern}");
        var command = new CreateHatchPatternCommand(
            definition.Name,
            definition.Lines,
            definition.Description);
        executor.ExecuteCommand(command);
        var patternId = command.CreatedPatternId
            ?? throw new InvalidOperationException($"Hatch pattern could not be created: {requestedPattern}");
        return document.HatchPatterns[patternId];
    }

    private static CadGradientKind ParseGradientKind(JsonElement arguments)
    {
        var value = OptionalString(arguments, "gradient_kind") ?? "linear";
        return value.Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "linear" => CadGradientKind.Linear,
            "radial" => CadGradientKind.Radial,
            _ => throw new ArgumentException("gradient_kind must be linear or radial.")
        };
    }
}
