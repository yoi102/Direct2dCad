using Direct2dCad.Db;

namespace Direct2dCad.ViewModels.Services.ViewServices;

public interface IOleImportService
{
    CadOleImportData? LoadFromClipboard();
    CadOleImportData? CreatePreview(byte[] oleBytes, int maxPreviewPixelSide);
    CadOleDrawData? DrawOleObject(Guid sessionId, EntityId entityId, byte[] oleBytes, int pixelWidth, int pixelHeight);
    void BeginEdit(Guid sessionId, EntityId entityId, byte[] oleBytes, string objectName, int maxPreviewPixelSide);
    void EndEditSession(Guid sessionId, EntityId entityId);
    void EndEditSessions(Guid sessionId);
    void ReleaseRenderSession(Guid sessionId, EntityId entityId);
    void ReleaseRenderSessions(Guid sessionId);
}
