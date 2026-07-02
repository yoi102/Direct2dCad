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
    None,
    [LocalizedDescription("Cross", typeof(Strings))]
    Cross,
    [LocalizedDescription("X", typeof(Strings))]
    X,
    [LocalizedDescription("Square", typeof(Strings))]
    Square
}
