using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang;

namespace Direct2dCad.ViewModels;

public partial class EditorTabViewModel
{
    public bool CanBooleanSelection => CadDocumentViewModel.CanBooleanSelection;
    public string BooleanSelectionDisabledReason => CadDocumentViewModel.BooleanSelectionDisabledReason;
    public string BooleanSelectionHelpText => GetBooleanHelpText("BooleanOperations");
    public string BooleanUnionHelpText => GetBooleanHelpText("BooleanUnion");
    public string BooleanIntersectionHelpText => GetBooleanHelpText("BooleanIntersection");
    public string BooleanDifferenceHelpText => GetBooleanHelpText("BooleanDifference");

    private string GetBooleanHelpText(string operationKey)
    {
        var label = CadUiText.Get(operationKey);
        var reason = BooleanSelectionDisabledReason;
        return reason.Length == 0 ? label : label + Environment.NewLine + reason;
    }

    internal void RefreshBooleanAvailability() => NotifyBooleanCommands();

    [RelayCommand(CanExecute = nameof(CanBooleanSelection))]
    private Task BooleanUnion() => CadDocumentViewModel.BeginBoolean(CadBooleanOperation.Union);
    [RelayCommand(CanExecute = nameof(CanBooleanSelection))]
    private Task BooleanIntersection() => CadDocumentViewModel.BeginBoolean(CadBooleanOperation.Intersection);
    [RelayCommand(CanExecute = nameof(CanBooleanSelection))]
    private Task BooleanDifference() => CadDocumentViewModel.BeginBoolean(CadBooleanOperation.Difference);
    private void NotifyBooleanCommands()
    {
        OnPropertyChanged(nameof(CanBooleanSelection));
        OnPropertyChanged(nameof(BooleanSelectionDisabledReason));
        OnPropertyChanged(nameof(BooleanSelectionHelpText));
        OnPropertyChanged(nameof(BooleanUnionHelpText));
        OnPropertyChanged(nameof(BooleanIntersectionHelpText));
        OnPropertyChanged(nameof(BooleanDifferenceHelpText));
        BooleanUnionCommand.NotifyCanExecuteChanged();
        BooleanIntersectionCommand.NotifyCanExecuteChanged();
        BooleanDifferenceCommand.NotifyCanExecuteChanged();
    }
}
