using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels;

public partial class EditorTabViewModel
{
    public bool CanBooleanSelection => CadDocumentViewModel.CanBooleanSelection;
    [RelayCommand(CanExecute = nameof(CanBooleanSelection))]
    private Task BooleanUnion() => CadDocumentViewModel.BeginBoolean(CadBooleanOperation.Union);
    [RelayCommand(CanExecute = nameof(CanBooleanSelection))]
    private Task BooleanIntersection() => CadDocumentViewModel.BeginBoolean(CadBooleanOperation.Intersection);
    [RelayCommand(CanExecute = nameof(CanBooleanSelection))]
    private Task BooleanDifference() => CadDocumentViewModel.BeginBoolean(CadBooleanOperation.Difference);
    private void NotifyBooleanCommands()
    {
        OnPropertyChanged(nameof(CanBooleanSelection));
        BooleanUnionCommand.NotifyCanExecuteChanged();
        BooleanIntersectionCommand.NotifyCanExecuteChanged();
        BooleanDifferenceCommand.NotifyCanExecuteChanged();
    }
}
