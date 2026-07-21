using Direct2dCad.Commands.Clipboard;

namespace Direct2dCad.ViewModels.Services.Interactions;

public interface ICadClipboardStore
{
    CadClipboardSnapshot? Snapshot { get; }
    bool HasUserCopySnapshot { get; }
    void Set(CadClipboardSnapshot? snapshot, bool isUserCopySnapshot);
    void Clear();
}

public sealed class CadClipboardStore : ICadClipboardStore
{
    public CadClipboardSnapshot? Snapshot { get; private set; }
    public bool HasUserCopySnapshot { get; private set; }

    public void Set(CadClipboardSnapshot? snapshot, bool isUserCopySnapshot)
    {
        Snapshot = snapshot;
        HasUserCopySnapshot = snapshot is not null && isUserCopySnapshot;
    }

    public void Clear()
    {
        Snapshot = null;
        HasUserCopySnapshot = false;
    }
}
