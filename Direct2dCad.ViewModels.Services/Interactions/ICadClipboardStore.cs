using Direct2dCad.Commands.Clipboard;

namespace Direct2dCad.ViewModels.Services.Interactions;

public interface ICadClipboardStore
{
    CadClipboardSnapshot? Snapshot { get; }
    void Set(CadClipboardSnapshot? snapshot);
    void Clear();
}

public sealed class CadClipboardStore : ICadClipboardStore
{
    public CadClipboardSnapshot? Snapshot { get; private set; }

    public void Set(CadClipboardSnapshot? snapshot)
    {
        Snapshot = snapshot;
    }

    public void Clear()
    {
        Snapshot = null;
    }
}
