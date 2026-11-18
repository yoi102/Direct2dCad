namespace Direct2dCad.ViewModels;

public partial class MainViewModel
{
    /// <summary>How often open drawings write recovery copies.</summary>
    public static TimeSpan RecoveryCaptureInterval { get; } = TimeSpan.FromSeconds(60);

    private CadApplicationExit? _applicationExit;

    /// <summary>The window-close protocol; hosts call <see cref="CadApplicationExit.OnClosing"/> from Closing.</summary>
    public CadApplicationExit ApplicationExit => _applicationExit ??= new(ConfirmApplicationExitAsync, ShutdownServicesAsync);

    /// <summary>Always confirms exiting first, then resolves unsaved drawings. True when the application may exit.</summary>
    public async Task<bool> ConfirmApplicationExitAsync()
    {
        if (!await _dialogService.ShowExitConfirmation())
            return false;
        return await ConfirmCloseApplicationAsync();
    }

    /// <summary>The periodic recovery-copy writer; the host starts it once its window is up and disposes it on close.</summary>
    public CadPeriodicWork CreateRecoveryCapture(Action<Exception> onError) =>
        new(RecoveryCaptureInterval, CaptureRecoveryCopiesAsync, onError);
}
