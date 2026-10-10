using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Windows.Interop;

namespace Direct2dCad.Avalonia.Services;

internal static class VectorPrintBridge
{
    public static void ExportXps(Direct2DVectorPage page,string path,double width,double height)
    {
        if(!OperatingSystem.IsWindowsVersionAtLeast(10))throw new PlatformNotSupportedException("Vector printing requires Windows 10 or later.");
        VectorPrinting.ExportXps(page.DevicePointer,page.CommandListPointer,path,(float)width,(float)height);
    }
    public static void Submit(Direct2DVectorPage page,string printer,string jobName,nint devMode,double width,double height,int dpi=300)
    {
        if(!OperatingSystem.IsWindowsVersionAtLeast(10))throw new PlatformNotSupportedException("Vector printing requires Windows 10 or later.");
        VectorPrinting.Submit(page.DevicePointer,page.CommandListPointer,printer,jobName,devMode,(float)width,(float)height,Math.Clamp(dpi,72,1200));
    }
}
