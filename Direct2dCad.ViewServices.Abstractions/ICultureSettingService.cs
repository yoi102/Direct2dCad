using System;
using System.Collections.Generic;
using System.Text;

namespace Direct2dCad.ViewServices.Abstractions;

public interface ICultureSettingService
{
    void ChangeCulture(string language);

    void ChangeCulture(int lcid);
}
