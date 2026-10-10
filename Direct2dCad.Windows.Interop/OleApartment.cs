using Windows.Win32;

namespace Direct2dCad.Windows.Interop;

public sealed unsafe class OleApartment : IDisposable
{
    private readonly int _thread=Environment.CurrentManagedThreadId;
    private bool _disposed;
    public OleApartment()=>PInvoke.OleInitialize(null).ThrowOnFailure();
    public void Dispose()
    {
        if(_disposed)return;
        if(Environment.CurrentManagedThreadId!=_thread)throw new InvalidOperationException("OLE apartment must close on its creating thread.");
        _disposed=true;PInvoke.OleUninitialize();
    }
}
