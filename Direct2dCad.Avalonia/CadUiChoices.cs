using System.Globalization;
using Avalonia.Data.Converters;
using Direct2dCad.ViewModels.Enums;
namespace Direct2dCad.Avalonia;
public static class CadUiChoices
{
    public static ViewModelCadUnit[] Units { get; } = Enum.GetValues<ViewModelCadUnit>();
}
public sealed class CadLayoutSpaceConverter : IValueConverter
{
    public static CadLayoutSpaceConverter Instance { get; } = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is CadLayoutSpaceMode mode && mode.ToString() == parameter as string;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true && Enum.TryParse<CadLayoutSpaceMode>(parameter as string, out var mode) ? mode : global::Avalonia.Data.BindingOperations.DoNothing;
}
public sealed class CadEnumLabelConverter : IValueConverter
{
    public static CadEnumLabelConverter Instance { get; } = new();
    public object Convert(object? value, Type type, object? parameter, CultureInfo culture) => value is null ? "" : Direct2dCad.Lang.CadUiText.Get(CadEnumLabelData.Key(value));
    public object ConvertBack(object? value, Type type, object? parameter, CultureInfo culture) => global::Avalonia.Data.BindingOperations.DoNothing;
}
