namespace Direct2dCad.ViewModels.Services.ViewServices;

public interface IDocumentSettingsDialogViewModel
{
    string? ValidationError { get; }

    bool TryApply();

    void ResetToDefaults();
}
