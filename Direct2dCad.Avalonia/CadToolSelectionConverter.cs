using System.Globalization;
using Avalonia.Data.Converters;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.Avalonia;

public sealed class CadToolSelectionConverter : IValueConverter
{
    public static CadToolSelectionConverter Instance { get; } = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CadCanvasToolMode mode && parameter is string name && Enum.TryParse<CadCanvasToolMode>(name, out var selected) && mode == selected;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => global::Avalonia.Data.BindingOperations.DoNothing;
}
