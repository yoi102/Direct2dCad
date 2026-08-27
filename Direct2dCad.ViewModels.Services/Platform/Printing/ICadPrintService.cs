using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Ole;

namespace Direct2dCad.ViewModels.Services.Platform.Printing;

public interface ICadPrintService
{
    Task<bool> PrintAsync(
        CadPrintRequest request,
        Action? onPrintStarted = null,
        Action<bool>? onBusyChanged = null,
        Action? onPrintCompleted = null);
}

public sealed record CadPrintRequest(
    string DocumentName,
    CadDocument Document,
    CadRectD PaperBounds,
    CadRenderOptions RenderOptions,
    Direct2DOleDrawCallback? OleDrawCallback = null);
