using Direct2dCad.Db;

namespace Direct2dCad.ViewModels.Services.ViewServices;

public interface IOleImportService
{
    CadOleImportData? LoadFromClipboard();
    CadOleDrawData? DrawOleObject(Guid sessionId, CadOleDrawRequest request);
    void BeginEdit(Guid sessionId, EntityId entityId, byte[] oleBytes, string objectName);
    void EndEditSession(Guid sessionId, EntityId entityId);
    void EndEditSessions(Guid sessionId);
    void ReleaseRenderSession(Guid sessionId, EntityId entityId);
    void ReleaseTransientRenderSession(Guid sessionId, Guid renderId);
    void ReleaseRenderSessions(Guid sessionId);
}
