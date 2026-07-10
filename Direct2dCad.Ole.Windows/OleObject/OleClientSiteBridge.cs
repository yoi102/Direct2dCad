using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Vanara.PInvoke;
using OleClientSiteInterface = Vanara.PInvoke.Ole32.IOleClientSite;
using OleContainerInterface = Vanara.PInvoke.Ole32.IOleContainer;
using static Vanara.PInvoke.Ole32;

namespace Direct2dCad.Ole.Windows;

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class OleClientSiteBridge(DrawableOleObject? owner) : OleClientSiteInterface
{
    public HRESULT SaveObject()
    {
        return owner?.SaveToStorageInternal() ?? HRESULT.S_FALSE;
    }

    public HRESULT GetMoniker(OLEGETMONIKER dwAssign, OLEWHICHMK dwWhichMoniker, out IMoniker ppmk)
    {
        ppmk = null!;
        return HRESULT.E_NOTIMPL;
    }

    public HRESULT GetContainer(out OleContainerInterface ppContainer)
    {
        ppContainer = null!;
        return HRESULT.E_NOINTERFACE;
    }

    public HRESULT ShowObject() => HRESULT.S_OK;

    public HRESULT OnShowWindow(bool fShow)
    {
        if (!fShow)
            owner?.OnHostClosed();

        return HRESULT.S_OK;
    }

    public HRESULT RequestNewObjectLayout() => HRESULT.S_OK;
}
