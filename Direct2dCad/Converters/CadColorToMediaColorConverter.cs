using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.wpf.Converters;

internal sealed class CadColorToMediaColorConverter : IValueConverter
{
    public static readonly CadColorToMediaColorConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is CadColor color
            ? Color.FromArgb(color.A, color.R, color.G, color.B)
            : DependencyProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is Color color
            ? CadColor.FromArgb(color.A, color.R, color.G, color.B)
            : Binding.DoNothing;
    }
}
