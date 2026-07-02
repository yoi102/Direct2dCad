using System.ComponentModel;
using Direct2dCad.Client.Common.Attributes;
using Direct2dCad.Client.Common.Converters;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Enums;

[TypeConverter(typeof(EnumDescriptionTypeConverter))]
public enum ViewModelCadShapeFont
{
    [LocalizedDescription("Unicode", typeof(Strings))]
    Unicode,
    [LocalizedDescription("Simplex", typeof(Strings))]
    Simplex,
    [LocalizedDescription("MonoLine", typeof(Strings))]
    MonoLine,
    [LocalizedDescription("BoxFallback", typeof(Strings))]
    BoxFallback
}
