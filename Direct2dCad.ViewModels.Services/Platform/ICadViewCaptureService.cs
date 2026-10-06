using Direct2dCad.Application.Tools;
using Direct2dCad.Db.Cad;
using Direct2dCad.Rendering;

namespace Direct2dCad.ViewModels.Services.Platform;

public sealed record CadViewCaptureRequest(CadDocument Document, CadViewport Viewport, CadRenderOptions Options,
    int Width, int Height, CadOleRenderCallback? OleDrawCallback = null);

public interface ICadViewCaptureService
{
    Task<CadToolImage> CaptureAsync(CadViewCaptureRequest request, CancellationToken cancellationToken);
}
