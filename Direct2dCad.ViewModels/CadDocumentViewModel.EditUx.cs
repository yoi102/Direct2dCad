using Direct2dCad.Db.Geometry;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Lang;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel
{
    private bool _editParameterInputValid = true;
    private string _editPreviewNotice = "";
    public string EditInstruction => CadUiText.Get(EditPromptKey);
    public string EditDistanceLabel => CadUiText.Get(CadCanvasToolMode == CadCanvasToolMode.Fillet ? "Radius" : "DistanceParameter");
    public string EditSelectionDisplay => $"{CadUiText.Get("Selected")}: {_editTargets.Count}";
    public string EditReselectText => CadUiText.Get("EditReselect");
    public string EditKeyboardHint => CadUiText.Get("EditKeyboardHint");
    public string EditActionText => CadUiText.Get(CadCanvasToolMode switch
    {
        CadCanvasToolMode.Trim or CadCanvasToolMode.Extend or CadCanvasToolMode.RectArray or CadCanvasToolMode.PolarArray when !_editReady => "EditNext",
        CadCanvasToolMode.Break => "EditSingleBreak",
        CadCanvasToolMode.Offset or CadCanvasToolMode.Trim or CadCanvasToolMode.Extend => "FinishOperation",
        _ => "Confirm"
    });
    public bool CanAdvanceEdit => IsCurveEditTool && CanEditDocument && EditParametersValid && (CadCanvasToolMode switch
    {
        CadCanvasToolMode.Offset => true,
        CadCanvasToolMode.Trim or CadCanvasToolMode.Extend => _editReady || _editTargets.Count > 0,
        CadCanvasToolMode.Fillet or CadCanvasToolMode.Chamfer => _editTargets.Count >= (EditWholePolyline ? 1 : 2),
        CadCanvasToolMode.Join => _editTargets.Count >= 2,
        CadCanvasToolMode.Break => _editBreakPoint is not null,
        CadCanvasToolMode.RectArray => _editTargets.Count > 0,
        CadCanvasToolMode.PolarArray => _editTargets.Count > 0,
        _ => false
    });
    public string EditFeedback => CadCanvasToolMode == CadCanvasToolMode.Offset && !IsOffsetDistanceLocked &&
        _editParameterInputValid && Millimetres(EditDistance) <= CadGeometryTolerance.Absolute && string.IsNullOrEmpty(StepInputError) ? "" :
        !EditParametersValid ? CadUiText.Get(HasRectArrayParameters || HasPolarArrayParameters ? "EditInvalidArrayParameters" : "EditInvalidParameters") :
        !string.IsNullOrEmpty(StepInputError) ? StepInputError : _editPreviewNotice;
    private bool EditParametersValid => _editParameterInputValid &&
        (!HasDistanceParameter || double.IsFinite(Millimetres(EditDistance)) && Millimetres(EditDistance) > CadGeometryTolerance.Absolute) &&
        (!HasChamferParameter || double.IsFinite(Millimetres(EditSecondDistance)) && Millimetres(EditSecondDistance) > CadGeometryTolerance.Absolute) &&
        (!HasRectArrayParameters || ArrayRows is >= 1 and <= 1000 && ArrayColumns is >= 1 and <= 1000 &&
            (long)ArrayRows * ArrayColumns is >= 2 && (long)ArrayRows * ArrayColumns * Math.Max(1, _editTargets.Count) <= 10000 &&
            double.IsFinite(Millimetres(ArraySpacingX) * Math.Max(1, ArrayColumns-1)) && double.IsFinite(Millimetres(ArraySpacingY) * Math.Max(1, ArrayRows-1)) &&
            (ArrayColumns == 1 || Math.Abs(Millimetres(ArraySpacingX)) > CadGeometryTolerance.Absolute) &&
            (ArrayRows == 1 || Math.Abs(Millimetres(ArraySpacingY)) > CadGeometryTolerance.Absolute)) &&
        (!HasPolarArrayParameters || ArrayCount is >= 2 and <= 1000 && (long)ArrayCount * Math.Max(1, _editTargets.Count) <= 10000 &&
            double.IsFinite(ArraySweepDegrees) && Math.Abs(ArraySweepDegrees) is > 1e-7 and <= 360);

    public void SetEditParameterInputValid(bool valid)
    {
        if (_editParameterInputValid == valid) return;
        _editParameterInputValid = valid;
        if (valid) { StepInputError=""; EditErrorDetail=""; }
        NotifyEditUx();
        RequestOverlayRender();
    }
    private void NotifyEditUx()
    {
        foreach (var name in new[] { nameof(EditInstruction), nameof(EditDistanceLabel), nameof(EditSelectionDisplay), nameof(EditReselectText), nameof(EditKeyboardHint), nameof(EditActionText), nameof(CanAdvanceEdit), nameof(EditFeedback) })
            OnPropertyChanged(name);
    }
    private void EditParametersChanged()
    {
        StepInputError = "";
        EditErrorDetail = "";
        if (!_applyingEditDynamicInput && _dynamicInputKind == DynamicInputKind.EditParameters)
        {
            RefreshDynamicInputValues(reset: true);
            SetEditParameterInputValid(true);
            DynamicInputError = "";
        }
        NotifyEditUx();
        RequestOverlayRender();
    }
    partial void OnEditDistanceChanged(double value)
    {
        if (CadCanvasToolMode == CadCanvasToolMode.Offset && !_applyingEditDynamicInput)
            _offsetDistanceLockedByParameter = true;
        EditParametersChanged();
    }
    partial void OnEditSecondDistanceChanged(double value) => EditParametersChanged();
    partial void OnArrayRowsChanged(int value) => EditParametersChanged();
    partial void OnArrayColumnsChanged(int value) => EditParametersChanged();
    partial void OnArrayCountChanged(int value) => EditParametersChanged();
    partial void OnArraySpacingXChanged(double value) => EditParametersChanged();
    partial void OnArraySpacingYChanged(double value) => EditParametersChanged();
    partial void OnArraySweepDegreesChanged(double value) => EditParametersChanged();
    partial void OnArrayRotateCopiesChanged(bool value) => EditParametersChanged();
    partial void OnTrimCornerLinesChanged(bool value) => EditParametersChanged();
    partial void OnEditWholePolylineChanged(bool value)
    {
        OnPropertyChanged(nameof(CanChangeCornerTrim));
        if(value && _editTargets.Count>1)
        { _editTargets.RemoveRange(1,_editTargets.Count-1); _editPicks.RemoveRange(1,_editPicks.Count-1); }
        _editCornerPreviewTarget=null;
        var applying=_applyingEditDynamicInput;
        _applyingEditDynamicInput=true;
        try { if(value) TrimCornerLines=true; }
        finally { _applyingEditDynamicInput=applying; }
        CadEditor.Selection.Replace(_editTargets);
        StepInputError=""; EditErrorDetail="";
        NotifyDrawingUx();
        RequestOverlayRender();
    }

    [RelayCommand]
    private void ReselectEditObjects()
    {
        var valid=_editParameterInputValid;
        var distanceLocked = _offsetDistanceLockedByParameter;
        ClearEditInteraction(); _editParameterInputValid=valid;
        _offsetDistanceLockedByParameter = distanceLocked;
        CadEditor.Selection.Clear(); StepInputError=""; EditErrorDetail="";
        NotifyDrawingUx(); RequestOverlayRender(updateHandleScene:true);
    }
}
