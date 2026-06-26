using Direct2dCad.Db;

namespace Direct2dCad.Editor.Commands;

public abstract class SelectionCommandBase : ICadEditorCommand
{
    private EntityId[]? _previousSelection;

    public abstract string Name { get; }

    public CadEditorCommandResult Execute(CadEditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _previousSelection = context.Selection.EntityIds.ToArray();
        ExecuteSelection(context);
        return CadEditorCommandResult.Selection();
    }

    public CadEditorCommandResult Undo(CadEditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_previousSelection is null)
            return CadEditorCommandResult.Empty;

        context.Selection.Replace(_previousSelection);
        return CadEditorCommandResult.Selection();
    }

    protected abstract void ExecuteSelection(CadEditorCommandContext context);

    protected static void ApplySelection(
        CadSelectionSet selection,
        IEnumerable<EntityId> entityIds,
        CadSelectionMode mode)
    {
        var ids = entityIds.Distinct().ToArray();

        switch (mode)
        {
            case CadSelectionMode.Replace:
                selection.Replace(ids);
                break;
            case CadSelectionMode.Add:
                foreach (var entityId in ids)
                    selection.Add(entityId);
                break;
            case CadSelectionMode.Remove:
                foreach (var entityId in ids)
                    selection.Remove(entityId);
                break;
            case CadSelectionMode.Toggle:
                foreach (var entityId in ids)
                {
                    if (!selection.Remove(entityId))
                        selection.Add(entityId);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }
}
