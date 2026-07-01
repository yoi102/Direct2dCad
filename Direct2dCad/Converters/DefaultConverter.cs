using System.Globalization;
using System.Windows.Data;

namespace Direct2dCad.wpf.Converters;

internal class DefaultConverter<T>(T defaultValue, T nonDefaultValue) : IValueConverter
{
    public T DefaultValue { get; set; } = defaultValue;
    public T NonDefaultValue { get; set; } = nonDefaultValue;

    public virtual object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value == default ? DefaultValue : NonDefaultValue;

    public virtual object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
