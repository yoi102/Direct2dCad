using Direct2dCad.ChangeTracking;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Editor;
using Direct2dCad.Rendering.Direct2D.Ole;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Platform;
using MessagePipe;

namespace Direct2dCad.ViewModels.Services.Interactions;

public sealed class CadOleSessionController : IDisposable
{
    private readonly IOleHostService _oleHostService;
    private readonly Action<EntityId> _invalidateView;
    private readonly IDisposable _subscription;
    private Guid _oleEditSessionId = Guid.NewGuid();
    private readonly HashSet<EntityId> _openOleEditEntityIds = [];
    private CadEditor _editor;
    private bool _isApplyingOleHostUpdate;
    private bool _disposed;
    private bool _isClearingSessions;

    public CadOleSessionController(CadEditor editor, IOleHostService host,
        ISubscriber<CadOleObjectUpdatedMessage> updates, Action<EntityId> invalidateView)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _oleHostService = host ?? throw new ArgumentNullException(nameof(host));
        _invalidateView = invalidateView ?? throw new ArgumentNullException(nameof(invalidateView));
        _subscription = (updates ?? throw new ArgumentNullException(nameof(updates))).Subscribe(OnOleObjectUpdated);
        _editor.DocumentCommands.DocumentChanged += OnDocumentChanged;
    }

    public CadOleImportData? LoadFromClipboard() => _oleHostService.LoadFromClipboard();

    public void BeginEdit(CadOleObject entity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CadEntityAccessPolicy.IsEditable(_editor.Document, entity))
            return;
        _openOleEditEntityIds.Add(entity.Id);
        try
        {
            _oleHostService.BeginEdit(_oleEditSessionId, entity.Id, entity.CopyOleBytes(),
                string.IsNullOrWhiteSpace(entity.Name) ? entity.SourceName : entity.Name);
        }
        catch
        {
            _openOleEditEntityIds.Remove(entity.Id);
            throw;
        }
    }

    public void ReplaceEditor(CadEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ClearSessions();
        _editor.DocumentCommands.DocumentChanged -= OnDocumentChanged;
        _editor = editor;
        _oleEditSessionId = Guid.NewGuid();
        _editor.DocumentCommands.DocumentChanged += OnDocumentChanged;
    }

    private void OnDocumentChanged(object? sender, CadDocumentChangeSet changes)
    {
        CloseStaleOleEditSessions();
        CloseReplacedOleEditSessions(changes);
        ReleaseChangedOleRenderSessions(changes);
    }

    private void OnOleObjectUpdated(CadOleObjectUpdatedMessage message)
    {
        if (_disposed || _isClearingSessions || message.SessionId != _oleEditSessionId ||
            (message.IsPersisted && !_openOleEditEntityIds.Contains(message.EntityId)) ||
            !_editor.Document.TryGetEntity(message.EntityId, out var entity) ||
            entity is not CadOleObject oleObject ||
            !CadEntityAccessPolicy.IsEditable(_editor.Document, oleObject))
        {
            return;
        }

        if (!message.IsPersisted)
        {
            _invalidateView(message.EntityId);
            return;
        }

        if (message.Data is null || !HasOleDataChanged(oleObject, message.Data))
            return;

        // Storage changes are a document command; view-only changes are redrawn from the active OLE session.
        _isApplyingOleHostUpdate = true;
        try
        {
            _editor.SetOleObjectData(
                message.EntityId,
                message.Data.OleBytes,
                message.Data.ContentType,
                message.Data.SourceName);
        }
        finally
        {
            _isApplyingOleHostUpdate = false;
        }
    }

    public Direct2DOleDrawData? Draw(Direct2DOleDrawRequest request)
    {
        if (_disposed)
            return null;
        var drawData = _oleHostService.DrawOleObject(
            _oleEditSessionId,
            new CadOleDrawRequest(
                request.RenderKey.EntityId,
                request.RenderKey.RenderId,
                request.OleBytes,
                request.FullPixelWidth,
                request.FullPixelHeight,
                request.RegionX,
                request.RegionY,
                request.PixelWidth,
                request.PixelHeight));

        return drawData is null
            ? null
            : new Direct2DOleDrawData(
                drawData.PixelWidth,
                drawData.PixelHeight,
                drawData.Stride,
                drawData.Pixels);
    }

    private static bool HasOleDataChanged(CadOleObject oleObject, CadOleImportData updated)
    {
        return !oleObject.OleMemory.Span.SequenceEqual(updated.OleBytes) ||
               !string.Equals(oleObject.ContentType, updated.ContentType, StringComparison.Ordinal) ||
               !string.Equals(oleObject.SourceName, updated.SourceName, StringComparison.Ordinal);
    }

    public void Release(Direct2DOleRenderKey renderKey)
    {
        if (_disposed)
            return;
        if (renderKey.EntityId is { } entityId)
            _oleHostService.ReleaseRenderSession(_oleEditSessionId, entityId);
        else
            _oleHostService.ReleaseTransientRenderSession(_oleEditSessionId, renderKey.RenderId);
    }

    private void CloseReplacedOleEditSessions(CadDocumentChangeSet changes)
    {
        if (_isApplyingOleHostUpdate || _openOleEditEntityIds.Count == 0)
            return;

        foreach (var change in changes.EntityChanges)
        {
            if ((change.Kind & CadEntityChangeKind.EmbeddedData) == 0 ||
                !_openOleEditEntityIds.Remove(change.EntityId))
            {
                continue;
            }

            _oleHostService.EndEditSession(_oleEditSessionId, change.EntityId);
        }
    }

    private void ReleaseChangedOleRenderSessions(CadDocumentChangeSet changes)
    {
        foreach (var change in changes.EntityChanges)
        {
            if (!ShouldReleaseOleRenderSession(change))
                continue;

            _oleHostService.ReleaseRenderSession(_oleEditSessionId, change.EntityId);
        }
    }

    private bool ShouldReleaseOleRenderSession(CadEntityChange change)
    {
        if ((change.Kind & CadEntityChangeKind.Deleted) != 0)
            return true;

        if ((change.Kind & CadEntityChangeKind.Appearance) == 0)
            return false;

        return _editor.Document.TryGetEntity(change.EntityId, out var entity) &&
               entity is CadOleObject;
    }

    private void CloseStaleOleEditSessions()
    {
        if (_openOleEditEntityIds.Count == 0)
            return;

        foreach (var entityId in _openOleEditEntityIds.ToArray())
        {
            if (_editor.Document.TryGetEntity(entityId, out var entity) &&
                entity is CadOleObject &&
                CadEntityAccessPolicy.IsEditable(_editor.Document, entity))
            {
                continue;
            }

            _openOleEditEntityIds.Remove(entityId);
            _oleHostService.EndEditSession(_oleEditSessionId, entityId);
        }
    }

    private void ClearSessions()
    {
        // Remove ownership first: closing an OLE server may synchronously publish a save.
        _isClearingSessions = true;
        _openOleEditEntityIds.Clear();
        try
        {
            try { _oleHostService.EndEditSessions(_oleEditSessionId); }
            finally { _oleHostService.ReleaseRenderSessions(_oleEditSessionId); }
        }
        finally { _isClearingSessions = false; }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _editor.DocumentCommands.DocumentChanged -= OnDocumentChanged;
        _subscription.Dispose();
        ClearSessions();
    }
}
