namespace Direct2dCad.ViewModels.Services.ViewServices;

public interface ISnackbarService
{
    void Enqueue(object content, TimeSpan? durationOverride = null, bool promote = false, bool neverConsiderToBeDuplicate = false);
    void EnqueueInAll(object content, TimeSpan? durationOverride = null, bool promote = false, bool neverConsiderToBeDuplicate = false);
    void Enqueue(object identifier, object content, TimeSpan? durationOverride = null, bool promote = false, bool neverConsiderToBeDuplicate = false);
}
