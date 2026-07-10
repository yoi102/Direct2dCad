using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.ViewModels.Services.Drawing;

internal sealed class CadDrawingDefaultChangedEventArgs(
    string propertyName,
    bool requestRender)
    : EventArgs
{
    public string PropertyName { get; } = propertyName;
    public bool RequestRender { get; } = requestRender;
}

internal sealed class CadDrawingDefaults : ObservableObject
{
    private CadColor _lineStrokeColor = CadColor.White;
    private double _lineLineWeight = CadLineWeight.Default.Value;
    private int _lineZIndex;
    private bool _lineIsVisible = true;
    private CadColor _polylineStrokeColor = CadColor.White;
    private double _polylineLineWeight = CadLineWeight.Default.Value;
    private int _polylineZIndex;
    private bool _polylineIsVisible = true;
    private bool _polylineClosed;
    private StyleId? _polylineFillStyleId;
    private CadColor _polygonStrokeColor = CadColor.White;
    private double _polygonLineWeight = CadLineWeight.Default.Value;
    private int _polygonZIndex;
    private bool _polygonIsVisible = true;
    private StyleId? _polygonFillStyleId;
    private CadColor _splineStrokeColor = CadColor.White;
    private double _splineLineWeight = CadLineWeight.Default.Value;
    private int _splineZIndex;
    private bool _splineIsVisible = true;
    private bool _splineClosed;
    private StyleId? _splineFillStyleId;
    private CadColor _circleStrokeColor = CadColor.White;
    private double _circleLineWeight = CadLineWeight.Default.Value;
    private int _circleZIndex;
    private bool _circleIsVisible = true;
    private StyleId? _circleFillStyleId;
    private CadColor _ellipseStrokeColor = CadColor.White;
    private double _ellipseLineWeight = CadLineWeight.Default.Value;
    private int _ellipseZIndex;
    private bool _ellipseIsVisible = true;
    private StyleId? _ellipseFillStyleId;
    private CadColor _rectangleStrokeColor = CadColor.White;
    private double _rectangleLineWeight = CadLineWeight.Default.Value;
    private int _rectangleZIndex;
    private bool _rectangleIsVisible = true;
    private StyleId? _rectangleFillStyleId;
    private double _rectangleCornerRadiusX;
    private double _rectangleCornerRadiusY;
    private string _text = "Text";
    private bool _textInverted;
    private double _textInvertedMarginFactor = CadText.DefaultInvertedMarginFactor;
    private CadColor _textStrokeColor = CadColor.White;
    private double _textLineWeight = CadLineWeight.Default.Value;
    private int _textZIndex;
    private bool _textIsVisible = true;
    private StyleId? _textStyleId;
    private CadColor _arcStrokeColor = CadColor.White;
    private double _arcLineWeight = CadLineWeight.Default.Value;
    private int _arcZIndex;
    private bool _arcIsVisible = true;

    public event EventHandler<CadDrawingDefaultChangedEventArgs>? SettingChanged;

    public CadColor LineStrokeColor
    {
        get => _lineStrokeColor;
        set => SetDrawingSetting(ref _lineStrokeColor, value);
    }

    public double LineLineWeight
    {
        get => _lineLineWeight;
        set => SetDrawingSetting(ref _lineLineWeight, value, IsFinitePositive(value));
    }

    public int LineZIndex
    {
        get => _lineZIndex;
        set => SetDrawingSetting(ref _lineZIndex, value);
    }

    public bool LineIsVisible
    {
        get => _lineIsVisible;
        set => SetDrawingSetting(ref _lineIsVisible, value);
    }

    public CadColor PolylineStrokeColor
    {
        get => _polylineStrokeColor;
        set => SetDrawingSetting(ref _polylineStrokeColor, value);
    }

    public double PolylineLineWeight
    {
        get => _polylineLineWeight;
        set => SetDrawingSetting(ref _polylineLineWeight, value, IsFinitePositive(value));
    }

    public int PolylineZIndex
    {
        get => _polylineZIndex;
        set => SetDrawingSetting(ref _polylineZIndex, value);
    }

    public bool PolylineIsVisible
    {
        get => _polylineIsVisible;
        set => SetDrawingSetting(ref _polylineIsVisible, value);
    }

    public bool PolylineClosed
    {
        get => _polylineClosed;
        set => SetDrawingSetting(ref _polylineClosed, value);
    }

    public StyleId? PolylineFillStyleId
    {
        get => _polylineFillStyleId;
        set => SetDrawingSetting(ref _polylineFillStyleId, value);
    }

    public CadColor PolygonStrokeColor
    {
        get => _polygonStrokeColor;
        set => SetDrawingSetting(ref _polygonStrokeColor, value);
    }

    public double PolygonLineWeight
    {
        get => _polygonLineWeight;
        set => SetDrawingSetting(ref _polygonLineWeight, value, IsFinitePositive(value));
    }

    public int PolygonZIndex
    {
        get => _polygonZIndex;
        set => SetDrawingSetting(ref _polygonZIndex, value);
    }

    public bool PolygonIsVisible
    {
        get => _polygonIsVisible;
        set => SetDrawingSetting(ref _polygonIsVisible, value);
    }

    public StyleId? PolygonFillStyleId
    {
        get => _polygonFillStyleId;
        set => SetDrawingSetting(ref _polygonFillStyleId, value);
    }

    public CadColor SplineStrokeColor
    {
        get => _splineStrokeColor;
        set => SetDrawingSetting(ref _splineStrokeColor, value);
    }

    public double SplineLineWeight
    {
        get => _splineLineWeight;
        set => SetDrawingSetting(ref _splineLineWeight, value, IsFinitePositive(value));
    }

    public int SplineZIndex
    {
        get => _splineZIndex;
        set => SetDrawingSetting(ref _splineZIndex, value);
    }

    public bool SplineIsVisible
    {
        get => _splineIsVisible;
        set => SetDrawingSetting(ref _splineIsVisible, value);
    }

    public bool SplineClosed
    {
        get => _splineClosed;
        set => SetDrawingSetting(ref _splineClosed, value);
    }

    public StyleId? SplineFillStyleId
    {
        get => _splineFillStyleId;
        set => SetDrawingSetting(ref _splineFillStyleId, value);
    }

    public CadColor CircleStrokeColor
    {
        get => _circleStrokeColor;
        set => SetDrawingSetting(ref _circleStrokeColor, value);
    }

    public double CircleLineWeight
    {
        get => _circleLineWeight;
        set => SetDrawingSetting(ref _circleLineWeight, value, IsFinitePositive(value));
    }

    public int CircleZIndex
    {
        get => _circleZIndex;
        set => SetDrawingSetting(ref _circleZIndex, value);
    }

    public bool CircleIsVisible
    {
        get => _circleIsVisible;
        set => SetDrawingSetting(ref _circleIsVisible, value);
    }

    public StyleId? CircleFillStyleId
    {
        get => _circleFillStyleId;
        set => SetDrawingSetting(ref _circleFillStyleId, value);
    }

    public CadColor EllipseStrokeColor
    {
        get => _ellipseStrokeColor;
        set => SetDrawingSetting(ref _ellipseStrokeColor, value);
    }

    public double EllipseLineWeight
    {
        get => _ellipseLineWeight;
        set => SetDrawingSetting(ref _ellipseLineWeight, value, IsFinitePositive(value));
    }

    public int EllipseZIndex
    {
        get => _ellipseZIndex;
        set => SetDrawingSetting(ref _ellipseZIndex, value);
    }

    public bool EllipseIsVisible
    {
        get => _ellipseIsVisible;
        set => SetDrawingSetting(ref _ellipseIsVisible, value);
    }

    public StyleId? EllipseFillStyleId
    {
        get => _ellipseFillStyleId;
        set => SetDrawingSetting(ref _ellipseFillStyleId, value);
    }

    public CadColor RectangleStrokeColor
    {
        get => _rectangleStrokeColor;
        set => SetDrawingSetting(ref _rectangleStrokeColor, value);
    }

    public double RectangleLineWeight
    {
        get => _rectangleLineWeight;
        set => SetDrawingSetting(ref _rectangleLineWeight, value, IsFinitePositive(value));
    }

    public int RectangleZIndex
    {
        get => _rectangleZIndex;
        set => SetDrawingSetting(ref _rectangleZIndex, value);
    }

    public bool RectangleIsVisible
    {
        get => _rectangleIsVisible;
        set => SetDrawingSetting(ref _rectangleIsVisible, value);
    }

    public StyleId? RectangleFillStyleId
    {
        get => _rectangleFillStyleId;
        set => SetDrawingSetting(ref _rectangleFillStyleId, value);
    }

    public double RectangleCornerRadiusX
    {
        get => _rectangleCornerRadiusX;
        set => SetDrawingSetting(ref _rectangleCornerRadiusX, value);
    }

    public double RectangleCornerRadiusY
    {
        get => _rectangleCornerRadiusY;
        set => SetDrawingSetting(ref _rectangleCornerRadiusY, value);
    }

    public string Text
    {
        get => _text;
        set => SetDrawingSetting(ref _text, value);
    }

    public bool TextInverted
    {
        get => _textInverted;
        set => SetDrawingSetting(ref _textInverted, value);
    }

    public double TextInvertedMarginFactor
    {
        get => _textInvertedMarginFactor;
        set => SetDrawingSetting(ref _textInvertedMarginFactor, value);
    }

    public CadColor TextStrokeColor
    {
        get => _textStrokeColor;
        set => SetDrawingSetting(ref _textStrokeColor, value);
    }

    public double TextLineWeight
    {
        get => _textLineWeight;
        set => SetDrawingSetting(ref _textLineWeight, value, IsFinitePositive(value));
    }

    public int TextZIndex
    {
        get => _textZIndex;
        set => SetDrawingSetting(ref _textZIndex, value);
    }

    public bool TextIsVisible
    {
        get => _textIsVisible;
        set => SetDrawingSetting(ref _textIsVisible, value);
    }

    public StyleId? TextStyleId
    {
        get => _textStyleId;
        set => SetDrawingSetting(ref _textStyleId, value);
    }

    public CadColor ArcStrokeColor
    {
        get => _arcStrokeColor;
        set => SetDrawingSetting(ref _arcStrokeColor, value);
    }

    public double ArcLineWeight
    {
        get => _arcLineWeight;
        set => SetDrawingSetting(ref _arcLineWeight, value, IsFinitePositive(value));
    }

    public int ArcZIndex
    {
        get => _arcZIndex;
        set => SetDrawingSetting(ref _arcZIndex, value);
    }

    public bool ArcIsVisible
    {
        get => _arcIsVisible;
        set => SetDrawingSetting(ref _arcIsVisible, value);
    }

    public void UpdateStrokeColors(CadColor previousColor, CadColor newColor)
    {
        if (LineStrokeColor == previousColor) LineStrokeColor = newColor;
        if (PolylineStrokeColor == previousColor) PolylineStrokeColor = newColor;
        if (PolygonStrokeColor == previousColor) PolygonStrokeColor = newColor;
        if (SplineStrokeColor == previousColor) SplineStrokeColor = newColor;
        if (CircleStrokeColor == previousColor) CircleStrokeColor = newColor;
        if (EllipseStrokeColor == previousColor) EllipseStrokeColor = newColor;
        if (RectangleStrokeColor == previousColor) RectangleStrokeColor = newColor;
        if (TextStrokeColor == previousColor) TextStrokeColor = newColor;
        if (ArcStrokeColor == previousColor) ArcStrokeColor = newColor;
    }

    public void UpdateLineWeights(double previousLineWeight, double newLineWeight)
    {
        if (AreClose(LineLineWeight, previousLineWeight)) LineLineWeight = newLineWeight;
        if (AreClose(PolylineLineWeight, previousLineWeight)) PolylineLineWeight = newLineWeight;
        if (AreClose(PolygonLineWeight, previousLineWeight)) PolygonLineWeight = newLineWeight;
        if (AreClose(SplineLineWeight, previousLineWeight)) SplineLineWeight = newLineWeight;
        if (AreClose(CircleLineWeight, previousLineWeight)) CircleLineWeight = newLineWeight;
        if (AreClose(EllipseLineWeight, previousLineWeight)) EllipseLineWeight = newLineWeight;
        if (AreClose(RectangleLineWeight, previousLineWeight)) RectangleLineWeight = newLineWeight;
        if (AreClose(TextLineWeight, previousLineWeight)) TextLineWeight = newLineWeight;
        if (AreClose(ArcLineWeight, previousLineWeight)) ArcLineWeight = newLineWeight;
    }

    private bool SetDrawingSetting<T>(
        ref T field,
        T value,
        bool requestRender = true,
        [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
            return false;

        SettingChanged?.Invoke(
            this,
            new CadDrawingDefaultChangedEventArgs(propertyName ?? string.Empty, requestRender));
        return true;
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) <= 1e-9;
    }
}
