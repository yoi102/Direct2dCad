using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Direct2dCad.Client.Common.Attributes;
using Direct2dCad.Client.Common.Converters;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Enums;

[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum ViewModelCadSnapMarkerType
{
    [LocalizedDescription("None", typeof(Strings))]
    None = 0,
    [LocalizedDescription("Cross", typeof(Strings))]
    Cross = 1,
    [LocalizedDescription("X", typeof(Strings))]
    X = 2,
    [LocalizedDescription("Square", typeof(Strings))]
    Square = 3,
    [LocalizedDescription("InfiniteCross", typeof(Strings))]
    InfiniteCross = 4
}
