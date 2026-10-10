using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.System.Com;
using Windows.Win32.System.Memory;

namespace Direct2dCad.Windows.Interop;

// Private DIB fixture: exercises real OLE storage/rendering without changing clipboard.
internal sealed unsafe class OleFixtureData:IDataObject.Interface
{
    internal static FORMATETC Format=>new(){cfFormat=8,dwAspect=(uint)DVASPECT.DVASPECT_CONTENT,lindex=-1,tymed=(uint)TYMED.TYMED_HGLOBAL};
    public HRESULT QueryGetData(FORMATETC* format)=>format is not null&&format->cfFormat==8&&(format->tymed&(uint)TYMED.TYMED_HGLOBAL)!=0?(HRESULT)0:(HRESULT)unchecked((int)0x80040064);
    public HRESULT GetData(FORMATETC* format,STGMEDIUM* medium)
    {
        if(medium is null)return (HRESULT)unchecked((int)0x80004003);
        *medium=default;var hr=QueryGetData(format);if(hr.Failed)return hr;
        var memory=PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE|GLOBAL_ALLOC_FLAGS.GMEM_ZEROINIT,(nuint)(sizeof(BITMAPINFOHEADER)+16));
        if(memory.IsNull)return (HRESULT)unchecked((int)0x8007000E);
        var header=(BITMAPINFOHEADER*)PInvoke.GlobalLock(memory);
        if(header is null){PInvoke.GlobalFree(memory);return (HRESULT)unchecked((int)0x8007000E);}
        header->biSize=(uint)sizeof(BITMAPINFOHEADER);header->biWidth=2;header->biHeight=2;header->biPlanes=1;header->biBitCount=32;header->biSizeImage=16;
        var pixels=(uint*)(header+1);for(var i=0;i<4;i++)pixels[i]=0x0000AA55;PInvoke.GlobalUnlock(memory);
        medium->tymed=TYMED.TYMED_HGLOBAL;medium->u.hGlobal=memory;return (HRESULT)0;
    }
    public HRESULT GetDataHere(FORMATETC* format,STGMEDIUM* medium)=>(HRESULT)unchecked((int)0x80004001);
    public HRESULT GetCanonicalFormatEtc(FORMATETC* input,FORMATETC* output){if(output is not null)*output=default;return (HRESULT)unchecked((int)0x80004001);}
    public HRESULT SetData(FORMATETC* format,STGMEDIUM* medium,BOOL release)=>(HRESULT)unchecked((int)0x80004001);
    public HRESULT EnumFormatEtc(uint direction,IEnumFORMATETC** output)
    {
        if(direction!=1)return (HRESULT)unchecked((int)0x80004001);
        var format=Format;return PInvoke.SHCreateStdEnumFmtEtc(1,&format,output);
    }
    public HRESULT DAdvise(FORMATETC* format,uint flags,IAdviseSink* sink,uint* connection)=>(HRESULT)unchecked((int)0x80040003);
    public HRESULT DUnadvise(uint connection)=>(HRESULT)unchecked((int)0x80040003);
    public HRESULT EnumDAdvise(IEnumSTATDATA** output){if(output is not null)*output=null;return (HRESULT)unchecked((int)0x80040003);}
}
