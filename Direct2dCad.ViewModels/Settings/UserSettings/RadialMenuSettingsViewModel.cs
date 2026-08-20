using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Lang.Strings;

namespace Direct2dCad.ViewModels.Settings.UserSettings;

public sealed record CadRadialMenuActionOption(
    CadRadialMenuAction Action,
    string DisplayName);

public sealed partial class RadialMenuSettingsViewModel : ObservableObject
{
    public RadialMenuSettingsViewModel(CadRadialMenuSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();

        ActionOptions = Enum.GetValues<CadRadialMenuAction>()
            .Select(action => new CadRadialMenuActionOption(
                action,
                Localized(CadRadialMenuActionCatalog.GetResourceKey(action))))
            .ToArray();
        Profiles =
        [
            new(CadRadialMenuGesture.Middle, Localized("RadialMenuMiddleMouse"), settings.GetActions(CadRadialMenuGesture.Middle), ActionOptions),
            new(CadRadialMenuGesture.ShiftMiddle, Localized("RadialMenuShiftMiddleMouse"), settings.GetActions(CadRadialMenuGesture.ShiftMiddle), ActionOptions),
            new(CadRadialMenuGesture.ControlMiddle, Localized("RadialMenuControlMiddleMouse"), settings.GetActions(CadRadialMenuGesture.ControlMiddle), ActionOptions),
            new(CadRadialMenuGesture.AltMiddle, Localized("RadialMenuAltMiddleMouse"), settings.GetActions(CadRadialMenuGesture.AltMiddle), ActionOptions)
        ];
        IsEnabled = settings.IsEnabled;
        SelectedProfile = Profiles[0];
    }

    public IReadOnlyList<CadRadialMenuActionOption> ActionOptions { get; }
    public IReadOnlyList<RadialMenuProfileViewModel> Profiles { get; }

    [ObservableProperty] public partial bool IsEnabled { get; set; }
    [ObservableProperty] public partial RadialMenuProfileViewModel SelectedProfile { get; set; }

    internal void ApplyTo(CadRadialMenuSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.IsEnabled = IsEnabled;
        foreach (var profile in Profiles)
            settings.SetActions(profile.Gesture, profile.Slots.Select(slot => slot.SelectedAction.Action));
    }

    internal void ResetToDefaults()
    {
        var defaults = new CadRadialMenuSettings();
        IsEnabled = defaults.IsEnabled;
        foreach (var profile in Profiles)
            profile.Load(defaults.GetActions(profile.Gesture));
        SelectedProfile = Profiles[0];
    }

    private static string Localized(string resourceKey) =>
        Strings.ResourceManager.GetString(resourceKey, System.Globalization.CultureInfo.CurrentUICulture) ?? resourceKey;
}

public sealed class RadialMenuProfileViewModel
{
    private readonly IReadOnlyList<CadRadialMenuActionOption> _actionOptions;

    internal RadialMenuProfileViewModel(
        CadRadialMenuGesture gesture,
        string displayName,
        IReadOnlyList<CadRadialMenuAction> actions,
        IReadOnlyList<CadRadialMenuActionOption> actionOptions)
    {
        Gesture = gesture;
        DisplayName = displayName;
        _actionOptions = actionOptions;
        Slots = new ObservableCollection<RadialMenuSlotViewModel>();
        for (var index = 0; index < CadRadialMenuSettings.SectorCount; index++)
            Slots.Add(new RadialMenuSlotViewModel(index, ResolveOption(actions[index])));
    }

    public CadRadialMenuGesture Gesture { get; }
    public string DisplayName { get; }
    public ObservableCollection<RadialMenuSlotViewModel> Slots { get; }

    internal void Load(IReadOnlyList<CadRadialMenuAction> actions)
    {
        for (var index = 0; index < Slots.Count; index++)
            Slots[index].SelectedAction = ResolveOption(actions[index]);
    }

    private CadRadialMenuActionOption ResolveOption(CadRadialMenuAction action) =>
        _actionOptions.First(option => option.Action == action);
}

public sealed partial class RadialMenuSlotViewModel : ObservableObject
{
    internal RadialMenuSlotViewModel(int index, CadRadialMenuActionOption selectedAction)
    {
        Index = index;
        SelectedAction = selectedAction;
    }

    public int Index { get; }
    public string DisplayName => string.Format(
        Strings.ResourceManager.GetString("RadialMenuSlot", System.Globalization.CultureInfo.CurrentUICulture) ?? "Slot {0}",
        Index + 1);

    [ObservableProperty] public partial CadRadialMenuActionOption SelectedAction { get; set; }
}
