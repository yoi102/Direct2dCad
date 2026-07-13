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

        var entityChanges = new List<CadEntityChange>();
        var structureChanged = false;
        var layoutsChanged = false;
        var layoutStructureChanged = false;
        var viewSettingsChanged = false;
        var selectionChanged = false;
        var viewChanged = false;

        foreach (var result in results)
        {
            entityChanges.AddRange(result.DocumentChanges.EntityChanges);
            structureChanged |= result.DocumentChanges.AffectsDocumentStructure;
            layoutsChanged |= result.DocumentChanges.AffectsLayouts;
            layoutStructureChanged |= result.DocumentChanges.AffectsLayoutStructure;
            viewSettingsChanged |= result.DocumentChanges.AffectsViewSettings;
            selectionChanged |= result.SelectionChanged;
            viewChanged |= result.ViewChanged;
        }

        var documentChanges = new CadDocumentChangeSet(entityChanges);
        if (structureChanged)
            documentChanges = documentChanges.WithDocumentStructureChanged();
        if (layoutsChanged)
            documentChanges = documentChanges.WithLayoutsChanged();
        if (layoutStructureChanged)
            documentChanges = documentChanges.WithLayoutStructureChanged();
        if (viewSettingsChanged)
            documentChanges = documentChanges.WithViewSettingsChanged();

        return new CadEditorCommandResult(documentChanges)
        {
            SelectionChanged = selectionChanged,
            ViewChanged = viewChanged
        };
    }
}
