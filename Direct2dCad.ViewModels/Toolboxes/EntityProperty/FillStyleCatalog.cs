using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Toolboxes.EntityProperty;

public enum FillStyleOptionKind
{
    None,
    Solid,
    Hatch,
    Custom
}

public sealed record FillStyleOption(StyleId? Id, string Name, FillStyleOptionKind Kind, string StyleName = "")
{
    public override string ToString() => Name;
}

internal static class FillStyleCatalog
{
    private const string NoFillName = "No Fill";
    private const string SolidFillName = "Solid Fill";
    private static readonly CadColor DefaultSolidFillColor = CadColor.FromArgb(96, 255, 255, 255);
    private static readonly CadColor DefaultHatchForegroundColor = CadColor.FromArgb(96, 255, 255, 255);

    public static CadColor DefaultFillColor => DefaultSolidFillColor;

    private static readonly DefaultHatchDefinition[] DefaultHatches =
    [
        new("ANSI31", "45 degree diagonal hatch", () => CadHatchPatternLines.Diagonal45(10.0)),
        new("Horizontal", "Horizontal line hatch", () => CadHatchPatternLines.Horizontal(10.0)),
        new("Vertical", "Vertical line hatch", () => CadHatchPatternLines.Vertical(10.0)),
        new("Grid", "Square grid hatch", () => CadHatchPatternLines.Grid(10.0)),
        new("Cross 45", "45/135 degree cross hatch", () => CadHatchPatternLines.Cross45(10.0)),
        new("Dashed", "Dashed horizontal hatch", () => CadHatchPatternLines.DashedHorizontal(10.0)),
        new("Dotted", "Dotted hatch", () => CadHatchPatternLines.Dotted(8.0)),
        new("Brick", "Brick hatch", () => CadHatchPatternLines.Brick(20.0, 10.0))
    ];

    public static IReadOnlyList<FillStyleOption> BuildFillStyleOptions(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var options = new List<FillStyleOption>
        {
            new(null, Text("FillNoFill", NoFillName), FillStyleOptionKind.None),
            new(FindFillStyle(document, SolidFillName), Text("FillSolid", SolidFillName), FillStyleOptionKind.Solid, SolidFillName)
        };

        foreach (var hatch in DefaultHatches)
            options.Add(new(
                FindFillStyle(document, hatch.Name),
                Text(hatch.ResourceKey, hatch.Name),
                FillStyleOptionKind.Hatch,
                hatch.Name));

        var knownStyleIds = options
            .Where(x => x.Id is not null)
            .Select(x => x.Id!.Value)
            .ToHashSet();

        options.AddRange(document.Styles.Values
            .OfType<CadFillStyle>()
            .Where(style => !knownStyleIds.Contains(style.Id))
            .OrderBy(style => style.Name, StringComparer.OrdinalIgnoreCase)
            .Select(style => new FillStyleOption(style.Id, style.Name, FillStyleOptionKind.Custom)));

        return options;
    }

    public static FillStyleOption? FindFillStyleOption(
        IReadOnlyList<FillStyleOption> options,
        StyleId? styleId)
    {
        return options.FirstOrDefault(option => Nullable.Equals(option.Id, styleId)) ??
               options.FirstOrDefault();
    }

    public static StyleId? ResolveFillStyleId(CadDocument document, FillStyleOption? option)
        => ResolveFillStyleId(document, option, ResolveFillColor(document, option?.Id, DefaultFillColor));

    public static StyleId? ResolveFillStyleId(CadDocument document, FillStyleOption? option, CadColor fillColor)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (option is null || option.Kind == FillStyleOptionKind.None)
            return null;

        if (option.Id is { } styleId &&
            document.Styles.TryGetValue(styleId, out var existingStyle) &&
            existingStyle is CadFillStyle existingFillStyle)
        {
            return ResolveFillStyleId(document, existingFillStyle, fillColor);
        }

        if (option.Kind == FillStyleOptionKind.Solid)
            return FindSolidFillStyle(document, fillColor) ??
                   document.CreateSolidFillStyle(CreateSolidFillStyleName(fillColor), fillColor);

        if (option.Kind == FillStyleOptionKind.Hatch)
        {
            var styleName = string.IsNullOrWhiteSpace(option.StyleName)
                ? option.Name
                : option.StyleName;
            var hatch = DefaultHatches.FirstOrDefault(x => string.Equals(x.Name, styleName, StringComparison.OrdinalIgnoreCase));
            if (hatch is null)
                return null;

            var patternId = FindHatchPattern(document, hatch.Name) ??
                document.CreateHatchPattern(hatch.Name, hatch.CreateLines(), hatch.Description);
            return FindHatchFillStyle(
                       document,
                       patternId,
                       fillColor,
                       backgroundColor: null,
                       hatchScale: 1.0,
                       hatchAngle: 0.0,
                       hatchOrigin: CadPointD.Origin,
                       isAnnotative: false) ??
                document.CreateHatchFillStyle(
                    CreateHatchFillStyleName(hatch.Name, fillColor),
                    patternId,
                    fillColor,
                    hatchScale: 1.0);
        }

        return null;
    }

    public static CadColor ResolveFillColor(CadDocument document, StyleId? styleId, CadColor fallback)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (styleId is null ||
            !document.Styles.TryGetValue(styleId.Value, out var style))
        {
            return fallback;
        }

        return style switch
        {
            CadGradientFillStyle gradient when gradient.IsSolid && gradient.Stops.Count > 0 => gradient.Stops[0].Color,
            CadHatchFillStyle hatch => hatch.ForegroundColor,
            _ => fallback
        };
    }

    public static bool SupportsFillColor(FillStyleOption? option)
        => option is not null && option.Kind != FillStyleOptionKind.None;

    private static StyleId? ResolveFillStyleId(CadDocument document, CadFillStyle fillStyle, CadColor fillColor)
    {
        if (fillStyle is CadGradientFillStyle gradient)
        {
            return gradient.IsSolid
                ? FindSolidFillStyle(document, fillColor) ??
                  document.CreateSolidFillStyle(CreateSolidFillStyleName(fillColor), fillColor)
                : gradient.Id;
        }

        if (fillStyle is CadHatchFillStyle hatch)
        {
            var patternName = document.HatchPatterns.TryGetValue(hatch.PatternId, out var pattern)
                ? pattern.Name
                : "Hatch";

            return FindHatchFillStyle(
                       document,
                       hatch.PatternId,
                       fillColor,
                       hatch.BackgroundColor,
                       hatch.HatchScale,
                       hatch.HatchAngle,
                       hatch.HatchOrigin,
                       hatch.IsAnnotative) ??
                   document.CreateHatchFillStyle(
                       CreateHatchFillStyleName(patternName, fillColor),
                       hatch.PatternId,
                       fillColor,
                       hatch.BackgroundColor,
                       hatch.HatchScale,
                       hatch.HatchAngle,
                       hatch.HatchOrigin,
                       hatch.IsAnnotative);
        }

        return fillStyle.Id;
    }

    private static StyleId? FindFillStyle(CadDocument document, string name)
    {
        return document.Styles.Values
            .OfType<CadFillStyle>()
            .FirstOrDefault(style => string.Equals(style.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    private static StyleId? FindSolidFillStyle(CadDocument document, CadColor color)
    {
        return document.Styles.Values
            .OfType<CadGradientFillStyle>()
            .FirstOrDefault(style =>
                style.IsSolid &&
                style.Stops.Count > 0 &&
                style.Stops[0].Color == color)
            ?.Id;
    }

    private static StyleId? FindHatchFillStyle(
        CadDocument document,
        HatchPatternId patternId,
        CadColor foregroundColor,
        CadColor? backgroundColor,
        double hatchScale,
        double hatchAngle,
        CadPointD hatchOrigin,
        bool isAnnotative)
    {
        return document.Styles.Values
            .OfType<CadHatchFillStyle>()
            .FirstOrDefault(style =>
                style.PatternId.Equals(patternId) &&
                style.ForegroundColor == foregroundColor &&
                Nullable.Equals(style.BackgroundColor, backgroundColor) &&
                style.HatchScale.Equals(hatchScale) &&
                style.HatchAngle.Equals(hatchAngle) &&
                style.HatchOrigin == hatchOrigin &&
                style.IsAnnotative == isAnnotative)
            ?.Id;
    }

    private static HatchPatternId? FindHatchPattern(CadDocument document, string name)
    {
        return document.HatchPatterns.Values
            .FirstOrDefault(pattern => string.Equals(pattern.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.Id;
    }

    private static string Text(string key, string fallback)
    {
        return Strings.ResourceManager.GetString(key) ?? fallback;
    }

    private static string CreateSolidFillStyleName(CadColor color)
    {
        return string.Equals(ToColorKey(color), ToColorKey(DefaultSolidFillColor), StringComparison.Ordinal)
            ? SolidFillName
            : $"{SolidFillName} {ToColorKey(color)}";
    }

    private static string CreateHatchFillStyleName(string hatchName, CadColor color)
    {
        return string.Equals(ToColorKey(color), ToColorKey(DefaultHatchForegroundColor), StringComparison.Ordinal)
            ? hatchName
            : $"{hatchName} {ToColorKey(color)}";
    }

    private static string ToColorKey(CadColor color)
    {
        return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private sealed record DefaultHatchDefinition(
        string Name,
        string Description,
        Func<IReadOnlyList<CadHatchLineDefinition>> CreateLines)
    {
        public string ResourceKey => Name switch
        {
            "ANSI31" => "FillHatchAnsi31",
            "Horizontal" => "FillHatchHorizontal",
            "Vertical" => "FillHatchVertical",
            "Grid" => "FillHatchGrid",
            "Cross 45" => "FillHatchCross45",
            "Dashed" => "FillHatchDashed",
            "Dotted" => "FillHatchDotted",
            "Brick" => "FillHatchBrick",
            _ => Name
        };
    }
}
