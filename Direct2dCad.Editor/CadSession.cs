namespace Direct2dCad.Editor;

public sealed class CadSession
{
    public CadEditor Editor { get; }

    public CadSession(CadEditor editor)
    {
        Editor = editor ?? throw new ArgumentNullException(nameof(editor));
    }
}
