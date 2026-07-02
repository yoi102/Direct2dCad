using System;
using System.Collections.Generic;
using System.Text;

namespace Direct2dCad.ViewModels.Services;

public interface ICultureSettingService
{
    void ChangeCulture(string language);

    void ChangeCulture(int lcid);

    int GetCurrentCultureLCID();
}
