using System.Windows;
using System.Windows.Interop;
using Direct2dCad.Ole.Windows;
using Direct2dCad.Db;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.ViewServices;
using MessagePipe;
using OleDrawData = Direct2dCad.Ole.Windows.CadOleDrawData;
using ViewOleDrawData = Direct2dCad.ViewModels.Services.ViewServices.CadOleDrawData;

namespace Direct2dCad.wpf.Services;

internal sealed class OleImportService : IOleImportService, IDisposable
{
    private readonly IPublisher<CadOleObjectUpdatedMessage> _updatedPublisher;
    private readonly Dictionary<(Guid SessionId, EntityId EntityId), CadOleServices.CadOleEditSession> _editSessions = [];
    private readonly Dictionary<(Guid SessionId, EntityId EntityId), CadOleServices.CadOleRenderSession> _renderSessions = [];

    public OleImportService(IPublisher<CadOleObjectUpdatedMessage> updatedPublisher)
    {
        _updatedPublisher = updatedPublisher;
    }

    public CadOleImportData? LoadFromClipboard()
    {
        var data = CadOleServices.TryCreateFromClipboard();
        return data is null
            ? null
            : new CadOleImportData(
                data.PixelWidth,
                data.PixelHeight,
                data.Stride,
                data.Pixels,
                data.OleBytes,
                data.ContentType,
                data.SourceName);
    }

    public void BeginEdit(
        Guid sessionId,
        EntityId entityId,
        byte[] oleBytes,
        string objectName,
        int maxPreviewPixelSide)
    {
        var key = (sessionId, entityId);
        if (_editSessions.Remove(key, out var priorSession))
            priorSession.Dispose();

        var hwnd = Application.Current?.MainWindow is { } window
            ? new WindowInteropHelper(window).Handle
            : IntPtr.Zero;

        _editSessions[key] = CadOleServices.BeginEdit(
            oleBytes,
            hwnd,
            objectName,
            "Direct2dCad",
            maxPreviewPixelSide,
            (data, isPersisted) => PublishUpdatedPreview(sessionId, entityId, data, isPersisted));
    }

    public CadOleImportData? CreatePreview(byte[] oleBytes, int maxPreviewPixelSide)
    {
        return ToImportData(CadOleServices.CreatePreview(oleBytes, maxPreviewPixelSide));
    }

    public ViewOleDrawData? DrawOleObject(
        Guid sessionId,
        EntityId entityId,
        byte[] oleBytes,
        int pixelWidth,
        int pixelHeight)
    {
        if (_editSessions.TryGetValue((sessionId, entityId), out var session))
            return ToDrawData(session.Draw(pixelWidth, pixelHeight));

        var key = (sessionId, entityId);
        if (!_renderSessions.TryGetValue(key, out var renderSession))
        {
            renderSession = CadOleServices.CreateRenderSession(oleBytes);
            _renderSessions[key] = renderSession;
        }

        return ToDrawData(renderSession.Draw(pixelWidth, pixelHeight));
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
        var key = (sessionId, entityId);
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

    private void PublishUpdatedPreview(Guid sessionId, EntityId entityId, CadOleClipboardData data, bool isPersisted)
    {
        var updated = ToImportData(data);
        if (updated is null)
            return;

        _ = Application.Current?.Dispatcher.BeginInvoke(() =>
            _updatedPublisher.Publish(new CadOleObjectUpdatedMessage(sessionId, entityId, updated, isPersisted)));
    }

    private static CadOleImportData? ToImportData(CadOleClipboardData? data)
    {
        return data is null
            ? null
            : new CadOleImportData(
                data.PixelWidth,
                data.PixelHeight,
                data.Stride,
                data.Pixels,
                data.OleBytes,
                data.ContentType,
                data.SourceName);
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
}
