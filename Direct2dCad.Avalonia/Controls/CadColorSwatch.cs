using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
namespace Direct2dCad.Avalonia.Controls;
/// <summary>A compact WPF-sized swatch with the official Avalonia color editor.</summary>
public sealed class CadColorSwatch : UserControl
{
    public static readonly StyledProperty<Color> ColorProperty=AvaloniaProperty.Register<CadColorSwatch,Color>(nameof(Color),Colors.Transparent,defaultBindingMode:BindingMode.TwoWay);
    public Color Color {get=>GetValue(ColorProperty);set=>SetValue(ColorProperty,value);}
    private readonly Border _swatch=new() {Width=18,Height=18,BorderBrush=Brushes.Gray,BorderThickness=new Thickness(1)};
    private readonly ColorView _picker=new() {Width=300};
    private bool _updating;
    public CadColorSwatch()
    {
        var button=new Button {Width=33,Height=28,Padding=new Thickness(5),Background=Brushes.Transparent,Content=_swatch};
        button.Flyout=new Flyout {Content=_picker};Content=button;
        _picker.PropertyChanged+=(_,e)=>{if(!_updating&&e.Property==ColorView.ColorProperty)SetCurrentValue(ColorProperty,_picker.Color);};
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if(change.Property!=ColorProperty||_swatch is null) return;
        _swatch.Background=new SolidColorBrush(Color);
        _updating=true;try {_picker.Color=Color;}finally {_updating=false;}
    }
}
