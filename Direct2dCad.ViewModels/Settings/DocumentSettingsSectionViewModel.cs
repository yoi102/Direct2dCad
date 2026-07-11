using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Cad.Settings;

namespace Direct2dCad.ViewModels.Settings;

public abstract class DocumentSettingsSectionViewModel : ObservableObject
{
    protected DocumentSettingsSectionViewModel(string title)
    {
        Title = title;
    }

    public string Title { get; }

    internal abstract bool TryApplyTo(CadViewSettings settings);

    internal abstract void ResetToDefaults();

    protected static bool IsPositiveFinite(double value) => value > 0 && IsFinite(value);

    protected static bool IsNonNegativeFinite(double value) => value >= 0 && IsFinite(value);

    protected static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
