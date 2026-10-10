using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.Graphics.Direct2D;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.Graphics.Printing.PrintTicket;
using Windows.Win32.Storage.Xps.Printing;

namespace Direct2dCad.Windows.Interop;

[System.Runtime.Versioning.SupportedOSPlatform("windows10.0")]
public static unsafe class VectorPrinting
{
    private static void PrintPage(nint device,nint commands,IPrintDocumentPackageTarget* target,float width,float height,float dpi)
    {
        if(device==0||commands==0||target is null||!float.IsFinite(width)||!float.IsFinite(height)||width<=0||height<=0)throw new ArgumentOutOfRangeException(nameof(width));
        var clsid=PInvoke.CLSID_WICImagingFactory;var iid=IWICImagingFactory.IID_Guid;
        IWICImagingFactory* wic=null;ID2D1PrintControl* control=null;
        try
        {
            PInvoke.CoCreateInstance(&clsid,null,CLSCTX.CLSCTX_INPROC_SERVER,&iid,(void**)&wic).ThrowOnFailure();
            D2D1_PRINT_CONTROL_PROPERTIES properties=new(){fontSubset=D2D1_PRINT_FONT_SUBSET_MODE.D2D1_PRINT_FONT_SUBSET_MODE_DEFAULT,rasterDPI=dpi,colorSpace=D2D1_COLOR_SPACE.D2D1_COLOR_SPACE_SRGB};
            ((ID2D1Device*)device)->CreatePrintControl(wic,target,&properties,&control);
            try{control->AddPage((ID2D1CommandList*)commands,new(){width=width,height=height},null,null,null);}
            finally{control->Close();}
        }
        finally{if(control is not null)control->Release();if(wic is not null)wic->Release();}
    }
    public static void ExportXps(nint device,nint commands,string path,float width,float height)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if(File.Exists(path))throw new IOException("XPS output already exists.");
        using var managed=new XpsFileTarget(path);
        var target=NativeComWrappers.Create<IPrintDocumentPackageTarget>(managed);
        try{PrintPage(device,commands,target,width,height,300);}finally{target->Release();}
    }
    internal static IStream* CreatePrintTicket(string printer, nint devMode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printer);
        if (devMode == 0) return null;
        IStream* ticket = null;
        HPTPROVIDER provider = default;
        fixed (char* printerName = printer)
            PInvoke.PTOpenProvider(new(printerName), 1, &provider).ThrowOnFailure();
        try
        {
            PInvoke.CreateStreamOnHGlobal(default, true, &ticket).ThrowOnFailure();
            var mode = (DEVMODEW*)devMode;
            // The API consumes an opaque, driver-validated DEVMODE buffer. CsWin32's
            // metadata uses DEVMODEA*, but printing supplies the complete Unicode bytes.
            PInvoke.PTConvertDevModeToPrintTicket(provider,
                (uint)(mode->dmSize + mode->dmDriverExtra), (DEVMODEA*)mode,
                EPrintTicketScope.kPTJobScope, ticket).ThrowOnFailure();
            ticket->Seek(0, SeekOrigin.Begin, null);
            return ticket;
        }
        catch
        {
            if (ticket is not null) ticket->Release();
            throw;
        }
        finally
        {
            // Release the provider even on a conversion failure; do not mask that error.
            _ = PInvoke.PTCloseProvider(provider);
        }
    }
    public static void Submit(nint device,nint commands,string printer,string jobName,nint devMode,float width,float height,float dpi)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printer);
        IStream* ticket=null;IPrintDocumentPackageTargetFactory* factory=null;IPrintDocumentPackageTarget* target=null;
        try
        {
            fixed(char* printerName=printer,job=jobName)
            {
                ticket = CreatePrintTicket(printer, devMode);
                var clsid=typeof(PrintDocumentPackageTargetFactory).GUID;var iid=IPrintDocumentPackageTargetFactory.IID_Guid;
                PInvoke.CoCreateInstance(&clsid,null,CLSCTX.CLSCTX_INPROC_SERVER,&iid,(void**)&factory).ThrowOnFailure();
                factory->CreateDocumentPackageTargetForPrintJob(new(printerName),new(job),null,ticket,&target);
                try{PrintPage(device,commands,target,width,height,Math.Clamp(dpi,72,1200));}
                catch{try{target->Cancel();}catch(System.Runtime.InteropServices.COMException){ }throw;}
            }
        }
        finally{if(target is not null)target->Release();if(factory is not null)factory->Release();if(ticket is not null)ticket->Release();}
    }
}
