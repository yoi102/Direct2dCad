using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Direct2dCad.Db;
using Direct2dCad.Ole.Windows;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using MessagePipe;
using OleDrawData = Direct2dCad.Ole.Windows.CadOleDrawData;
using ViewOleDrawData = Direct2dCad.ViewModels.Services.Platform.CadOleDrawData;

namespace Direct2dCad.wpf.Services.Ole;

internal sealed class OleHostService : IOleHostService, IDisposable
{
    private readonly IPublisher<CadOleObjectUpdatedMessage> _updatedPublisher;
    private readonly Dictionary<(Guid SessionId, EntityId EntityId), CadOleServices.CadOleEditSession> _editSessions = [];
    private readonly Dictionary<RenderSessionKey, CadOleServices.CadOleRenderSession> _renderSessions = [];

    public OleHostService(IPublisher<CadOleObjectUpdatedMessage> updatedPublisher)
    {
        _updatedPublisher = updatedPublisher;
    }

    public CadOleImportData? LoadFromClipboard()
    {
        var data = CadOleServices.TryCreateFromClipboard();
        return data is null
            ? null
            : new CadOleImportData(
                data.OleBytes,
                data.ContentType,
                data.SourceName,
                data.NaturalAspectRatio);
    }

    public void BeginEdit(
        Guid sessionId,
        EntityId entityId,
        byte[] oleBytes,
        string objectName)
    {
        var key = (sessionId, entityId);
        if (_editSessions.Remove(key, out var priorSession))
            priorSession.Dispose();

        var hwnd = System.Windows.Application.Current?.MainWindow is { } window
            ? new WindowInteropHelper(window).Handle
            : IntPtr.Zero;

        _editSessions[key] = CadOleServices.BeginEdit(
            oleBytes,
            hwnd,
            objectName,
            "Direct2dCad",
            (data, isPersisted) => PublishUpdatedObject(sessionId, entityId, data, isPersisted));
    }

    public ViewOleDrawData? DrawOleObject(
        Guid sessionId,
        CadOleDrawRequest request)
    {
        if (request.EntityId is { } persistedEntityId &&
            _editSessions.TryGetValue((sessionId, persistedEntityId), out var session))
        {
            return ToDrawData(session.DrawRegion(
                request.FullPixelWidth,
                request.FullPixelHeight,
                request.RegionX,
                request.RegionY,
                request.PixelWidth,
                request.PixelHeight));
        }

        var key = new RenderSessionKey(sessionId, request.EntityId, request.RenderId);
        if (!_renderSessions.TryGetValue(key, out var renderSession))
        {
            renderSession = CadOleServices.CreateRenderSession(GetArray(request.OleBytes));
            _renderSessions[key] = renderSession;
        }

        return ToDrawData(renderSession.DrawRegion(
            request.FullPixelWidth,
            request.FullPixelHeight,
            request.RegionX,
            request.RegionY,
            request.PixelWidth,
            request.PixelHeight));
    }

    public void EndEditSession(Guid sessionId, EntityId entityId)
    {
        var key = (sessionId, entityId);
        if (_editSessions.Remove(key, out var session))
            session.Dispose();
    }

    public void EndEditSessions(Guid sessionId)
    {
        foreach (var (key, session) in _editSessions.Where(pair => pair.Key.SessionId == sessionId).ToArray())
        {
            _editSessions.Remove(key);
            session.Dispose();
        }
    }

    public void ReleaseRenderSession(Guid sessionId, EntityId entityId)
    {
        var key = new RenderSessionKey(sessionId, entityId, Guid.Empty);
        if (_renderSessions.Remove(key, out var session))
            session.Dispose();
    }

    public void ReleaseTransientRenderSession(Guid sessionId, Guid renderId)
    {
        var key = new RenderSessionKey(sessionId, null, renderId);
        if (_renderSessions.Remove(key, out var session))
            session.Dispose();
    }

    public void ReleaseRenderSessions(Guid sessionId)
    {
        foreach (var (key, session) in _renderSessions.Where(pair => pair.Key.SessionId == sessionId).ToArray())
        {
            _renderSessions.Remove(key);
            session.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var session in _editSessions.Values)
            session.Dispose();

        _editSessions.Clear();

        foreach (var session in _renderSessions.Values)
            session.Dispose();

        _renderSessions.Clear();
    }

    private void PublishUpdatedObject(
        Guid sessionId,
        EntityId entityId,
        CadOleClipboardData? data,
        bool isPersisted)
    {
        var updated = ToImportData(data);
        _ = System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            _updatedPublisher.Publish(new CadOleObjectUpdatedMessage(sessionId, entityId, updated, isPersisted)));
    }

    private static CadOleImportData? ToImportData(CadOleClipboardData? data)
    {
        return data is null
            ? null
            : new CadOleImportData(
                data.OleBytes,
                data.ContentType,
                data.SourceName,
                data.NaturalAspectRatio);
    }

    private static ViewOleDrawData? ToDrawData(OleDrawData? data)
    {
        return data is null
            ? null
            : new ViewOleDrawData(
                data.PixelWidth,
                data.PixelHeight,
                data.Stride,
                data.Pixels);
    }

    private static byte[] GetArray(ReadOnlyMemory<byte> memory)
    {
        if (MemoryMarshal.TryGetArray(memory, out var segment) &&
            segment.Array is not null &&
            segment.Offset == 0 &&
            segment.Count == segment.Array.Length)
        {
            return segment.Array;
        }

        return memory.ToArray();
    }

    private readonly record struct RenderSessionKey(
        Guid SessionId,
        EntityId? EntityId,
        Guid RenderId);
}
