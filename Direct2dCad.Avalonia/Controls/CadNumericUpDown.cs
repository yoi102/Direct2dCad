using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace Direct2dCad.Avalonia.Controls;

/// <summary>Keep range notifications compatible with Win32 UI Automation VARIANT values.</summary>
public sealed class CadNumericUpDown : NumericUpDown
{
    protected override Type StyleKeyOverride => typeof(NumericUpDown);
    protected override AutomationPeer OnCreateAutomationPeer() => new CadNumericPeer(this);

    private sealed class CadNumericPeer : ControlAutomationPeer, IRangeValueProvider
    {
        private readonly CadNumericUpDown _owner;
        public CadNumericPeer(CadNumericUpDown owner) : base(owner)
        {
            _owner = owner;
            owner.PropertyChanged += Changed;
        }
        public bool IsReadOnly => _owner.IsReadOnly || !_owner.IsEffectivelyEnabled;
        public double Maximum => (double)_owner.Maximum;
        public double Minimum => (double)_owner.Minimum;
        public double Value => (double)(_owner.Value ?? Math.Clamp(0m, _owner.Minimum, _owner.Maximum));
        public double SmallChange => (double)_owner.Increment;
        public double LargeChange => SmallChange;
        public void SetValue(double value)
        {
            EnsureEnabled();
            if (_owner.IsReadOnly) throw new InvalidOperationException("The value is read-only.");
            if (!double.IsFinite(value) || value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value));
            _owner.SetCurrentValue(ValueProperty, (decimal)value);
        }
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Spinner;
        protected override string GetClassNameCore() => "NumericUpDown";
        private void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            var property = e.Property == MinimumProperty ? RangeValuePatternIdentifiers.MinimumProperty
                : e.Property == MaximumProperty ? RangeValuePatternIdentifiers.MaximumProperty
                : e.Property == ValueProperty ? RangeValuePatternIdentifiers.ValueProperty : null;
            if (property is not null)
                RaisePropertyChangedEvent(property, ConvertRange(e.OldValue), ConvertRange(e.NewValue));
            else if (e.Property == IsReadOnlyProperty)
                RaisePropertyChangedEvent(RangeValuePatternIdentifiers.IsReadOnlyProperty, e.OldValue, e.NewValue);
        }
        private static object? ConvertRange(object? value) => value is decimal number ? (double)number : value;
    }
}
