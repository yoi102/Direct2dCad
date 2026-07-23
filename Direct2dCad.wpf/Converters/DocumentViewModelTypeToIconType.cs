using System.Globalization;
using System.Windows.Data;
using Direct2dCad.ViewModels;

namespace Direct2dCad.wpf.Converters;

internal class DocumentViewModelTypeToIconType : IValueConverter
{
    public static readonly DocumentViewModelTypeToIconType Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AvalonDock.Layout.LayoutDocument document && document.Content is CadObservableDocument)
            return "LeadPencil";

        return "HandWave";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
