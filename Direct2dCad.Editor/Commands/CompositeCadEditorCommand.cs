namespace Direct2dCad.Editor.Commands;

public sealed class CompositeCadEditorCommand : ICadEditorCommand
{
    private readonly ICadEditorCommand[] _commands;

    public string Name { get; }

    public CompositeCadEditorCommand(string name, IEnumerable<ICadEditorCommand> commands)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Command Batch" : name.Trim();
        _commands = commands?.ToArray() ?? throw new ArgumentNullException(nameof(commands));

        if (_commands.Length == 0)
            throw new ArgumentException("At least one command is required.", nameof(commands));
    }

    public CadEditorCommandResult Execute(CadEditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<CadEditorCommandResult>(_commands.Length);
        foreach (var command in _commands)
            results.Add(command.Execute(context));

        return CadEditorCommandResult.Combine(results);
    }

    public CadEditorCommandResult Undo(CadEditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<CadEditorCommandResult>(_commands.Length);
        for (var i = _commands.Length - 1; i >= 0; i--)
            results.Add(_commands[i].Undo(context));

        return CadEditorCommandResult.Combine(results);
    }
}
