using System.Globalization;
using global::Avalonia.Data.Converters;
using global::Avalonia.Markup.Xaml;
using global::Avalonia.Media;
using Direct2dCad.Db.Cad;
using Direct2dCad.Lang;

namespace Direct2dCad.Avalonia;

public sealed class Loc(string key) : MarkupExtension
{
    private static readonly List<(WeakReference<object> Target, Action<object, string> Set, string Key)> Targets = [];
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget { TargetObject: { } target, TargetProperty: { } property })
        {
            if (property is global::Avalonia.AvaloniaProperty ap && target is global::Avalonia.AvaloniaObject)
                Targets.Add((new(target), (obj, value) => ((global::Avalonia.AvaloniaObject)obj).SetValue(ap, value), key));
            else if (property is global::Avalonia.Data.Core.IPropertyInfo info)
                Targets.Add((new(target), (obj, value) => info.Set(obj, value), key));
        }
        return CadUiText.Get(key);
    }
    internal static void Track(global::Avalonia.AvaloniaObject target, global::Avalonia.AvaloniaProperty property, string key)
    {
        Targets.RemoveAll(entry => entry.Target.TryGetTarget(out var current) && ReferenceEquals(current,target));
        Targets.Add((new(target),(obj,value)=>((global::Avalonia.AvaloniaObject)obj).SetValue(property,value),key));
        target.SetValue(property,CadUiText.Get(key));
    }
    internal static void Refresh()
    {
        for (var i = Targets.Count - 1; i >= 0; i--)
            if (Targets[i].Target.TryGetTarget(out var target)) Targets[i].Set(target, CadUiText.Get(Targets[i].Key));
            else Targets.RemoveAt(i);
        App.Window?.RefreshLocalizedPanels();
    }
}
public sealed class CadColorConverter : IValueConverter
{
    public static CadColorConverter Instance { get; } = new();
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is CadColor c ? Color.FromArgb(c.A, c.R, c.G, c.B) : Colors.Transparent;
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is Color c ? CadColor.FromArgb(c.A, c.R, c.G, c.B) : global::Avalonia.Data.BindingOperations.DoNothing;
}

