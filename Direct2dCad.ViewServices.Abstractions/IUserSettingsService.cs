using Direct2dCad.Client.Common.Settings;

namespace Direct2dCad.ViewServices.Abstractions;

public interface IUserSettingsService
{
    CadUserSettings Load();
    void Save(CadUserSettings settings);
}
