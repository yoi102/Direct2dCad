namespace Direct2dCad.ViewModels.Services.ViewServices;

public interface IUserSettingsDialogViewModel
{
    string? ValidationError { get; }

    bool TryApply();
}
