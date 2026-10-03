using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Drawing;

namespace Direct2dCad.ViewModels;

public sealed class CadDynamicInputField : ObservableObject
{
    private readonly Action _changed;
    private string _text = "";
    private bool _isLocked;
    private bool _refreshing;
    public string Key { get; }
    public string Label { get; }
    public string Prefix => Key switch
    {
        "Radius" => "R:", "Diameter" or "Direction" => "D:",
        "Angle" or "StartAngle" => "A:", "Length" => "L:",
        "Width" => "W:", "Height" => "H:",
        "GripScale" => "S:",
        "AxisX" or "EditSpacingX" => "X:", "AxisY" or "EditSpacingY" => "Y:",
        "EditRadius" => "R:", "EditDistance" => "D:", "EditSecondDistance" => "D2:",
        "EditRows" => "R:", "EditColumns" => "C:", "EditCount" => "N:", "EditSweep" => "A:", _ => Key + ":"
    };
    public bool IsAngle => Key is "Angle" or "StartAngle" or "Direction" or "EditSweep";
    public string AutomationId => Key switch
    {
        "EditDistance" or "EditRadius" => "EditDistanceInput", "EditSecondDistance" => "EditSecondDistanceInput",
        "EditRows" => "EditArrayRowsInput", "EditColumns" => "EditArrayColumnsInput",
        "EditSpacingX" => "EditArraySpacingXInput", "EditSpacingY" => "EditArraySpacingYInput",
        "EditCount" => "EditArrayCountInput", "EditSweep" => "EditArraySweepInput", _ => "DynamicInput" + Key
    };
    public bool IsLocked { get => _isLocked; private set => SetProperty(ref _isLocked, value); }
    public string Text
    {
        get => _text;
        set
        {
            SetProperty(ref _text, value);
            if (_refreshing) return;
            IsLocked = true;
            _changed();
        }
    }
    internal CadDynamicInputField(string key, Action changed)
    {
        Key = key;
        Label = Prefix + " " + CadUiText.Get(key switch
        {
            "AxisX" => "RadiusX", "AxisY" => "RadiusY", "Direction" => "Angle",
            "EditDistance" => "DistanceParameter", "EditRadius" => "Radius", "EditSecondDistance" => "SecondDistanceParameter",
            "EditRows" => "ArrayRows", "EditColumns" => "ArrayColumns", "EditSpacingX" => "ArraySpacingX", "EditSpacingY" => "ArraySpacingY",
            "EditCount" => "ArrayCount", "EditSweep" => "ArraySweep", _ => key
        }) + (IsAngle ? " (°)" : "");
        _changed = changed;
    }
    internal void Refresh(double value, int precision, bool reset = false)
    {
        if (IsLocked && !reset) return;
        _refreshing = true;
        try
        {
            if (reset) IsLocked = false;
            var digits = Math.Clamp(precision, 0, 12);
            Text = value.ToString(digits == 0 ? "0" : "0." + new string('#', digits), CultureInfo.CurrentCulture);
        }
        finally { _refreshing = false; }
    }
}

public partial class CadDocumentViewModel
{
    private enum DynamicInputKind { None, Coordinates, Polar, Radius, Diameter, Rectangle, Curve, EditParameters, Grip }
    private (CadCanvasToolMode Mode, DynamicInputKind Kind, CadPointD? Anchor, int Phase, CadUnit Unit, string Culture)? _dynamicInputStep;
    private DynamicInputKind _dynamicInputKind;
    private CadPointD _dynamicInputPointer;
    private CadPointD? _dynamicInputPreview;
    private CadDrawingSessionState? _dynamicInputResolvedState;
    private bool _isDynamicInputInteracting;
    public ObservableCollection<CadDynamicInputField> DynamicInputFields { get; } = [];
    [ObservableProperty] public partial string DynamicInputError { get; private set; } = "";
    public bool HasDynamicInput => CanEditDocument && (_gripDrag.IsActive || HasActiveDrawingTool &&
        CadCanvasToolMode is not (CadCanvasToolMode.InsertBlock or CadCanvasToolMode.LayoutViewport) && !IsPastePreviewActive && !IsBooleanTool &&
        (!IsCurveEditTool || GetEditDynamicInputKeys().Length > 0));
    public string DynamicInputHint => CadUiText.Get("DynamicInputHint");
    public string DynamicInputUnit => CadUnitConversion.GetSymbol(DocumentUnit);
    public (CadPointD? Anchor, CadPointD Point, CadPointD XCorner, CadPointD YCorner) DynamicInputScreenGeometry
    {
        get
        {
            var point = ResolveDynamicInputPreview(_dynamicInputPointer);
            var anchor = DynamicInputAnchor;
            return (anchor is { } origin ? WorldToScreen(origin) : null, WorldToScreen(point),
                WorldToScreen(anchor is { } xOrigin ? new(point.X, xOrigin.Y) : point),
                WorldToScreen(anchor is { } yOrigin ? new(yOrigin.X, point.Y) : point));
        }
    }
    private bool HasLockedDynamicInput => DynamicInputFields.Any(f => f.IsLocked);
    private CadPointD? DynamicInputAnchor => _gripDrag.IsActive ? GripDynamicInputAnchor : CadCanvasToolMode == CadCanvasToolMode.DimAngular && _dimensionAnchors.Count == 2
        ? _dimensionAnchors[0].Point : DrawingAnchor;

    private DynamicInputKind GetDynamicInputKind()
    {
        if (!HasDynamicInput) return DynamicInputKind.None;
        if (_gripDrag.IsActive) return DynamicInputKind.Grip;
        if (IsCurveEditTool) return DynamicInputKind.EditParameters;
        if (GetCurveDynamicInputKeys().Length > 0) return DynamicInputKind.Curve;
        if (IsDimensionTool)
        {
            if (_dimensionAnchors.Count == 0 && CadCanvasToolMode is CadCanvasToolMode.DimRadius or CadCanvasToolMode.DimDiameter)
                return DynamicInputKind.None; // This step picks a source entity, rather than a coordinate.
            return _dimensionAnchors.Count is > 0 && _dimensionAnchors.Count < DimensionPointCount
                ? DynamicInputKind.Polar : DynamicInputKind.Coordinates;
        }
        if (DrawingAnchor is null) return DynamicInputKind.Coordinates;
        return CadCanvasToolMode switch
        {
            CadCanvasToolMode.CircleCenterRadius => DynamicInputKind.Radius,
            CadCanvasToolMode.CircleCenterDiameter => DynamicInputKind.Diameter,
            CadCanvasToolMode.CircleTwoPoint => DynamicInputKind.Diameter,
            CadCanvasToolMode.Rectangle => DynamicInputKind.Rectangle,
            CadCanvasToolMode.Line or CadCanvasToolMode.Polyline or CadCanvasToolMode.Polygon or CadCanvasToolMode.Spline => DynamicInputKind.Polar,
            _ => DynamicInputKind.Coordinates
        };
    }

    private void RefreshDynamicInput()
    {
        var kind = GetDynamicInputKind();
        var step = (CadCanvasToolMode, kind, DynamicInputAnchor,
            _dimensionAnchors.Count + _drawingState.PendingEllipsePoints.Count +
            (_drawingState.PendingArcStartPoint is null ? 0 : 1) + (_drawingState.PendingCircleSecondPoint is null ? 0 : 1),
            DocumentUnit, CultureInfo.CurrentUICulture.Name);
        if (_dynamicInputStep != step)
        {
            _dynamicInputStep = step;
            _dynamicInputKind = kind;
            _dynamicInputPreview = null;
            _dynamicInputResolvedState = null;
            DynamicInputError = "";
            DynamicInputFields.Clear();
            string[] keys = kind switch
            {
                DynamicInputKind.Coordinates => ["X", "Y"],
                DynamicInputKind.Polar => ["Length", "Angle"],
                DynamicInputKind.Radius => ["Radius"],
                DynamicInputKind.Diameter => ["Diameter"],
                DynamicInputKind.Rectangle => ["Width", "Height"],
                DynamicInputKind.Curve => GetCurveDynamicInputKeys(),
                DynamicInputKind.EditParameters => GetEditDynamicInputKeys(),
                DynamicInputKind.Grip => GetGripDynamicInputKeys(),
                _ => []
            };
            foreach (var key in keys) DynamicInputFields.Add(new(key, OnDynamicInputChanged));
            RefreshDynamicInputValues();
        }
        OnPropertyChanged(nameof(HasDynamicInput));
        OnPropertyChanged(nameof(DynamicInputHint));
        OnPropertyChanged(nameof(DynamicInputUnit));
        OnPropertyChanged(nameof(DynamicInputScreenGeometry));
        OnPropertyChanged(nameof(DynamicInputScreenMeasurements));
    }

    private double[] DynamicInputValues()
    {
        var delta = _dynamicInputPointer - (DynamicInputAnchor ?? CadPointD.Origin);
        double Display(double value) => CadUnitConversion.FromMillimeters(value, DocumentUnit);
        return _dynamicInputKind switch
        {
            DynamicInputKind.Coordinates => [Display(_dynamicInputPointer.X), Display(_dynamicInputPointer.Y)],
            DynamicInputKind.Polar => [Display(delta.Length), (Math.Atan2(delta.Y, delta.X) * 180 / Math.PI + 360) % 360],
            DynamicInputKind.Radius or DynamicInputKind.Diameter => [Display(delta.Length)],
            DynamicInputKind.Rectangle => [Display(Math.Abs(delta.X)), Display(Math.Abs(delta.Y))],
            DynamicInputKind.Curve => GetCurveDynamicInputValues(_drawingState, _dynamicInputPointer),
            DynamicInputKind.EditParameters => GetEditDynamicInputValues(),
            DynamicInputKind.Grip => GetGripDynamicInputValues(_dynamicInputPointer),
            _ => []
        };
    }
    private void RefreshDynamicInputValues(bool reset = false)
    {
        // Rendering can run while Escape or an entity command is clearing a drawing step.
        if (_dynamicInputKind != GetDynamicInputKind()) { RefreshDynamicInput(); return; }
        var values = _dynamicInputKind == DynamicInputKind.Curve && !reset && HasLockedDynamicInput && _dynamicInputPreview is { } point
            ? GetCurveDynamicInputValues(_dynamicInputResolvedState ?? _drawingState, point) : DynamicInputValues();
        for (var i = 0; i < DynamicInputFields.Count; i++)
            DynamicInputFields[i].Refresh(values[i], DynamicInputFields[i].IsAngle ? DocumentAnglePrecision : DocumentLengthPrecision, reset);
    }
    private void UpdateDynamicInputPointer(CadPointD point)
    {
        var moved = _dynamicInputPointer != point;
        _dynamicInputPointer = point;
        UpdateOffsetDistanceFromPointer(point);
        UpdateEditCornerPreviewTarget(point);
        if (HasLockedDynamicInput && TryGetDynamicInputPoint(out var resolved)) _dynamicInputPreview = resolved;
        RefreshDynamicInputValues();
        if (moved)
        {
            OnPropertyChanged(nameof(DynamicInputScreenGeometry));
            OnPropertyChanged(nameof(DynamicInputScreenMeasurements));
        }
    }
    private void OnDynamicInputChanged()
    {
        if (_dynamicInputKind == DynamicInputKind.EditParameters)
        {
            DynamicInputError = TryApplyEditDynamicInput() ? "" : EditFeedback;
            PublishCurrentStepPrompt();
            OnPropertyChanged(nameof(DynamicInputScreenGeometry));
            RequestOverlayRender();
            return;
        }
        if (TryGetDynamicInputPoint(out var point))
        {
            _dynamicInputPreview = point;
            ApplyGripDynamicInput(point);
            if (_gripDrag.IsActive) StepInputError="";
            DynamicInputError = "";
            RefreshDynamicInputValues();
        }
        else DynamicInputError = CadUiText.Get("InvalidDynamicInput");
        OnPropertyChanged(nameof(DynamicInputScreenGeometry));
        OnPropertyChanged(nameof(DynamicInputScreenMeasurements));
        RequestOverlayRender(updateHandleScene: _gripDrag.IsActive);
    }
    private bool TryGetDynamicInputPoint(out CadPointD point)
    {
        point = _dynamicInputPointer;
        if (_dynamicInputKind == DynamicInputKind.None || _dynamicInputKind != GetDynamicInputKind()) return false;
        if (_dynamicInputKind == DynamicInputKind.EditParameters) return EditParametersValid;
        if (!HasLockedDynamicInput) { _dynamicInputResolvedState = null; return true; }
        var values = DynamicInputValues();
        for (var i = 0; i < values.Length; i++)
        {
            var field = DynamicInputFields[i];
            if (!field.IsLocked) continue;
            if (!(double.TryParse(field.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out values[i]) ||
                double.TryParse(field.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) || !double.IsFinite(values[i]))
                return false;
        }
        double World(double value) => CadUnitConversion.ToMillimeters(value, DocumentUnit);
        var anchor = DynamicInputAnchor ?? CadPointD.Origin;
        var delta = _dynamicInputPointer - anchor;
        switch (_dynamicInputKind)
        {
            case DynamicInputKind.Coordinates:
                point = new(World(values[0]), World(values[1]));
                break;
            case DynamicInputKind.Polar:
                if (values[0] <= 0) return false;
                var radians = values[1] % 360 * Math.PI / 180;
                point = anchor + new CadVectorD(Math.Cos(radians), Math.Sin(radians)) * World(values[0]);
                break;
            case DynamicInputKind.Radius:
            case DynamicInputKind.Diameter:
                if (values[0] <= 0) return false;
                var direction = delta.Length > 1e-9 ? delta.Normalize() : CadVectorD.UnitX;
                point = anchor + direction * World(values[0]);
                break;
            case DynamicInputKind.Rectangle:
                if (values[0] <= 0 || values[1] <= 0) return false;
                point = anchor + new CadVectorD((delta.X < 0 ? -1 : 1) * World(values[0]), (delta.Y < 0 ? -1 : 1) * World(values[1]));
                break;
            case DynamicInputKind.Curve:
                if (!TryResolveCurveDynamicInput(values, out point, out var resolvedState)) return false;
                _dynamicInputResolvedState = resolvedState;
                break;
            case DynamicInputKind.Grip:
                if (!TryResolveGripDynamicInput(values, out point)) return false;
                break;
        }
        return double.IsFinite(point.X) && double.IsFinite(point.Y);
    }
    private CadPointD ResolveDynamicInputPreview(CadPointD pointer)
    {
        if (!HasDynamicInput || !HasLockedDynamicInput) return pointer;
        if (TryGetDynamicInputPoint(out var point)) _dynamicInputPreview = point;
        return _dynamicInputPreview ?? pointer;
    }
    public void ClearDynamicInputLocks()
    {
        _dynamicInputPreview = null;
        _dynamicInputResolvedState = null;
        DynamicInputError = "";
        _offsetDistanceLockedByParameter = false;
        if (IsCurveEditTool) SetEditParameterInputValid(true);
        RefreshDynamicInputValues(reset: true);
        UpdateOffsetDistanceFromPointer(_dynamicInputPointer);
        RefreshDynamicInputValues();
        ApplyGripDynamicInput(_dynamicInputPointer);
        NotifyEditUx();
        PublishCurrentStepPrompt();
        OnPropertyChanged(nameof(DynamicInputScreenGeometry));
        OnPropertyChanged(nameof(DynamicInputScreenMeasurements));
    }
    public bool SubmitDynamicInput()
    {
        if (_gripDrag.IsActive)
        {
            if (!TryGetDynamicInputPoint(out var gripPoint)) { DynamicInputError=CadUiText.Get("InvalidDynamicInput"); return false; }
            ApplyGripDynamicInput(gripPoint);
            CommitGripDrag(WorldToScreen(_gripDrag.ActiveDrag!.CurrentPointerWorld), preserveNumericPoint: true);
            return true;
        }
        // Enter accepts modification parameters; source/center picking and committing remain canvas steps.
        if (IsCurveEditTool)
        {
            DynamicInputError = EditParametersValid ? "" : EditFeedback;
            return EditParametersValid;
        }
        if (!HasDynamicInput || !TryGetDynamicInputPoint(out var point))
        { DynamicInputError = CadUiText.Get("InvalidDynamicInput"); return false; }
        var originalState = _dynamicInputResolvedState is not null ? _drawingState.Clone() : null;
        if (_dynamicInputResolvedState is not null) _drawingState.CopyFrom(_dynamicInputResolvedState);
        var accepted = HandleDrawingWorldPoint(point);
        if (!accepted && originalState is not null) { _drawingState.CopyFrom(originalState); RefreshDynamicInput(); }
        DynamicInputError = accepted ? "" : StepInputError;
        return accepted;
    }

    public void SetDynamicInputInteraction(bool active)
    {
        if (_isDynamicInputInteracting == active) return;
        _isDynamicInputInteracting = active;
        RequestOverlayRender();
    }
}
