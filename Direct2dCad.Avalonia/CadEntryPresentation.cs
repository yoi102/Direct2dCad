using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Direct2dCad.ViewModels.Toolboxes;

namespace Direct2dCad.Avalonia;

public sealed class CadTerminalBrushConverter : IMultiValueConverter
{
    public static CadTerminalBrushConverter Instance { get; } = new();
    public object? Convert(IList<object?> values, Type type, object? parameter, CultureInfo culture) => values.FirstOrDefault() switch
    {
        CadCommandLineEntryKind.Activity => new SolidColorBrush(Color.Parse("#64B5F6")),
        CadCommandLineEntryKind.Warning => new SolidColorBrush(Color.Parse("#FFC857")),
        CadCommandLineEntryKind.Error => new SolidColorBrush(Color.Parse("#FF6B6B")),
        _ => values.ElementAtOrDefault(1)
    };
}
public sealed class CadMessageLevelConverter : IValueConverter
{
    public static CadMessageLevelConverter Instance { get; } = new();
    public static Direct2dCad.ViewModels.Services.Platform.Notifications.CadMessageLevel?[] Levels { get; } = [null, .. Enum.GetValues<Direct2dCad.ViewModels.Services.Platform.Notifications.CadMessageLevel>()];
    public object Convert(object? value, Type type, object? parameter, CultureInfo culture) => Direct2dCad.Lang.CadUiText.Get(value?.ToString() ?? "All");
    public object ConvertBack(object? value, Type type, object? parameter, CultureInfo culture) => global::Avalonia.Data.BindingOperations.DoNothing;
}
public sealed record CadMessageLevelOption(Direct2dCad.ViewModels.Services.Platform.Notifications.CadMessageLevel? Level)
{
    public string Label => Direct2dCad.Lang.CadUiText.Get(Level?.ToString() ?? "All");
}
public sealed class CadMessageFilterConverter : IValueConverter
{
    public static CadMessageFilterConverter Instance { get; } = new();
    public static CadMessageLevelOption[] Options { get; } = CadMessageLevelConverter.Levels.Select(level => new CadMessageLevelOption(level)).ToArray();
    public object Convert(object? value, Type type, object? parameter, CultureInfo culture) => Options.Single(option => Equals(option.Level, value));
    public object? ConvertBack(object? value, Type type, object? parameter, CultureInfo culture) => value is CadMessageLevelOption option ? option.Level : global::Avalonia.Data.BindingOperations.DoNothing;
}
public sealed class CadChatBrushConverter : IValueConverter
{
    public static CadChatBrushConverter Instance { get; } = new();
    public object Convert(object? value, Type type, object? parameter, CultureInfo culture) => new SolidColorBrush(Color.Parse(value switch
    {
        AiChatItemKind.User => "#243F51B5", AiChatItemKind.System => "#00000000", AiChatItemKind.Tool => "#1828A745", AiChatItemKind.Error => "#20D32F2F", _ => "#12000000"
    }));
    public object ConvertBack(object? value, Type type, object? parameter, CultureInfo culture) => global::Avalonia.Data.BindingOperations.DoNothing;
}
public sealed class CadAttachmentPreviewConverter : IValueConverter
{
    public static CadAttachmentPreviewConverter Instance { get; } = new();
    public object? Convert(object? value, Type type, object? parameter, CultureInfo culture)
    {
        if (value is not string url || !url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || url.Length > 24 * 1024 * 1024) return null;
        var separator = url.IndexOf(','); if (separator < 0) return null;
        try { using var stream = new MemoryStream(System.Convert.FromBase64String(url[(separator + 1)..])); return new Bitmap(stream); }
        catch (Exception error) when (error is FormatException or ArgumentException or IOException) { return null; }
    }
    public object ConvertBack(object? value, Type type, object? parameter, CultureInfo culture) => global::Avalonia.Data.BindingOperations.DoNothing;
}
