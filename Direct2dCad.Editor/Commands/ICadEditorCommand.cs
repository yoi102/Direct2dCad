namespace Direct2dCad.Editor.Commands;

public interface ICadEditorCommand
{
    string Name { get; }

    CadEditorCommandResult Execute(CadEditorCommandContext context);

    CadEditorCommandResult Undo(CadEditorCommandContext context);
}
