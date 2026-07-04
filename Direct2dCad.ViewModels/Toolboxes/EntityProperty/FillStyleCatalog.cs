using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Styles.FillStyles;
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
    private static readonly CadColor DefaultHatchForegroundColor = CadColor.FromRgb(180, 220, 255);

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
    {
        ArgumentNullException.ThrowIfNull(document);

        if (option is null || option.Kind == FillStyleOptionKind.None)
            return null;

        if (option.Id is { } styleId)
            return styleId;

        if (option.Kind == FillStyleOptionKind.Solid)
            return FindFillStyle(document, SolidFillName) ??
                   document.CreateSolidFillStyle(SolidFillName, DefaultSolidFillColor);

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
            return FindFillStyle(document, hatch.Name) ??
                document.CreateHatchFillStyle(
                    hatch.Name,
                    patternId,
                    DefaultHatchForegroundColor,
                    hatchScale: 1.0);
        }

        return null;
    }

    private static StyleId? FindFillStyle(CadDocument document, string name)
    {
        return document.Styles.Values
            .OfType<CadFillStyle>()
            .FirstOrDefault(style => string.Equals(style.Name, name, StringComparison.OrdinalIgnoreCase))
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
