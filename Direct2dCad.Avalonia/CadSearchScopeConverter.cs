using System.Globalization;
using Avalonia.Data.Converters;
using Direct2dCad.ViewModels.Toolboxes;
namespace Direct2dCad.Avalonia;
public sealed class CadSearchScopeConverter : IValueConverter
{
    public static CadSearchScopeConverter Instance { get; } = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is CadEntitySearchScope scope && scope.ToString() == parameter as string;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true && Enum.TryParse<CadEntitySearchScope>(parameter as string, out var scope) ? scope : global::Avalonia.Data.BindingOperations.DoNothing;
}
