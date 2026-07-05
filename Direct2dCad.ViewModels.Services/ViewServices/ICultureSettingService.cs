namespace Direct2dCad.ViewModels.Services.ViewServices;

public interface ICultureSettingService
{
    void ChangeCulture(string language);

    void ChangeCulture(int lcid);

    int GetCurrentCultureLCID();
}
