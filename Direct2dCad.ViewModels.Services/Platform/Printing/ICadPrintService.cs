using Direct2dCad.Rendering;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels.Services.Platform.Printing;

public interface ICadPrintService
{
    Task<bool> PrintAsync(
        CadPrintRequest request,
        Action? onPrintStarted = null,
        Action<bool>? onBusyChanged = null,
        Action? onPrintCompleted = null,
        Action<CadPrintCompletion>? onPrintFinished = null,
        CancellationToken cancellationToken = default);
}

public enum CadPrintCompletionStatus { Completed, Cancelled, Failed }

public sealed record CadPrintCompletion(CadPrintCompletionStatus Status, string? Error = null)
{
    public static async Task<CadPrintCompletion> ObserveAsync(Task writingCompletion)
    {
        try { await writingCompletion.ConfigureAwait(false); return new(CadPrintCompletionStatus.Completed); }
        catch (OperationCanceledException) { return new(CadPrintCompletionStatus.Cancelled); }
        catch (Exception ex) { return new(CadPrintCompletionStatus.Failed, ex.Message); }
    }
}

public sealed record CadPrintRequest(
    string DocumentName,
    CadDocument Document,
    CadRectD PaperBounds,
    LayoutId ActiveLayoutId,
    CadOleRenderCallback? OleDrawCallback = null)
{
    public bool IsModelSpace { get; init; }
    public CadRectD? CurrentViewBounds { get; init; }
}
