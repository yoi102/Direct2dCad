using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.ViewModels.Services.Platform;

namespace Direct2dCad.ViewModels.Settings.UserSettings;

public partial class UserSettingsViewModel : ObservableObject, IUserSettingsDialogViewModel
{
    private readonly IUserSettingsStore _settingsStore;
    private readonly Action<CadUserSettings> _applySettings;

    public UserSettingsViewModel(
        CadUserSettings settings,
        IUserSettingsStore settingsStore,
        Action<CadUserSettings> applySettings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _applySettings = applySettings ?? throw new ArgumentNullException(nameof(applySettings));

        var workingCopy = settings.Clone();
        General = new GeneralUserSettingsViewModel(workingCopy.General);
        Rendering = new RenderingUserSettingsViewModel(workingCopy.Rendering);
        Interaction = new InteractionUserSettingsViewModel(workingCopy.Interaction);
        Sections = [General, Rendering, Interaction];
        SelectedSection = Sections[0];
    }

    public GeneralUserSettingsViewModel General { get; }
    public RenderingUserSettingsViewModel Rendering { get; }
    public InteractionUserSettingsViewModel Interaction { get; }
    public IReadOnlyList<UserSettingsSectionViewModel> Sections { get; }

    [ObservableProperty] public partial UserSettingsSectionViewModel SelectedSection { get; set; }
    [ObservableProperty] public partial string? ValidationError { get; private set; }

    public bool TryApply()
    {
        var settings = CadUserSettings.CreateDefault();
        foreach (var section in Sections)
        {
            if (section.TryApplyTo(settings))
                continue;

            SelectedSection = section;
            ValidationError = Direct2dCad.Lang.Strings.Strings.ResourceManager.GetString(
                "UserSettingsInvalidValues",
                System.Globalization.CultureInfo.CurrentUICulture);
            return false;
        }

        settings.Normalize();
        try
        {
            _settingsStore.Save(settings);
            _applySettings(settings);
            ValidationError = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ValidationError = ex.Message;
            return false;
        }
    }

    public void ResetToDefaults()
    {
        foreach (var section in Sections)
            section.ResetToDefaults();

        ValidationError = null;
    }
}
