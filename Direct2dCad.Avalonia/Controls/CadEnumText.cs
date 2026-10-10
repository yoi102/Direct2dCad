using Avalonia;
using Avalonia.Controls;
namespace Direct2dCad.Avalonia.Controls;
public sealed class CadEnumText : TextBlock
{
    public static readonly StyledProperty<object?> ValueProperty=AvaloniaProperty.Register<CadEnumText,object?>(nameof(Value));
    public object? Value {get=>GetValue(ValueProperty);set=>SetValue(ValueProperty,value);}
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property==ValueProperty) Loc.Track(this,TextProperty,CadEnumLabelData.Key(Value));
    }
}
