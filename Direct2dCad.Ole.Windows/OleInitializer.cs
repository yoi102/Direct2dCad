using Vanara.PInvoke;
using static Vanara.PInvoke.Ole32;

namespace Direct2dCad.Ole.Windows;

internal sealed class OleInitializationScope : IDisposable
{
    private OleInitializationScope()
    {
    }

    public static OleInitializationScope Enter()
    {
        OleInitialize(IntPtr.Zero).ThrowIfFailed("OleInitialize failed.");
        return new OleInitializationScope();
    }

    public void Dispose()
    {
        OleUninitialize();
    }
}
