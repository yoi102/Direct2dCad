using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;

namespace Direct2dCad.Avalonia.Controls;

/// <summary>Formats numeric properties without converting the display text back into geometry.</summary>
public sealed class CadPropertyNumberBox : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);
    public static readonly StyledProperty<object?> ValueProperty = AvaloniaProperty.Register<CadPropertyNumberBox, object?>(
        nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<string> FormatStringProperty = AvaloniaProperty.Register<CadPropertyNumberBox, string>(nameof(FormatString), "0.########");
    public static readonly StyledProperty<double> IntervalProperty = AvaloniaProperty.Register<CadPropertyNumberBox, double>(nameof(Interval), 1);
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<CadPropertyNumberBox, double>(nameof(Minimum), double.NegativeInfinity);
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<CadPropertyNumberBox, double>(nameof(Maximum), double.PositiveInfinity);
    public static readonly StyledProperty<bool> ShowStepperProperty = AvaloniaProperty.Register<CadPropertyNumberBox, bool>(nameof(ShowStepper));
    private bool _displaying, _writing;
    private string? _displayText;
    public object? Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string FormatString { get => GetValue(FormatStringProperty); set => SetValue(FormatStringProperty, value); }
    public double Interval { get => GetValue(IntervalProperty); set => SetValue(IntervalProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public bool ShowStepper { get => GetValue(ShowStepperProperty); set => SetValue(ShowStepperProperty, value); }
    private readonly StackPanel _stepper;

    public CadPropertyNumberBox()
    {
        _stepper = new StackPanel { Spacing = 0, Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        foreach (var direction in new[] { 1, -1 })
        {
            var button = new RepeatButton { Content = direction > 0 ? "▴" : "▾", FontSize = 10, Padding = new Thickness(0), Width = 18, MinHeight = 12, Height = 12, Focusable = false };
            global::Avalonia.Automation.AutomationProperties.SetName(button,direction>0?"Increase value":"Decrease value");
            button.Classes.Add("numericStep");
            button.Click += (_, _) => Step(direction);
            _stepper.Children.Add(button);
        }
        InnerRightContent = _stepper;
        TextChanged += (_, _) => CommitText();
        LostFocus += (_, _) => { CommitText(); DisplayValue(); };
        KeyDown += (_, e) => { if (ShowStepper && !IsReadOnly && e.KeyModifiers == KeyModifiers.None && e.Key is Key.Up or Key.Down) { Step(e.Key == Key.Up ? 1 : -1); e.Handled = true; } };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == ValueProperty && !_writing) || change.Property == FormatStringProperty) DisplayValue();
        if (_stepper is not null && (change.Property == ShowStepperProperty || change.Property == IsReadOnlyProperty)) _stepper.IsVisible = ShowStepper && !IsReadOnly;
    }

    private void DisplayValue()
    {
        _displayText = Value switch
        {
            double value => value.ToString(FormatString, CultureInfo.CurrentCulture),
            float value => value.ToString(FormatString, CultureInfo.CurrentCulture),
            decimal value => value.ToString(FormatString, CultureInfo.CurrentCulture),
            int value => value.ToString(FormatString, CultureInfo.CurrentCulture),
            _ => null
        };
        _displaying = true;
        try { Text = _displayText; DataValidationErrors.ClearErrors(this); }
        finally { _displaying = false; }
    }

    private void CommitText()
    {
        if (_displaying || IsReadOnly || Text == _displayText || Value is null) return;
        object? parsed = null;
        const NumberStyles style = NumberStyles.Float;
        if (Value is double && double.TryParse(Text, style, CultureInfo.CurrentCulture, out var d) && double.IsFinite(d)) parsed = d;
        else if (Value is float && float.TryParse(Text, style, CultureInfo.CurrentCulture, out var f) && float.IsFinite(f)) parsed = f;
        else if (Value is decimal && decimal.TryParse(Text, style, CultureInfo.CurrentCulture, out var m)) parsed = m;
        else if (Value is int && int.TryParse(Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var i)) parsed = i;
        if (parsed is not null && !WithinBounds(parsed)) parsed = null;
        if (parsed is null) { DataValidationErrors.SetErrors(this, [new FormatException("Invalid numeric value")]); return; }
        DataValidationErrors.ClearErrors(this);
        _writing = true;
        try { SetCurrentValue(ValueProperty, parsed); }
        finally { _writing = false; }
    }

    internal bool TryCommitEdit()
    {
        CommitText();
        return !DataValidationErrors.GetHasErrors(this);
    }

    internal void CancelEdit() => DisplayValue();

    private bool WithinBounds(object value)
    {
        var number = value switch { double d => d, float f => f, decimal m => (double)m, int i => i, _ => double.NaN };
        return double.IsFinite(number) && number >= Minimum && number <= Maximum;
    }

    internal void Step(int direction)
    {
        if (IsReadOnly || !IsEnabled || !double.IsFinite(Interval) || Interval <= 0 || Minimum > Maximum) return;
        var current = Value switch { double d => d, float f => f, decimal m => (double)m, int i => i, _ => double.NaN };
        var next = Math.Clamp(current + direction * Interval, Minimum, Maximum);
        if (!double.IsFinite(next)) return;
        object value;
        try
        {
            value = Value switch { double => next, float => checked((float)next), decimal => checked((decimal)next), int => checked((int)Math.Round(next)), _ => next };
        }
        catch (OverflowException) { return; }
        if (!WithinBounds(value)) return;
        SetCurrentValue(ValueProperty, value);
        DisplayValue();
    }
}
