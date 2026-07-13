using Direct2dCad.Db;

namespace Direct2dCad.Editor.Commands;

public sealed class SetSelectionCommand : SelectionCommandBase
{
    private readonly EntityId[] _entityIds;

    public override string Name { get; }

    public SetSelectionCommand(
        IEnumerable<EntityId> entityIds,
        string name = "Set Selection")
    {
        ArgumentNullException.ThrowIfNull(entityIds);
        _entityIds = entityIds.Distinct().ToArray();
        Name = string.IsNullOrWhiteSpace(name) ? "Set Selection" : name.Trim();
    }

    protected override void ExecuteSelection(CadEditorCommandContext context)
    {
        ApplySelection(context.Selection, _entityIds, CadSelectionMode.Replace);
    }
}
