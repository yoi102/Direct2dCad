using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.Commands;

public sealed class DeleteStyleCommand(StyleId styleId) : ICadCommand
{
    private CadStyle? _snapshot;
    public string Name => "Delete Style";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        if (styleId == StyleId.DefaultGraphic)
            throw new InvalidOperationException("The default graphic style cannot be deleted.");

        _snapshot ??= document.GetStyle<CadStyle>(styleId);
        if (document.GetStyleReferenceCount(styleId) > 0)
            throw new InvalidOperationException($"Style is still referenced: {_snapshot.Name}");

        if (!document.RemoveStyleCore(styleId))
            throw new InvalidOperationException($"Style does not exist: {styleId}");

        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_snapshot is not null && !document.Styles.ContainsKey(styleId))
            document.AddStyleCore(_snapshot);
        return CadDocumentChangeSet.Empty.WithTableChanges(CadDocumentTableChangeKind.Styles);
    }
}
