namespace Direct2dCad.ViewModels.Services.Platform;

using Direct2dCad.ViewModels.Services.Platform.Notifications;

public interface ISnackbarService
{
    void Enqueue(
        object content,
        TimeSpan? durationOverride = null,
        bool promote = false,
        bool neverConsiderToBeDuplicate = false,
        CadMessageLevel level = CadMessageLevel.Information);

    void EnqueueInAll(
        object content,
        TimeSpan? durationOverride = null,
        bool promote = false,
        bool neverConsiderToBeDuplicate = false,
        CadMessageLevel level = CadMessageLevel.Information);

    void Enqueue(
        object identifier,
        object content,
        TimeSpan? durationOverride = null,
        bool promote = false,
        bool neverConsiderToBeDuplicate = false,
        CadMessageLevel level = CadMessageLevel.Information);
}
