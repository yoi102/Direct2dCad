using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.CommandLine;
using Direct2dCad.Commands;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Snapping;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel
{
    private readonly CadObjectSnapController _objectSnap = new();
    public bool IsGridSnapEnabled { get => CadEditor.Document.ViewSettings.Snap.GridEnabled; set => SetSnapping(CadEditor.Document.ViewSettings.Snap with { GridEnabled=value }); }
    public bool IsObjectSnapEnabled { get => CadEditor.Document.ViewSettings.Snap.ObjectsEnabled; set => SetSnapping(CadEditor.Document.ViewSettings.Snap with { ObjectsEnabled=value }); }
    public bool IsOrthoEnabled { get => CadEditor.Document.ViewSettings.Snap.OrthoEnabled; set => SetSnapping(CadEditor.Document.ViewSettings.Snap with { OrthoEnabled=value, PolarEnabled=value ? false : IsPolarEnabled }); }
    public bool IsPolarEnabled { get => CadEditor.Document.ViewSettings.Snap.PolarEnabled; set => SetSnapping(CadEditor.Document.ViewSettings.Snap with { PolarEnabled=value, OrthoEnabled=value ? false : IsOrthoEnabled }); }
    public double PolarIncrementDegrees { get => CadEditor.Document.ViewSettings.Snap.PolarIncrementDegrees; set { if (double.IsFinite(value) && value is > 0 and <= 180) SetSnapping(CadEditor.Document.ViewSettings.Snap with { PolarIncrementDegrees=value }); } }
    public bool IsPerpendicularSnapEnabled { get => (CadEditor.Document.ViewSettings.Snap.Modes & CadObjectSnapModes.Perpendicular)!=0; set => SetSnapMode(CadObjectSnapModes.Perpendicular,value); }
    public bool IsTangentSnapEnabled { get => (CadEditor.Document.ViewSettings.Snap.Modes & CadObjectSnapModes.Tangent)!=0; set => SetSnapMode(CadObjectSnapModes.Tangent,value); }
    public string SnapCandidateDisplay => _objectSnap.Current is { } candidate ? CadUiText.Get("Snap"+candidate.Kind) : "";
    public bool HasSnapCandidates => _objectSnap.Candidates.Count>1;
    public bool HasActiveDrawingTool => CadCanvasToolMode != CadCanvasToolMode.Select;
    public string CurrentToolNameDisplay => CadUiText.Get(CadCanvasToolMode.ToString());
    public bool CanEditDocument => !CadEditor.Document.IsReadOnly;
    public string PropertyContext => CadEditor.Selection.EntityIds.Count switch
    { 0 => CadUiText.Get(LangKeys.NewDrawingSettings), 1 => CadUiText.Get(LangKeys.SelectedEntitySettings), _ => CadUiText.Get(LangKeys.MultipleEntitySettings) };

    public CadPointD? DrawingAnchor => _dimensionAnchors.LastOrDefault()?.Point ?? _drawingState.PendingPolylinePoints.LastOrDefaultNullable() ??
        _drawingState.PendingPolygonPoints.LastOrDefaultNullable() ?? _drawingState.PendingSplinePoints.LastOrDefaultNullable() ??
        _drawingState.PendingEllipsePoints.LastOrDefaultNullable() ?? _drawingState.PendingCircleSecondPoint ??
        _drawingState.PendingArcStartPoint ?? _drawingState.PendingWorldPoint;

    public string CurrentStepPrompt
    {
        get
        {
            if (IsBooleanTool) return BooleanPrompt;
            if (IsDimensionTool) return DimensionPrompt;
            if (IsCurveEditTool) return EditPrompt;
            if (CadCanvasToolMode == CadCanvasToolMode.Select) return CadUiText.Get(LangKeys.SelectPrompt);
            var detailed=DetailedDrawingStep();
            if(detailed is not null) return CadUiText.Get(CadCanvasToolMode.ToString())+": "+CadUiText.Get(detailed);
            var step = DrawingAnchor is null ? LangKeys.SpecifyFirstPoint : LangKeys.SpecifyNextPoint;
            if (CadCanvasToolMode is CadCanvasToolMode.CircleCenterRadius or CadCanvasToolMode.CircleCenterDiameter && DrawingAnchor is not null) step=LangKeys.SpecifyRadiusPoint;
            if (CadCanvasToolMode is CadCanvasToolMode.Polyline or CadCanvasToolMode.Polygon or CadCanvasToolMode.Spline && DrawingAnchor is not null) step=LangKeys.SpecifyNextOrFinish;
            return CadUiText.Get(CadCanvasToolMode.ToString()) + ": " + CadUiText.Get(step);
        }
    }

    private string? DetailedDrawingStep()
    {
        var count=_drawingState.PendingEllipsePoints.Count;
        if(CadCanvasToolMode is CadCanvasToolMode.EllipseCenter or CadCanvasToolMode.EllipseAxisEnd or CadCanvasToolMode.EllipseArc)
            return count switch { 0=>CadCanvasToolMode==CadCanvasToolMode.EllipseCenter ? "PromptCenter" : "PromptAxisStart",1=>"PromptAxisEnd",2=>"PromptSecondAxis",3=>"PromptArcStart",_=>"PromptArcEnd" };
        if(CadCanvasToolMode==CadCanvasToolMode.ArcContinue) return "PromptArcEnd";
        if(CadCanvasToolMode is >= CadCanvasToolMode.ArcThreePoint and <= CadCanvasToolMode.ArcCenterStartLength)
        {
            var centerFirst=CadCanvasToolMode is CadCanvasToolMode.ArcCenterStartEnd or CadCanvasToolMode.ArcCenterStartAngle or CadCanvasToolMode.ArcCenterStartLength;
            if(_drawingState.PendingWorldPoint is null) return centerFirst ? "PromptCenter" : "PromptArcStart";
            if(_drawingState.PendingArcStartPoint is null) return CadCanvasToolMode switch
            { CadCanvasToolMode.ArcThreePoint=>"PromptArcThrough",CadCanvasToolMode.ArcStartCenterEnd or CadCanvasToolMode.ArcStartCenterAngle or CadCanvasToolMode.ArcStartCenterLength=>"PromptCenter",_=>centerFirst ? "PromptArcStart" : "PromptArcEnd" };
            return CadCanvasToolMode switch
            { CadCanvasToolMode.ArcStartCenterAngle or CadCanvasToolMode.ArcStartEndAngle or CadCanvasToolMode.ArcCenterStartAngle=>"PromptArcAngle",CadCanvasToolMode.ArcStartCenterLength or CadCanvasToolMode.ArcCenterStartLength=>"PromptArcLength",CadCanvasToolMode.ArcStartEndRadius=>"PromptArcRadius",_=>"PromptArcEnd" };
        }
        return null;
    }

    [ObservableProperty] public partial string StepInput { get; set; } = "";
    [ObservableProperty] public partial string StepInputError { get; private set; } = "";

    [RelayCommand]
    private void SubmitStepInput()
    {
        var input=StepInput.Trim();
        StepInputError = "";
        if (input.Length==0)
        {
            if (!CompleteCurrentDrawing().Handled) StepInputError = CadUiText.Get("DrawingCannotComplete");
            return;
        }
        var result=new CadCommandLineService().Execute(input,this);
        if (result.Success) StepInputError = "";
        else if (string.IsNullOrEmpty(StepInputError)) StepInputError = CadUiText.Get(LangKeys.InvalidStepInput);
        if(result.Success) StepInput="";
        NotifyDrawingUx();
    }

    [RelayCommand] private void FinishStep() => CompleteCurrentDrawing();
    [RelayCommand] private void CancelStep() => Escape();
    [RelayCommand] private void CycleSnapCandidate() { _objectSnap.Cycle(); OnPropertyChanged(nameof(SnapCandidateDisplay)); RequestOverlayRender(); }

    private void SetSnapMode(CadObjectSnapModes mode,bool enabled) => SetSnapping(CadEditor.Document.ViewSettings.Snap with
    { Modes= enabled ? CadEditor.Document.ViewSettings.Snap.Modes | mode : CadEditor.Document.ViewSettings.Snap.Modes & ~mode });
    public void SetSnapping(CadSnapSettings settings)
    {
        if (!CanEditDocument || settings==CadEditor.Document.ViewSettings.Snap) return;
        CadEditor.DocumentCommands.Execute(new SetSnapSettingsCommand(settings));
        _objectSnap.Clear(); NotifyDrawingUx(); RequestOverlayRender();
    }
    private void NotifyDrawingUx()
    {
        RefreshDynamicInput();
        RefreshDimensionContext();
        foreach (var name in new[]{nameof(IsCurveEditTool),nameof(HasDistanceParameter),nameof(HasChamferParameter),nameof(HasRectArrayParameters),nameof(HasPolarArrayParameters),nameof(HasCornerParameters)}) OnPropertyChanged(name);
        foreach (var name in new[]{nameof(CurrentStepPrompt),nameof(CurrentToolNameDisplay),nameof(HasActiveDrawingTool),nameof(PropertyContext),nameof(CanEditDocument),nameof(IsGridSnapEnabled),nameof(IsObjectSnapEnabled),nameof(IsOrthoEnabled),nameof(IsPolarEnabled),nameof(PolarIncrementDegrees),nameof(IsPerpendicularSnapEnabled),nameof(IsTangentSnapEnabled),nameof(SnapCandidateDisplay),nameof(HasSnapCandidates)}) OnPropertyChanged(name);
    }
}

internal static class CadDrawingPointListExtensions
{
    public static CadPointD? LastOrDefaultNullable(this IReadOnlyList<CadPointD> points) => points.Count==0 ? null : points[^1];
}
