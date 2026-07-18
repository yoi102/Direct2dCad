namespace Direct2dCad.Editor.Commands;

public sealed class CadEditorCommandResult
{
    public static CadEditorCommandResult Empty { get; } = new(CadDocumentChangeSet.Empty);

    public CadDocumentChangeSet DocumentChanges { get; }
    public bool SelectionChanged { get; init; }
    public bool ViewChanged { get; init; }

    public bool HasChanges =>
        DocumentChanges.DocumentChanged ||
        SelectionChanged ||
        ViewChanged;

    public CadEditorCommandResult(CadDocumentChangeSet documentChanges)
    {
        DocumentChanges = documentChanges ?? throw new ArgumentNullException(nameof(documentChanges));
    }

    public static CadEditorCommandResult FromDocument(CadDocumentChangeSet documentChanges)
    {
        return new CadEditorCommandResult(documentChanges);
    }

    public static CadEditorCommandResult Selection()
    {
        return new CadEditorCommandResult(CadDocumentChangeSet.Empty)
        {
            SelectionChanged = true
        };
    }

    public static CadEditorCommandResult View()
    {
        return new CadEditorCommandResult(CadDocumentChangeSet.Empty)
        {
            ViewChanged = true
        };
    }

    public static CadEditorCommandResult Combine(IEnumerable<CadEditorCommandResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var documentChanges = new List<CadDocumentChangeSet>();
        var selectionChanged = false;
        var viewChanged = false;

        foreach (var result in results)
        {
            documentChanges.Add(result.DocumentChanges);
            selectionChanged |= result.SelectionChanged;
            viewChanged |= result.ViewChanged;
        }

        return new CadEditorCommandResult(CadDocumentChangeSet.Combine(documentChanges))
        {
            SelectionChanged = selectionChanged,
            ViewChanged = viewChanged
        };
    }
}
