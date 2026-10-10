using System.Globalization;
using Avalonia.Data.Converters;

namespace Direct2dCad.Avalonia;

public sealed class CadColorEditorEnabledConverter : IMultiValueConverter
{
    public static CadColorEditorEnabledConverter Instance { get; } = new();
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count == 3 && (values[0] is true ? values[1] is true : values[2] is true);
}
