namespace Direct2dCad.ViewModels.Services.Platform;

public interface IApplicationCultureService
{
    void ChangeCulture(string language);

    void ChangeCulture(int lcid);

    int GetCurrentCultureLCID();
}
