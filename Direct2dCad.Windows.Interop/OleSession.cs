using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;
using Windows.Win32.Graphics.Gdi;
using System.Runtime.InteropServices;

namespace Direct2dCad.Windows.Interop;

public sealed unsafe class OleSession : IDisposable
{
    private ILockBytes* _bytes;
    private IStorage* _storage;
    private IOleObject* _object;
    private IOleClientSite* _sitePointer;
    private ClientSite? _site;
    private uint _cookie;
    private bool _saving,_disposed;
    private readonly int _thread=Environment.CurrentManagedThreadId;
    public int Aspect { get; }
    public Action<bool>? Changed { get; set; }
    internal IOleClientSite* CallbackSite=>_sitePointer;
    private OleSession(int aspect)
    {
        if(aspect is not (1 or 2 or 4 or 8))throw new ArgumentOutOfRangeException(nameof(aspect));
        Aspect=aspect;
    }
    private void CheckAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        if(Environment.CurrentManagedThreadId!=_thread)throw new InvalidOperationException("OLE session must stay on its creating apartment thread.");
    }
    private void Attach()
    {
        _site=new(this);_sitePointer=NativeComWrappers.Create<IOleClientSite>(_site);
        _object->SetClientSite(_sitePointer);
        // Static OLE images support storage/drawing but reject advisory host names.
        try{fixed(char* app="Direct2dCad",name="CAD object")_object->SetHostNames(new PCWSTR(app),new PCWSTR(name));}
        catch(COMException ex)when(ex.HResult is unchecked((int)0x8004000B) or unchecked((int)0x80004001)){ }
        var sink=NativeComWrappers.Query<IAdviseSink>((IUnknown*)_sitePointer);
        try{fixed(uint* cookie=&_cookie)_object->Advise(sink,cookie);}
        catch(COMException ex)when(ex.HResult is unchecked((int)0x8004000B) or unchecked((int)0x80040003) or unchecked((int)0x80004001)){_cookie=0;}
        finally{sink->Release();}
    }
    public static OleSession Load(ReadOnlySpan<byte> storage,int aspect)
    {
        if(storage.Length is <=0 or >64*1024*1024)throw new ArgumentOutOfRangeException(nameof(storage));
        var session=new OleSession(aspect);
        var memory=PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE,(nuint)storage.Length);
        if(memory.IsNull)throw new OutOfMemoryException();
        try
        {
            var data=PInvoke.GlobalLock(memory);if(data is null)throw new OutOfMemoryException();
            try{storage.CopyTo(new Span<byte>(data,storage.Length));}finally{PInvoke.GlobalUnlock(memory);}
            fixed(ILockBytes** bytes=&session._bytes)PInvoke.CreateILockBytesOnHGlobal(memory,true,bytes).ThrowOnFailure();
            memory=default; // ILockBytes owns the HGLOBAL after successful creation.
            fixed(IStorage** target=&session._storage)PInvoke.StgOpenStorageOnILockBytes(session._bytes,null,STGM.STGM_READWRITE|STGM.STGM_SHARE_EXCLUSIVE,null,0,target).ThrowOnFailure();
            var iid=IOleObject.IID_Guid;
            fixed(IOleObject** target=&session._object)PInvoke.OleLoad(session._storage,&iid,null,(void**)target).ThrowOnFailure();
            session.Attach();return session;
        }
        catch{session.Dispose();throw;}
        finally{if(!memory.IsNull)PInvoke.GlobalFree(memory);}
    }
    private void CreateStorage()
    {
        fixed(ILockBytes** bytes=&_bytes)PInvoke.CreateILockBytesOnHGlobal(default,true,bytes).ThrowOnFailure();
        fixed(IStorage** storage=&_storage)PInvoke.StgCreateDocfileOnILockBytes(_bytes,STGM.STGM_CREATE|STGM.STGM_READWRITE|STGM.STGM_SHARE_EXCLUSIVE,0,storage).ThrowOnFailure();
    }
    public static OleSession? FromClipboard()
    {
        IDataObject* data=null;PInvoke.OleGetClipboard(&data).ThrowOnFailure();
        try
        {
            if(PInvoke.OleQueryCreateFromData(data).Value!=0)return null;
            var session=new OleSession(1);
            try
            {
                session.CreateStorage();var iid=IOleObject.IID_Guid;
                fixed(IOleObject** target=&session._object)PInvoke.OleCreateFromData(data,&iid,OLERENDER.OLERENDER_DRAW,null,null,session._storage,(void**)target).ThrowOnFailure();
                session.Attach();return session;
            }
            catch{session.Dispose();throw;}
        }
        finally{data->Release();}
    }
    public static OleSession CreateFixture()
    {
        var session=new OleSession(1);var data=NativeComWrappers.Create<IDataObject>(new OleFixtureData());
        try
        {
            session.CreateStorage();var iid=IOleObject.IID_Guid;var format=OleFixtureData.Format;
            fixed(IOleObject** target=&session._object)PInvoke.OleCreateStaticFromData(data,&iid,OLERENDER.OLERENDER_FORMAT,&format,null,session._storage,(void**)target).ThrowOnFailure();
            session.Attach();return session;
        }
        catch{session.Dispose();throw;}
        finally{data->Release();}
    }
    private void Save()
    {
        CheckAccess();if(_saving)return;_saving=true;
        IPersistStorage* persist=null;
        try
        {
            persist=NativeComWrappers.Query<IPersistStorage>((IUnknown*)_object);
            PInvoke.OleSave(persist,_storage,true).ThrowOnFailure();persist->SaveCompleted(null);_storage->Commit(0);
        }
        finally{if(persist is not null)persist->Release();_saving=false;}
    }
    public (byte[] Storage,double AspectRatio) Snapshot()
    {
        Save();STATSTG stat=default;_bytes->Stat(&stat,1);
        if(stat.cbSize is 0 or >64*1024*1024)throw new InvalidDataException("OLE storage exceeds its size budget.");
        var result=new byte[(int)stat.cbSize];uint read=0;
        fixed(byte* data=result)_bytes->ReadAt(0,data,(uint)result.Length,&read);
        if(read!=result.Length)throw new EndOfStreamException();
        double aspect=1;SIZE extent=default;
        try{_object->GetExtent((DVASPECT)Aspect,&extent);if(extent.cy!=0)aspect=(double)extent.cx/extent.cy;}catch(COMException){ }
        return(result,aspect);
    }
    public void BeginEdit(nint owner)
    {
        CheckAccess();PInvoke.OleRun((IUnknown*)_object).ThrowOnFailure();
        RECT rect=new(0,0,800,600);_object->DoVerb(-2,null,_sitePointer,0,new HWND(owner),&rect);
    }
    public byte[] Draw(int fullWidth,int fullHeight,int x,int y,int width,int height)
    {
        CheckAccess();
        if(width<=0||height<=0||fullWidth<width||fullHeight<height||x<0||y<0||x>fullWidth-width||y>fullHeight-height||(long)width*height>16*1024*1024)throw new ArgumentOutOfRangeException(nameof(width));
        var dc=PInvoke.CreateCompatibleDC(default);if(dc.IsNull)throw new System.ComponentModel.Win32Exception();
        HBITMAP bitmap=default;HGDIOBJ old=default;
        try
        {
            BITMAPINFO info=default;info.bmiHeader.biSize=(uint)sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=width;info.bmiHeader.biHeight=-height;info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;
            void* pixels=null;bitmap=PInvoke.CreateDIBSection(dc,&info,DIB_USAGE.DIB_RGB_COLORS,&pixels,default,0);
            if(bitmap.IsNull||pixels is null)throw new System.ComponentModel.Win32Exception();
            old=PInvoke.SelectObject(dc,bitmap);var count=checked(width*height*4);new Span<byte>(pixels,count).Fill(255);
            RECT rect=new(-x,-y,fullWidth-x,fullHeight-y);PInvoke.OleDraw((IUnknown*)_object,(uint)Aspect,dc,&rect).ThrowOnFailure();
            var result=new byte[count];new ReadOnlySpan<byte>(pixels,count).CopyTo(result);for(var i=3;i<count;i+=4)result[i]=255;return result;
        }
        finally{if(!old.IsNull)PInvoke.SelectObject(dc,old);if(!bitmap.IsNull)PInvoke.DeleteObject(bitmap);PInvoke.DeleteDC(dc);}
    }
    private void Notify(bool persisted){if(!_disposed&&!_saving)Changed?.Invoke(persisted);}
    public void Dispose()
    {
        if(_disposed)return;CheckAccess();_disposed=true;Changed=null;if(_site is not null)_site.Session=null;
        if(_object is not null)
        {
            try{_object->Close(1);}catch(COMException){ }
            if(_cookie!=0){try{_object->Unadvise(_cookie);}catch(COMException){ }}
            try{_object->SetClientSite(null);}catch(COMException){ }
            _object->Release();_object=null;
        }
        if(_sitePointer is not null){_sitePointer->Release();_sitePointer=null;}
        if(_storage is not null){_storage->Release();_storage=null;}
        if(_bytes is not null){_bytes->Release();_bytes=null;}
    }
    internal sealed class ClientSite(OleSession session):IOleClientSite.Interface,IAdviseSink.Interface
    {
        internal OleSession? Session=session;
        public HRESULT SaveObject(){if(Session is null)return (HRESULT)unchecked((int)0x8000FFFF);Session.Save();Session.Notify(true);return (HRESULT)0;}
        public HRESULT GetMoniker(uint assign,uint which,IMoniker** result){if(result is not null)*result=null;return (HRESULT)unchecked((int)0x80004001);}
        public HRESULT GetContainer(IOleContainer** result){if(result is not null)*result=null;return (HRESULT)unchecked((int)0x80004002);}
        public HRESULT ShowObject()=>(HRESULT)0;
        public HRESULT OnShowWindow(BOOL show)=>(HRESULT)0;
        public HRESULT RequestNewObjectLayout()=>(HRESULT)unchecked((int)0x80004001);
        public void OnDataChange(FORMATETC* format,STGMEDIUM* medium)=>Session?.Notify(false);
        public void OnViewChange(uint aspect,int index)=>Session?.Notify(false);
        public void OnRename(IMoniker* moniker){ }
        public void OnSave()=>Session?.Notify(true);
        public void OnClose()=>Session?.Notify(true);
    }
}
