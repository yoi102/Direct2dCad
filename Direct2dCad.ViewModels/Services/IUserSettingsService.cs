using Direct2dCad.Client.Common.Settings;

namespace Direct2dCad.ViewModels.Services;

public interface IUserSettingsService
{
    CadUserSettings Load();
    void Save(CadUserSettings settings);
}
