using global::Avalonia.Threading;
using Direct2dCad.Db;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.Windows.Interop;
using MessagePipe;

namespace Direct2dCad.Avalonia.Services;

internal sealed class OleHostService : IOleHostService, IDisposable
{
    private readonly IPublisher<CadOleObjectUpdatedMessage> _publisher;
    private readonly OleApartment _apartment=new();
    private readonly Dictionary<(Guid Session, EntityId? Entity, Guid Render), OleSession> _sessions=[];
    private readonly Dictionary<(Guid Session, EntityId Entity), OleSession> _editors=[];
    private readonly Dictionary<OleSession,string> _names=[];
    private bool _disposed;
    public OleHostService(IPublisher<CadOleObjectUpdatedMessage> publisher)=>_publisher=publisher;
    private CadOleImportData Snapshot(OleSession native)
    {
        var (storage,aspect)=native.Snapshot();var name=_names.GetValueOrDefault(native,"OLE Object");
        return new(new OlePayload(storage,native.Aspect,name).Encode(),"application/x-ole-storage",name,aspect);
    }
    public CadOleImportData? LoadFromClipboard()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        using var native=OleSession.FromClipboard();return native is null?null:Snapshot(native);
    }
    private OleSession Load(ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        var payload=OlePayload.Decode(bytes);var native=OleSession.Load(payload.Storage,payload.Aspect);_names[native]=payload.Name;return native;
    }
    public CadOleDrawData? DrawOleObject(Guid sessionId,CadOleDrawRequest request)
    {
        OleSession native;
        if(request.EntityId is { } entity&&_editors.TryGetValue((sessionId,entity),out var editing))native=editing;
        else
        {
            var key=(sessionId,request.EntityId,request.RenderId);
            if(!_sessions.TryGetValue(key,out native!))_sessions[key]=native=Load(request.OleBytes.Span);
        }
        var pixels=native.Draw(request.FullPixelWidth,request.FullPixelHeight,request.RegionX,request.RegionY,request.PixelWidth,request.PixelHeight);
        return new(request.PixelWidth,request.PixelHeight,checked(request.PixelWidth*4),pixels);
    }
    public void BeginEdit(Guid sessionId,EntityId entityId,byte[] oleBytes,string name)
    {
        EndEditSession(sessionId,entityId);var native=Load(oleBytes);_editors[(sessionId,entityId)]=native;
        native.Changed=persisted=>Dispatcher.UIThread.Post(() =>
        {
            if(_disposed||!_editors.TryGetValue((sessionId,entityId),out var current)||!ReferenceEquals(current,native))return;
            try{_publisher.Publish(new(sessionId,entityId,persisted?Snapshot(native):null,persisted));}
            catch(Exception ex){App.Window?.ShowNotification(ex.Message);}
        });
        try{native.BeginEdit(App.OwnerHandle);}catch{EndEditSession(sessionId,entityId);throw;}
    }
    private void CloseSession(OleSession native){_names.Remove(native);native.Dispose();}
    public void EndEditSession(Guid sessionId,EntityId entityId){if(_editors.Remove((sessionId,entityId),out var native))CloseSession(native);}
    public void EndEditSessions(Guid sessionId){foreach(var key in _editors.Keys.Where(key=>key.Session==sessionId).ToArray())EndEditSession(key.Session,key.Entity);}
    public void ReleaseRenderSession(Guid sessionId,EntityId entityId){foreach(var key in _sessions.Keys.Where(key=>key.Session==sessionId&&key.Entity==entityId).ToArray()){CloseSession(_sessions[key]);_sessions.Remove(key);}}
    public void ReleaseTransientRenderSession(Guid sessionId,Guid renderId){foreach(var key in _sessions.Keys.Where(key=>key.Session==sessionId&&key.Entity is null&&key.Render==renderId).ToArray()){CloseSession(_sessions[key]);_sessions.Remove(key);}}
    public void ReleaseRenderSessions(Guid sessionId){foreach(var key in _sessions.Keys.Where(key=>key.Session==sessionId).ToArray()){CloseSession(_sessions[key]);_sessions.Remove(key);}}
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;
        try{foreach(var key in _editors.Keys.ToArray())EndEditSession(key.Session,key.Entity);foreach(var native in _sessions.Values)CloseSession(native);_sessions.Clear();}
        finally{_apartment.Dispose();}
    }
    internal CadOleImportData CreateSmokeFixture(){using var native=OleSession.CreateFixture();return Snapshot(native);}
}
