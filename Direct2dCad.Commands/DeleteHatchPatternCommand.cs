using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Styles.FillStyles;

namespace Direct2dCad.Commands;

public sealed class DeleteHatchPatternCommand(HatchPatternId patternId) : ICadCommand
{
    private CadHatchPatternDefinition? _snapshot;
    public string Name => "Delete Hatch Pattern";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        _snapshot ??= document.GetHatchPattern(patternId);
        if (document.GetHatchPatternReferenceCount(patternId) > 0)
            throw new InvalidOperationException($"Hatch pattern is still referenced: {_snapshot.Name}");
        if (!document.RemoveHatchPatternCore(patternId))
            throw new InvalidOperationException($"Hatch pattern does not exist: {patternId}");
        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_snapshot is not null && !document.HatchPatterns.ContainsKey(patternId))
            document.AddHatchPatternCore(_snapshot);
        return CadDocumentChangeSet.Empty.WithDocumentStructureChanged();
    }
}
