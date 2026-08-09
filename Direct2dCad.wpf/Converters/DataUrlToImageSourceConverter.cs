using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Direct2dCad.wpf.Converters;

internal sealed class DataUrlToImageSourceConverter : IValueConverter
{
    public static DataUrlToImageSourceConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string dataUrl ||
            !dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return Binding.DoNothing;
        }

        var separator = dataUrl.IndexOf(',');
        if (separator < 0)
            return Binding.DoNothing;

        try
        {
            var bytes = System.Convert.FromBase64String(dataUrl[(separator + 1)..]);
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (FormatException)
        {
            return Binding.DoNothing;
        }
        catch (InvalidDataException)
        {
            return Binding.DoNothing;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
