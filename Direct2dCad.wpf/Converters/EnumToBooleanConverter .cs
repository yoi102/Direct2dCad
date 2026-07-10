using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows.Data;

namespace Direct2dCad.wpf.Converters;

internal class EnumToBooleanConverter : IValueConverter
{
    public static EnumToBooleanConverter Instance = new ();
    public static EnumToBooleanConverter InverseInstance = new();


    /// <summary>
    /// Enum -> bool
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        var enumType = value.GetType();

        // Nullable<Enum> 对应处理
        enumType = Nullable.GetUnderlyingType(enumType) ?? enumType;

        if (!enumType.IsEnum)
            return false;

        object parameterValue;

        if (parameter is string parameterString)
        {
            parameterValue = Enum.Parse(enumType, parameterString);
        }
        else
        {
            parameterValue = parameter;
        }

        return value.Equals(parameterValue);
    }

    /// <summary>
    /// bool -> Enum
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter == null)
            return Binding.DoNothing;

        if (value is not bool isChecked || !isChecked)
            return Binding.DoNothing;

        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (!enumType.IsEnum)
            return Binding.DoNothing;

        if (parameter is string parameterString)
        {
            return Enum.Parse(enumType, parameterString);
        }

        return parameter;
    }
}
