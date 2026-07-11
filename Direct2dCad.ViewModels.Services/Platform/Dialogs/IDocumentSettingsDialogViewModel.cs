namespace Direct2dCad.ViewModels.Services.Platform;

public interface IDocumentSettingsDialogViewModel
{
    string? ValidationError { get; }

    bool TryApply();

    void ResetToDefaults();
}
