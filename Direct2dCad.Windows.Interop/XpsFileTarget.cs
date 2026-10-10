using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.Storage.Xps;
using Windows.Win32.Storage.Xps.Printing;
using Windows.Win32.Storage.Packaging.Opc;

namespace Direct2dCad.Windows.Interop;

internal sealed unsafe class XpsFileTarget:IPrintDocumentPackageTarget.Interface,IXpsDocumentPackageTarget.Interface,IDisposable
{
    private readonly string _path;
    private IXpsOMObjectFactory* _factory;
    private IXpsOMPackageWriter* _writer;
    internal XpsFileTarget(string path)
    {
        _path=path;var clsid=typeof(XpsOMObjectFactory).GUID;var iid=IXpsOMObjectFactory.IID_Guid;
        fixed(IXpsOMObjectFactory** result=&_factory)PInvoke.CoCreateInstance(&clsid,null,CLSCTX.CLSCTX_INPROC_SERVER,&iid,(void**)result).ThrowOnFailure();
    }
    public HRESULT GetPackageTargetTypes(uint* count,Guid** types)
    {
        if(count is null||types is null)return (HRESULT)unchecked((int)0x80004003);
        *count=0;*types=(Guid*)PInvoke.CoTaskMemAlloc((nuint)sizeof(Guid));
        if(*types is null)return (HRESULT)unchecked((int)0x8007000E);
        **types=PInvoke.ID_DOCUMENTPACKAGETARGET_MSXPS;*count=1;return (HRESULT)0;
    }
    public HRESULT GetPackageTarget(Guid* type,Guid* iid,void** result)
    {
        if(result is null)return (HRESULT)unchecked((int)0x80004003);*result=null;
        if(type is null||iid is null)return (HRESULT)unchecked((int)0x80004003);
        if(*type!=PInvoke.ID_DOCUMENTPACKAGETARGET_MSXPS)return (HRESULT)unchecked((int)0x80004002);
        var unknown=(IUnknown*)NativeComWrappers.Instance.GetOrCreateComInterfaceForObject(this,System.Runtime.InteropServices.CreateComInterfaceFlags.None);
        try{return unknown->QueryInterface(iid,result);}finally{unknown->Release();}
    }
    public HRESULT Cancel()=>(HRESULT)0;
    public HRESULT GetXpsOMPackageWriter(IOpcPartUri* sequence,IOpcPartUri* discard,IXpsOMPackageWriter** result)
    {
        if(result is null)return (HRESULT)unchecked((int)0x80004003);*result=null;
        if(_factory is null)return (HRESULT)unchecked((int)0x8000FFFF);
        if(_writer is null)
        {
            fixed(char* path=_path)_writer=_factory->CreatePackageWriterOnFile(new(path),null,0x80,true,XPS_INTERLEAVING.XPS_INTERLEAVING_OFF,sequence,null,null,null,discard);
        }
        _writer->AddRef();*result=_writer;return (HRESULT)0;
    }
    public HRESULT GetXpsOMFactory(IXpsOMObjectFactory** result)
    {
        if(result is null)return (HRESULT)unchecked((int)0x80004003);*result=null;
        if(_factory is null)return (HRESULT)unchecked((int)0x8000FFFF);
        _factory->AddRef();*result=_factory;return (HRESULT)0;
    }
    public HRESULT GetXpsType(XPS_DOCUMENT_TYPE* type){if(type is null)return (HRESULT)unchecked((int)0x80004003);*type=XPS_DOCUMENT_TYPE.XPS_DOCUMENT_TYPE_XPS;return (HRESULT)0;}
    public void Dispose(){if(_writer is not null){_writer->Release();_writer=null;}if(_factory is not null){_factory->Release();_factory=null;}}
}
