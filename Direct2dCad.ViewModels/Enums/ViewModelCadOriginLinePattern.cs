using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Direct2dCad.Client.Common.Attributes;
using Direct2dCad.Client.Common.Converters;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Enums;


[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum ViewModelCadOriginLinePattern
{
    [LocalizedDescription("Solid", typeof(Strings))]
    Solid,
    [LocalizedDescription("Dash", typeof(Strings))]
    Dash,
    [LocalizedDescription("Dot", typeof(Strings))]
    Dot,
    [LocalizedDescription("DashDot", typeof(Strings))]
    DashDot
}
