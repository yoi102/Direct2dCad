using System.Globalization;
using global::Avalonia.Data;
using global::Avalonia.Data.Converters;

namespace Direct2dCad.Avalonia;

// DataGrid's default converter supports arbitrary reflection-based conversions.
// Vertex columns only need these known primitives, including culture-aware input.
public sealed class CadGridNumberConverter : IValueConverter
{
    public static CadGridNumberConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        double number => number.ToString("G17", culture),
        int index => index.ToString(culture),
        _ => global::Avalonia.AvaloniaProperty.UnsetValue
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (targetType == typeof(double) && value is string text &&
            double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, culture, out var number) && double.IsFinite(number))
            return number;
        return new BindingNotification(new FormatException(), BindingErrorType.DataValidationError);
    }
}
