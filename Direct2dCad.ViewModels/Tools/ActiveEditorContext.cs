namespace Direct2dCad.ViewModels.Tools;

public interface IActiveEditorContext
{
    EditorTabViewModel? Current { get; }

    void SetCurrent(EditorTabViewModel? editorTab);
}

internal sealed class ActiveEditorContext : IActiveEditorContext
{
    private EditorTabViewModel? _current;

    public EditorTabViewModel? Current => Volatile.Read(ref _current);

    public void SetCurrent(EditorTabViewModel? editorTab) =>
        Volatile.Write(ref _current, editorTab);
}
