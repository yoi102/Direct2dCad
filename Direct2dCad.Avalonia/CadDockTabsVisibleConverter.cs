using System.Globalization;
using Avalonia.Data.Converters;
using Direct2dCad.Avalonia.Services;

namespace Direct2dCad.Avalonia;

public sealed class CadDockTabsVisibleConverter : IMultiValueConverter
{
    public static CadDockTabsVisibleConverter Instance { get; } = new();
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count != 2 || values[0] is not int count || count != 1 || values[1] is not CadDockDocument;
}
