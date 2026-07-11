namespace Direct2dCad.ViewModels.Services.Platform;

public interface IUserSettingsDialogViewModel
{
    string? ValidationError { get; }

    bool TryApply();

    void ResetToDefaults();
}
