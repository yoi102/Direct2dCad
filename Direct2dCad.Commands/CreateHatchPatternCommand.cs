using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.Commands;

/// <summary>
/// Creates a hatch pattern needed by an AI-created hatch style and restores the
/// same pattern ID on redo.
/// </summary>
public sealed class CreateHatchPatternCommand : ICadCommand
{
    private readonly string _name;
    private readonly CadHatchLineDefinition[] _lines;
    private readonly string _description;
    private CadHatchPatternDefinition? _createdPattern;

    public string Name => "Create Hatch Pattern";
    public HatchPatternId? CreatedPatternId => _createdPattern?.Id;

    public CreateHatchPatternCommand(
        string name,
        IEnumerable<CadHatchLineDefinition> lines,
        string description = "")
    {
        _name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Pattern name cannot be empty.", nameof(name))
            : name.Trim();
        _lines = lines?.ToArray() ?? throw new ArgumentNullException(nameof(lines));
        if (_lines.Length == 0)
            throw new ArgumentException("At least one hatch line is required.", nameof(lines));
        _description = description ?? string.Empty;
    }

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (_createdPattern is null)
        {
            var patternId = document.CreateHatchPattern(_name, _lines, _description);
            _createdPattern = document.HatchPatterns[patternId];
        }
        else if (!document.HatchPatterns.ContainsKey(_createdPattern.Id))
        {
            document.AddHatchPatternCore(_createdPattern);
        }

        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_createdPattern is not null &&
            !document.Styles.Values.OfType<CadHatchFillStyle>().Any(style =>
                style.PatternId == _createdPattern.Id))
            document.RemoveHatchPatternCore(_createdPattern.Id);

        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }
}
