using Direct2dCad.Client.Common.Settings;

namespace Direct2dCad.ViewModels.Services.Platform;

public interface IToolboxLayoutSettingsStore
{
    CadToolboxState? Load(string contentId);

    void Save(IEnumerable<KeyValuePair<string, CadToolboxState>> toolboxes);
}
