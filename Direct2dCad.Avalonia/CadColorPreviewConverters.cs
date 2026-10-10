using System.Globalization;
using global::Avalonia.Data.Converters;
using global::Avalonia.Media;

namespace Direct2dCad.Avalonia;

public sealed class CadColorPreviewBrushConverter : IValueConverter
{
    public static CadColorPreviewBrushConverter Instance { get; } = new();
    public object? Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>value is Color color?new SolidColorBrush(color):null;
    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>global::Avalonia.Data.BindingOperations.DoNothing;
}

public sealed class CadColorPreviewTextConverter : IValueConverter
{
    public static CadColorPreviewTextConverter Instance { get; } = new();
    public object? Convert(object? value,Type targetType,object? parameter,CultureInfo culture)=>value is Color color?color.ToString():null;
    public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>global::Avalonia.Data.BindingOperations.DoNothing;
}
