using Direct2dCad.Client.Common.Settings;

namespace Direct2dCad.ViewModels.Services.Platform;

public interface IUserSettingsStore
{
    CadUserSettings Load();
    void Save(CadUserSettings settings);
}
