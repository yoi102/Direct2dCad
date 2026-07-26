using System.Runtime.InteropServices.ComTypes;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;
using static Vanara.PInvoke.Ole32;
using OleLockBytes = Vanara.PInvoke.Ole32.ILockBytes;
using OleStorage = Vanara.PInvoke.Ole32.IStorage;

namespace Direct2dCad.Ole.Windows;

internal static class DrawableOleObjectFactory
{
    private const int OleSStatic = 0x00040001;
    private static readonly Guid IidIUnknown = new("00000000-0000-0000-C000-000000000046");

    public static DrawableOleObject? CreateFromClipboard()
    {
        OleGetClipboard(out var dataObject).ThrowIfFailed("OleGetClipboard failed.");
        try
        {
            var queryResult = OleQueryCreateFromData(dataObject);
            if (queryResult == OleSStatic)
                return null;

            queryResult.ThrowIfFailed("Clipboard data cannot create an OLE object.");
            return CreateFromDataObject(dataObject);
        }
        finally
        {
            HGlobalHelper.ReleaseComObjectSafe(dataObject);
        }
    }

    public static DrawableOleObject CreateFromDataObject(IDataObject dataObject)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        CreateILockBytesOnHGlobal(IntPtr.Zero, true, out var lockBytes)
            .ThrowIfFailed("CreateILockBytesOnHGlobal failed.");

        OleStorage? storage = null;
        object? oleObject = null;
        try
        {
            StgCreateDocfileOnILockBytes(
                lockBytes,
                STGM.STGM_CREATE | STGM.STGM_READWRITE | STGM.STGM_SHARE_EXCLUSIVE | STGM.STGM_TRANSACTED,
                0,
                out storage).ThrowIfFailed("StgCreateDocfileOnILockBytes failed.");

            var format = new FORMATETC
            {
                cfFormat = 0,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_NULL
            };

            var iid = IidIUnknown;
            OleCreateFromData(
                dataObject,
                in iid,
                OLERENDER.OLERENDER_DRAW,
                in format,
                new OleClientSiteBridge(null),
                storage,
                out oleObject).ThrowIfFailed("OleCreateFromData failed.");

            storage.Commit(STGC.STGC_DEFAULT);
            return new DrawableOleObject(oleObject, DVASPECT.DVASPECT_CONTENT, storage, lockBytes);
        }
        catch
        {
            HGlobalHelper.ReleaseComObjectSafe(oleObject);
            HGlobalHelper.ReleaseComObjectSafe(storage);
            HGlobalHelper.ReleaseComObjectSafe(lockBytes);
            throw;
        }
    }

    internal static DrawableOleObject CreateFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException("The OLE source file does not exist.", filePath);

        CreateILockBytesOnHGlobal(IntPtr.Zero, true, out var lockBytes)
            .ThrowIfFailed("CreateILockBytesOnHGlobal failed.");

        OleStorage? storage = null;
        object? oleObject = null;
        try
        {
            StgCreateDocfileOnILockBytes(
                lockBytes,
                STGM.STGM_CREATE | STGM.STGM_READWRITE | STGM.STGM_SHARE_EXCLUSIVE | STGM.STGM_TRANSACTED,
                0,
                out storage).ThrowIfFailed("StgCreateDocfileOnILockBytes failed.");

            var classId = Guid.Empty;
            var iid = IidIUnknown;
            var format = new FORMATETC
            {
                cfFormat = 0,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_NULL
            };
            OleCreateFromFile(
                in classId,
                filePath,
                in iid,
                OLERENDER.OLERENDER_DRAW,
                in format,
                new OleClientSiteBridge(null),
                storage,
                out oleObject).ThrowIfFailed("OleCreateFromFile failed.");

            storage.Commit(STGC.STGC_DEFAULT);
            return new DrawableOleObject(
                oleObject,
                DVASPECT.DVASPECT_CONTENT,
                storage,
                lockBytes)
            {
                Name = Path.GetFileName(filePath)
            };
        }
        catch
        {
            HGlobalHelper.ReleaseComObjectSafe(oleObject);
            HGlobalHelper.ReleaseComObjectSafe(storage);
            HGlobalHelper.ReleaseComObjectSafe(lockBytes);
            throw;
        }
    }

    public static DrawableOleObject CreateFromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var payload = CadOleStoragePayload.FromBytes(bytes);
        var hGlobal = HGlobalHelper.BytesToHGlobal(payload.StorageBytes);
        OleLockBytes? lockBytes = null;
        OleStorage? storage = null;
        object? oleObject = null;

        try
        {
            CreateILockBytesOnHGlobal(hGlobal, true, out lockBytes)
                .ThrowIfFailed("CreateILockBytesOnHGlobal failed.");
            hGlobal = IntPtr.Zero;

            StgOpenStorageOnILockBytes(
                lockBytes,
                null,
                STGM.STGM_READWRITE | STGM.STGM_SHARE_EXCLUSIVE | STGM.STGM_TRANSACTED,
                null,
                0,
                out storage).ThrowIfFailed("StgOpenStorageOnILockBytes failed.");

            var iid = IidIUnknown;
            OleLoad(storage, in iid, new OleClientSiteBridge(null), out oleObject)
                .ThrowIfFailed("OleLoad failed.");

            return new DrawableOleObject(oleObject, payload.DrawAspect, storage, lockBytes)
            {
                Name = string.IsNullOrWhiteSpace(payload.Name) ? "OLE Object" : payload.Name
            };
        }
        catch
        {
            HGlobalHelper.ReleaseComObjectSafe(oleObject);
            HGlobalHelper.ReleaseComObjectSafe(storage);
            HGlobalHelper.ReleaseComObjectSafe(lockBytes);
            if (hGlobal != IntPtr.Zero)
                GlobalFree((HGLOBAL)hGlobal);

            throw;
        }
    }
}
