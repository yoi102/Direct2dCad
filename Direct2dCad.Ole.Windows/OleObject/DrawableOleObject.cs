using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Vanara.PInvoke;
using static Vanara.PInvoke.Gdi32;
using static Vanara.PInvoke.Ole32;
using OleLockBytes = Vanara.PInvoke.Ole32.ILockBytes;
using OleObjectInterface = Vanara.PInvoke.Ole32.IOleObject;
using OlePersistStorage = Vanara.PInvoke.Ole32.IPersistStorage;
using OleStorage = Vanara.PInvoke.Ole32.IStorage;
using OleViewObject = Vanara.PInvoke.Ole32.IViewObject;

namespace Direct2dCad.Ole.Windows;

internal sealed class DrawableOleObject : IDisposable
{
    private const int OleIverbPrimary = 0;
    private const int OleIverbShow = -1;
    private const int OleIverbOpen = -2;

    private readonly OleLockBytes _lockBytes;
    private readonly OleStorage _storage;
    private readonly OleClientSiteBridge _clientSite;
    private readonly OleAdviseSinkBridge _adviseSink;
    private uint _adviseConnection;
    private bool _viewAdviseAttached;
    private bool _isSaving;
    private object? _oleObject;
    private string _name = "OLE Object";

    public DrawableOleObject(
        object oleObject,
        DVASPECT drawAspect,
        OleStorage storage,
        OleLockBytes lockBytes)
    {
        _oleObject = oleObject;
        DrawAspect = drawAspect;
        _storage = storage;
        _lockBytes = lockBytes;
        _clientSite = new OleClientSiteBridge(this);
        _adviseSink = new OleAdviseSinkBridge(this);
        AttachCallbacks();
    }

    public event EventHandler? HostViewChanged;

    public event EventHandler? HostClosed;

    public event EventHandler? Saved;

    public DVASPECT DrawAspect { get; }

    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    internal void OnHostViewChanged()
    {
        try
        {
            HostViewChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // OLE server notification flow must not depend on preview generation.
        }
    }

    internal void OnHostClosed()
    {
        try
        {
            HostClosed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // OLE server notification flow must not depend on preview generation.
        }
    }

    internal void OnSaved()
    {
        try
        {
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // OLE server save must not fail because the host preview failed.
        }
    }

    public void OpenEditor(IntPtr parentHwnd, string containerName)
    {
        if (_oleObject is not OleObjectInterface ole)
            throw new ObjectDisposedException(nameof(DrawableOleObject));

        ole.SetHostNames(containerName, Name);
        var rect = new RECT();
        var message = default(MSG);
        var hr = ole.DoVerb(OleIverbOpen, in message, _clientSite, 0, parentHwnd, in rect);
        if (hr == HRESULT.OLEOBJ_S_INVALIDVERB || hr == HRESULT.E_NOTIMPL)
            hr = ole.DoVerb(OleIverbPrimary, in message, _clientSite, 0, parentHwnd, in rect);
        if (hr == HRESULT.OLEOBJ_S_INVALIDVERB || hr == HRESULT.E_NOTIMPL)
            hr = ole.DoVerb(OleIverbShow, in message, _clientSite, 0, parentHwnd, in rect);

        hr.ThrowIfFailed("IOleObject.DoVerb failed.");
    }

    public double ResolveNaturalAspectRatio()
    {
        if (_oleObject is OleObjectInterface ole &&
            ole.GetExtent(DrawAspect, out var size) == HRESULT.S_OK &&
            size.cx > 0 &&
            size.cy > 0)
        {
            return Math.Clamp(size.cx / (double)size.cy, 1e-6, 1e6);
        }

        return 4.0 / 3.0;
    }

    public byte[] Draw(int width, int height)
    {
        return Draw(width, height, 0, 0, width, height);
    }

    public byte[] Draw(
        int fullWidth,
        int fullHeight,
        int regionX,
        int regionY,
        int regionWidth,
        int regionHeight)
    {
        if (_oleObject is null)
            throw new ObjectDisposedException(nameof(DrawableOleObject));

        if (fullWidth <= 0 || fullHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(fullWidth));
        if (regionX < 0 || regionY < 0 || regionWidth <= 0 || regionHeight <= 0 ||
            regionX > fullWidth - regionWidth || regionY > fullHeight - regionHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(regionX));
        }

        var stride = checked(regionWidth * 4);
        var hdc = CreateCompatibleDC(HDC.NULL);
        if (hdc == HDC.NULL)
            throw new InvalidOperationException("CreateCompatibleDC failed.");

        var bitmapInfo = new BITMAPINFO(regionWidth, -regionHeight, 32);
        bitmapInfo.bmiHeader.biCompression = BitmapCompressionMode.BI_RGB;
        bitmapInfo.bmiHeader.biSizeImage = checked((uint)(stride * regionHeight));

        var bitmap = CreateDIBSection(
            hdc,
            in bitmapInfo,
            DIBColorMode.DIB_RGB_COLORS,
            out var bits,
            Vanara.PInvoke.Gdi32.HSECTION.NULL,
            0);

        if (bitmap == HBITMAP.NULL)
        {
            DeleteDC(hdc);
            throw new InvalidOperationException("CreateDIBSection failed.");
        }

        var previous = SelectObject(hdc, bitmap);
        try
        {
            var clear = new byte[checked(stride * regionHeight)];
            for (var i = 0; i < clear.Length; i += 4)
            {
                clear[i] = 255;
                clear[i + 1] = 255;
                clear[i + 2] = 255;
                clear[i + 3] = 255;
            }

            Marshal.Copy(clear, 0, bits, clear.Length);

            // Keep the full-object scale and shift the requested region into the tile DIB.
            var rect = new RECT(
                -regionX,
                -regionY,
                checked(fullWidth - regionX),
                checked(fullHeight - regionY));
            OleDraw(_oleObject, DrawAspect, hdc, in rect).ThrowIfFailed("OleDraw failed.");

            var pixels = new byte[clear.Length];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (var i = 0; i < pixels.Length; i += 4)
                pixels[i + 3] = 255;

            // The negative DIB height already stores rows top-to-bottom, which is
            // the row order expected by the Direct2D bitmap created by the host.
            return pixels;
        }
        finally
        {
            SelectObject(hdc, previous);
            DeleteObject(bitmap);
            DeleteDC(hdc);
        }
    }

    public byte[] GetBackingStorageBytes()
    {
        SaveToStorageInternal();
        _storage.Commit(STGC.STGC_DEFAULT);

        GetHGlobalFromILockBytes(_lockBytes, out var hGlobal)
            .ThrowIfFailed("GetHGlobalFromILockBytes failed.");

        var storageBytes = HGlobalHelper.HGlobalToBytes(hGlobal);
        return new CadOleStoragePayload(storageBytes, DrawAspect, Name).ToBytes();
    }

    public HRESULT SaveToStorageInternal()
    {
        if (_isSaving)
            return HRESULT.S_OK;

        if (_oleObject is not OlePersistStorage persist)
            return HRESULT.S_OK;

        _isSaving = true;
        try
        {
            if (persist.IsDirty() == HRESULT.S_FALSE)
                return HRESULT.S_OK;

            OleSave(persist, _storage, true).ThrowIfFailed("OleSave failed.");
            persist.SaveCompleted(null);
            _storage.Commit(STGC.STGC_DEFAULT);
            OnSaved();
            return HRESULT.S_OK;
        }
        catch
        {
            return HRESULT.E_FAIL;
        }
        finally
        {
            _isSaving = false;
        }
    }

    private void AttachCallbacks()
    {
        if (_oleObject is OleObjectInterface ole)
        {
            ole.SetClientSite(_clientSite).ThrowIfFailed("IOleObject.SetClientSite failed.");
            OleSetContainedObject(_oleObject, true).ThrowIfFailed("OleSetContainedObject failed.");

            var adviseResult = ole.Advise(_adviseSink, out _adviseConnection);
            if (adviseResult.Failed)
                _adviseConnection = 0;
        }

        if (_oleObject is OleViewObject view)
        {
            var viewAdviseResult = view.SetAdvise(DrawAspect, ADVF.ADVF_PRIMEFIRST, _adviseSink);
            _viewAdviseAttached = viewAdviseResult.Succeeded;
        }
    }

    public void Dispose()
    {
        if (_oleObject is OleObjectInterface ole)
        {
            try
            {
                if (_adviseConnection != 0)
                    _ = ole.Unadvise(_adviseConnection);
            }
            catch
            {
            }

            try
            {
                if (_viewAdviseAttached && _oleObject is OleViewObject view)
                    _ = view.SetAdvise(DrawAspect, 0, null);
            }
            catch
            {
            }

            try
            {
                ole.Close(OLECLOSE.OLECLOSE_NOSAVE);
            }
            catch
            {
            }

            try
            {
                ole.SetClientSite(null);
            }
            catch
            {
            }
        }

        HGlobalHelper.ReleaseComObjectSafe(_oleObject);
        HGlobalHelper.ReleaseComObjectSafe(_storage);
        HGlobalHelper.ReleaseComObjectSafe(_lockBytes);
        _oleObject = null;
        _adviseConnection = 0;
        _viewAdviseAttached = false;
    }
}
