using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Direct2dCad.Avalonia.Controls;

/// <summary>The WPF toolbox interaction: display a label, double-click or F2 to rename.</summary>
public sealed class CadEditableText : UserControl
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<CadEditableText, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<CadEditableText, bool>(nameof(IsReadOnly));
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public bool IsReadOnly { get => GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    private readonly TextBlock _label = new() { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _editor = new() { IsVisible = false, MinHeight = 22, Padding = new Thickness(2, 0) };
    private bool _editing;
    public CadEditableText()
    {
        Focusable = true;
        var grid = new Grid(); grid.Children.Add(_label); grid.Children.Add(_editor); Content = grid;
        DoubleTapped += (_, e) => { if (BeginEdit()) e.Handled = true; };
        KeyDown += (_, e) => { if (!_editing && e.Key == Key.F2 && BeginEdit()) e.Handled = true; };
        _editor.KeyDown += (_, e) => { if (e.Key == Key.Enter) { EndEdit(true); e.Handled = true; } else if (e.Key == Key.Escape) { EndEdit(false); e.Handled = true; } };
        _editor.LostFocus += (_, _) => { if(_editor.ContextMenu?.IsOpen!=true && _editor.ContextFlyout?.IsOpen!=true) EndEdit(true); };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && _label is not null) _label.Text = Text;
    }
    internal bool BeginEdit()
    {
        if (IsReadOnly || !IsEnabled) return false;
        _editing = true; _editor.Text = Text; _label.IsVisible = false; _editor.IsVisible = true;
        _editor.Focus(); _editor.SelectAll(); return true;
    }
    internal void EndEdit(bool commit)
    {
        if (!_editing) return;
        if (commit && string.IsNullOrWhiteSpace(_editor.Text)) { DataValidationErrors.SetErrors(_editor, [new FormatException("Name cannot be empty")]); return; }
        _editing = false;
        if (commit) SetCurrentValue(TextProperty, _editor.Text);
        DataValidationErrors.ClearErrors(_editor); _editor.IsVisible = false; _label.IsVisible = true;
    }
}
