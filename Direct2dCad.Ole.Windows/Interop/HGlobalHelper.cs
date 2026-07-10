using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;

namespace Direct2dCad.Ole.Windows;

internal static class HGlobalHelper
{
    public static IntPtr BytesToHGlobal(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var hGlobal = GlobalAlloc(GMEM.GHND, bytes.Length);
        if (hGlobal == HGLOBAL.NULL)
            throw new OutOfMemoryException("GlobalAlloc failed.");

        var target = GlobalLock(hGlobal);
        if (target == IntPtr.Zero)
        {
            GlobalFree(hGlobal);
            throw new InvalidOperationException("GlobalLock failed.");
        }

        try
        {
            Marshal.Copy(bytes, 0, target, bytes.Length);
            return (IntPtr)hGlobal;
        }
        finally
        {
            GlobalUnlock(hGlobal);
        }
    }

    public static byte[] HGlobalToBytes(IntPtr hGlobal)
    {
        if (hGlobal == IntPtr.Zero)
            return [];

        var memory = (HGLOBAL)hGlobal;
        var size = GlobalSize(memory);
        if (size == SizeT.Zero)
            return [];

        var source = GlobalLock(memory);
        if (source == IntPtr.Zero)
            throw new InvalidOperationException("GlobalLock failed.");

        try
        {
            var bytes = new byte[checked((int)size)];
            Marshal.Copy(source, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            GlobalUnlock(memory);
        }
    }

    public static void ReleaseComObjectSafe(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }
}
