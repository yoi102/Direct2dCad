using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Direct2dCad.Ole.Windows;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class OleAdviseSinkBridge(DrawableOleObject owner) : IAdviseSink
{
    public void OnDataChange(ref FORMATETC format, ref STGMEDIUM medium)
    {
        owner.OnHostViewChanged();
    }

    public void OnViewChange(int aspect, int index)
    {
        owner.OnHostViewChanged();
    }

    public void OnRename(IMoniker moniker)
    {
    }

    public void OnSave()
    {
        owner.OnSaved();
    }

    public void OnClose()
    {
        owner.OnHostClosed();
    }
}
