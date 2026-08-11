using System.ComponentModel;
using Direct2dCad.Client.Common.Attributes;
using Direct2dCad.Client.Common.Converters;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Enums;

[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum ViewModelCadUnit
{
    [LocalizedDescription("Unitless", typeof(Strings))]
    Unitless,
    [LocalizedDescription("Millimeter", typeof(Strings))]
    Millimeter,
    [LocalizedDescription("Centimeter", typeof(Strings))]
    Centimeter,
    [LocalizedDescription("Meter", typeof(Strings))]
    Meter,
    [LocalizedDescription("Inch", typeof(Strings))]
    Inch,
    [LocalizedDescription("Foot", typeof(Strings))]
    Foot,
    [LocalizedDescription("Mil", typeof(Strings))]
    Mil
}
