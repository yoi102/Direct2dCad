using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Direct2dCad.Avalonia.Controls;

public sealed class CadIcon : PathIcon
{
    protected override Type StyleKeyOverride => typeof(PathIcon);
    public static readonly StyledProperty<string?> KindProperty = AvaloniaProperty.Register<CadIcon, string?>(nameof(Kind));
    public static readonly StyledProperty<string?> OnKindProperty = AvaloniaProperty.Register<CadIcon, string?>(nameof(OnKind));
    public static readonly StyledProperty<bool> IsOnProperty = AvaloniaProperty.Register<CadIcon, bool>(nameof(IsOn));
    public string? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string? OnKind { get => GetValue(OnKindProperty); set => SetValue(OnKindProperty, value); }
    public bool IsOn { get => GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }
    public CadIcon() { Width = Height = 16; }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty || change.Property == OnKindProperty || change.Property == IsOnProperty)
            Data = CadIconData.Get(IsOn && OnKind is not null ? OnKind : Kind) is { } data ? StreamGeometry.Parse(data) : null;
    }
}
