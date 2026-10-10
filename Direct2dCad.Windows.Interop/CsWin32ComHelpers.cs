using Direct2dCad.Windows.Interop;

namespace Windows.Win32;

internal static unsafe partial class ComHelpers
{
    static partial void PopulateIUnknownImpl<TComInterface>(System.Com.IUnknown.Vtbl* vtable)
        where TComInterface:unmanaged => NativeComWrappers.PopulateIUnknown(vtable);
}
