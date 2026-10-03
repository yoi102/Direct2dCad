using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Lang;
using Direct2dCad.ViewModels.Enums;

namespace Direct2dCad.ViewModels;

public sealed class CadDynamicInputField : ObservableObject
{
    private readonly Action _changed;
    private string _text = "";
    private bool _isLocked;
    private bool _refreshing;
    public string Key { get; }
    public string Label { get; }
    public string AutomationId => "DynamicInput" + Key;
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
    { Key = key; Label = CadUiText.Get(key) + (key == "Angle" ? " (°)" : ""); _changed = changed; }
    internal void Refresh(double value, bool reset = false)
    {
        if (IsLocked && !reset) return;
        _refreshing = true;
        try
        {
            if (reset) IsLocked = false;
            Text = value.ToString("G9", CultureInfo.CurrentCulture);
        }
        finally { _refreshing = false; }
    }
}

public partial class CadDocumentViewModel
{
    private enum DynamicInputKind { None, Coordinates, Polar, Radius, Diameter, Rectangle }
    private (CadCanvasToolMode Mode, DynamicInputKind Kind, CadPointD? Anchor, int Phase, CadUnit Unit, string Culture)? _dynamicInputStep;
    private DynamicInputKind _dynamicInputKind;
    private CadPointD _dynamicInputPointer;
    private CadPointD? _dynamicInputPreview;
    public ObservableCollection<CadDynamicInputField> DynamicInputFields { get; } = [];
    [ObservableProperty] public partial string DynamicInputError { get; private set; } = "";
    public bool HasDynamicInput => CanEditDocument && HasActiveDrawingTool &&
        CadCanvasToolMode is not (CadCanvasToolMode.InsertBlock or CadCanvasToolMode.LayoutViewport) && !IsPastePreviewActive && !IsBooleanTool;
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
    private CadPointD? DynamicInputAnchor => CadCanvasToolMode == CadCanvasToolMode.DimAngular && _dimensionAnchors.Count == 2
        ? _dimensionAnchors[0].Point : DrawingAnchor;

    private DynamicInputKind GetDynamicInputKind()
    {
        if (!HasDynamicInput) return DynamicInputKind.None;
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
            CadCanvasToolMode.Rectangle => DynamicInputKind.Rectangle,
            CadCanvasToolMode.Line or CadCanvasToolMode.Polyline or CadCanvasToolMode.Polygon or CadCanvasToolMode.Spline => DynamicInputKind.Polar,
            _ => DynamicInputKind.Coordinates
        };
    }

    private void RefreshDynamicInput()
    {
        var kind = GetDynamicInputKind();
        var step = (CadCanvasToolMode, kind, DynamicInputAnchor,
            _dimensionAnchors.Count + _drawingState.PendingEllipsePoints.Count, DocumentUnit, CultureInfo.CurrentUICulture.Name);
        if (_dynamicInputStep != step)
        {
            _dynamicInputStep = step;
            _dynamicInputKind = kind;
            _dynamicInputPreview = null;
            DynamicInputError = "";
            DynamicInputFields.Clear();
            string[] keys = kind switch
            {
                DynamicInputKind.Coordinates => ["X", "Y"],
                DynamicInputKind.Polar => ["Length", "Angle"],
                DynamicInputKind.Radius => ["Radius"],
                DynamicInputKind.Diameter => ["Diameter"],
                DynamicInputKind.Rectangle => ["Width", "Height"],
                _ => []
            };
            foreach (var key in keys) DynamicInputFields.Add(new(key, OnDynamicInputChanged));
            RefreshDynamicInputValues();
        }
        OnPropertyChanged(nameof(HasDynamicInput));
        OnPropertyChanged(nameof(DynamicInputHint));
        OnPropertyChanged(nameof(DynamicInputUnit));
        OnPropertyChanged(nameof(DynamicInputScreenGeometry));
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
            _ => []
        };
    }
    private void RefreshDynamicInputValues(bool reset = false)
    {
        var values = DynamicInputValues();
        for (var i = 0; i < DynamicInputFields.Count; i++) DynamicInputFields[i].Refresh(values[i], reset);
    }
    private void UpdateDynamicInputPointer(CadPointD point)
    {
        var moved = _dynamicInputPointer != point;
        _dynamicInputPointer = point;
        RefreshDynamicInputValues();
        if (moved) OnPropertyChanged(nameof(DynamicInputScreenGeometry));
    }
    private void OnDynamicInputChanged()
    {
        if (TryGetDynamicInputPoint(out var point))
        {
            _dynamicInputPreview = point;
            DynamicInputError = "";
        }
        else DynamicInputError = CadUiText.Get("InvalidStepInput");
        OnPropertyChanged(nameof(DynamicInputScreenGeometry));
        RequestOverlayRender();
    }
    private bool TryGetDynamicInputPoint(out CadPointD point)
    {
        point = _dynamicInputPointer;
        if (_dynamicInputKind == DynamicInputKind.None) return false;
        if (!HasLockedDynamicInput) return true;
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
        DynamicInputError = "";
        RefreshDynamicInputValues(reset: true);
        OnPropertyChanged(nameof(DynamicInputScreenGeometry));
    }
    public bool SubmitDynamicInput()
    {
        if (!HasDynamicInput || !TryGetDynamicInputPoint(out var point))
        { DynamicInputError = CadUiText.Get("InvalidStepInput"); return false; }
        var accepted = HandleDrawingWorldPoint(point);
        DynamicInputError = accepted ? "" : StepInputError;
        return accepted;
    }
}
