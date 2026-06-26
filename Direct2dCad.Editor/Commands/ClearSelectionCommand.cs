namespace Direct2dCad.Editor.Commands;

public sealed class ClearSelectionCommand : SelectionCommandBase
{
    public override string Name => "Clear Selection";

    protected override void ExecuteSelection(CadEditorCommandContext context)
    {
        context.Selection.Clear();
    }
}
