using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.Editor;

public readonly record struct CadSelectionAvailability(bool HasSelection, bool CanDelete, bool CanCreateBlock);

public sealed class CadSelectionAvailabilityCache
{
    private (CadEditor Editor, long Selection, long Access, BlockId Owner)? _key;
    private CadSelectionAvailability _value;
    public long Version { get; private set; }

    public CadSelectionAvailability Get(CadEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var key = (editor, editor.Selection.Version, editor.EntityAccessVersion, editor.ActiveOwnerBlockId);
        if (_key == key)
            return _value;

        var anyEditable = false;
        var allEditableInOwner = editor.Selection.Count > 0;
        foreach (var id in editor.Selection.EntityIds)
        {
            var exists = editor.Document.TryGetEntity(id, out var entity) && entity is not null;
            var editable = exists && CadEntityAccessPolicy.IsEditable(editor.Document, entity!);
            anyEditable |= editable;
            allEditableInOwner &= editable && entity!.OwnerBlockId == editor.ActiveOwnerBlockId;
            if (anyEditable && !allEditableInOwner)
                break;
        }
        var canCreate = allEditableInOwner && editor.Document.Layers.Values.Any(layer =>
            CadEntityAccessPolicy.CanAddToLayer(editor.Document, layer.Id));
        _key = key;
        _value = new(editor.Selection.Count > 0, anyEditable, canCreate);
        Version = unchecked(Version + 1);
        return _value;
    }
}
