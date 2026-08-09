using System.Globalization;
using System.Windows.Data;
using Direct2dCad.ViewModels.Services.Platform.Notifications;

namespace Direct2dCad.wpf.Converters;

internal sealed class CadMessageLevelToStringConverter : IValueConverter
{
    public static CadMessageLevelToStringConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var resourceKey = value is CadMessageLevel level
            ? level switch
            {
                CadMessageLevel.Information => "Information",
                CadMessageLevel.Warning => "Warning",
                CadMessageLevel.Error => "Error",
                _ => null
            }
            : null;

        return resourceKey is null
            ? string.Empty
            : Direct2dCad.Lang.Strings.Strings.ResourceManager.GetString(
                  resourceKey,
                  CultureInfo.CurrentUICulture)
              ?? resourceKey;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
