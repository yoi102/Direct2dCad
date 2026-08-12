using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Editor.Commands;

public sealed class FilterSelectionCommand : SelectionCommandBase
{
    private readonly Func<CadEntity, bool> _filter;
    private readonly CadSelectionMode _mode;
    private readonly BlockId _ownerBlockId;

    public FilterSelectionCommand(
        Func<CadEntity, bool> filter,
        CadSelectionMode mode = CadSelectionMode.Replace,
        BlockId? ownerBlockId = null)
    {
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _mode = mode;
        _ownerBlockId = ownerBlockId ?? BlockId.ModelSpace;
    }

    public override string Name => "Filter Selection";

    protected override void ExecuteSelection(CadEditorCommandContext context)
    {
        var entityIds = context.Document.GetEntitiesInBlock(_ownerBlockId)
            .Where(entity => !entity.IsErased && _filter(entity))
            .Select(entity => entity.Id);
        ApplySelection(context.Selection, entityIds, _mode);
    }
}
