using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Direct2dCad.Client.Common.Attributes;
using Direct2dCad.Client.Common.Converters;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Enums;


[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum ViewModelCadGridType
{
    [LocalizedDescription("None", typeof(Strings))]
    None,
    [LocalizedDescription("Dots", typeof(Strings))]
    Dots,
    [LocalizedDescription("Lines", typeof(Strings))]
    Lines,
    [LocalizedDescription("Cross", typeof(Strings))]
    Cross
}
