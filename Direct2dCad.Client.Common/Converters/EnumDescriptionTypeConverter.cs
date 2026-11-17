using System.ComponentModel;
using System.Reflection;

namespace Direct2dCad.Client.Common.Converters;

/// <summary>Displays an enum value by its <see cref="DescriptionAttribute"/> (including localized descriptions).</summary>
public class EnumDescriptionTypeConverter(Type type) : EnumConverter(type)
{
    public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType)
    {
        // Only the display string is customized; every other conversion keeps EnumConverter semantics.
        if (destinationType != typeof(string))
            return base.ConvertTo(context, culture, value, destinationType);

        if (value is null)
            return string.Empty;

        var valueString = value.ToString();
        if (valueString is null)
            return string.Empty;

        var field = value.GetType().GetField(valueString);
        if (field is not null)
        {
            var attribute = field.GetCustomAttribute<DescriptionAttribute>(false);
            return !string.IsNullOrEmpty(attribute?.Description) ? attribute.Description : valueString;
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
