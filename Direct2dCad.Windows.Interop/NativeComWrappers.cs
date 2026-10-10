using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.Storage.Xps.Printing;
using Windows.Win32.Storage.Xps;

namespace Direct2dCad.Windows.Interop;

// CsWin32 supplies every ABI signature and callback thunk. ComWrappers supplies
// the runtime's AOT-compatible identity, QueryInterface and reference counting.
internal sealed unsafe class NativeComWrappers : ComWrappers
{
    internal static NativeComWrappers Instance { get; } = new();
    private static readonly ComInterfaceEntry* SiteEntries=Allocate(Entry<IOleClientSite>(),Entry<IAdviseSink>());
    private static readonly ComInterfaceEntry* DataEntries=Allocate(Entry<IDataObject>());
    private static readonly ComInterfaceEntry* PrintEntries=CreatePrintEntries();
    private static ComInterfaceEntry* CreatePrintEntries()=>OperatingSystem.IsWindowsVersionAtLeast(10)?Allocate(Entry<IPrintDocumentPackageTarget>(),Entry<IXpsDocumentPackageTarget>()):null;
    private static ComInterfaceEntry Entry<T>() where T:unmanaged,IComIID,IVTable => new() {IID=T.Guid,Vtable=(nint)T.VTable};
    private static ComInterfaceEntry* Allocate(params ComInterfaceEntry[] entries)
    {
        var memory=(ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(NativeComWrappers),sizeof(ComInterfaceEntry)*entries.Length);
        entries.CopyTo(new Span<ComInterfaceEntry>(memory,entries.Length));return memory;
    }
    protected override ComInterfaceEntry* ComputeVtables(object obj,CreateComInterfaceFlags flags,out int count)
    {
        if(obj is OleSession.ClientSite){count=2;return SiteEntries;}
        if(obj is OleFixtureData){count=1;return DataEntries;}
        if(obj is XpsFileTarget){count=2;return PrintEntries;}
        throw new NotSupportedException("Unsupported COM callback object.");
    }
    protected override object CreateObject(nint externalComObject,CreateObjectFlags flags)=>throw new NotSupportedException("Native objects use CsWin32's typed pointers.");
    protected override void ReleaseObjects(IEnumerable objects)=>throw new NotSupportedException();
    internal static void PopulateIUnknown(IUnknown.Vtbl* vtable)
    {
        GetIUnknownImpl(out var query,out var addRef,out var release);
        var slots=(nint*)vtable;slots[0]=query;slots[1]=addRef;slots[2]=release;
    }
    internal static T* Create<T>(object obj) where T:unmanaged,IComIID
    {
        var unknown=(IUnknown*)Instance.GetOrCreateComInterfaceForObject(obj,CreateComInterfaceFlags.None);
        try{return Query<T>(unknown);}finally{unknown->Release();}
    }
    internal static T* Query<T>(IUnknown* source) where T:unmanaged,IComIID
    {
        var iid=T.Guid;void* result=null;source->QueryInterface(&iid,&result).ThrowOnFailure();return (T*)result;
    }
}
