namespace Direct2dCad.Editor.Commands;

public enum CadCommandActivityKind
{
    Execute,
    Undo,
    Redo
}

public enum CadCommandActivityScope
{
    Document,
    Editor
}

public sealed record CadCommandActivity(
    string Name,
    CadCommandActivityKind Kind,
    CadCommandActivityScope Scope,
    int CommandCount,
    bool HasChanges);
