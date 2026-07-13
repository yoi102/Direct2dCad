using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.ViewModels.Services.Drawing;

namespace Direct2dCad.ViewModels.Drawing;

public sealed class CadDrawingDefaultsViewModel : ObservableObject, ICadDrawingDefaults
{
    private int _updateDepth;
    private bool _defaultsChanged;
    private CadColor _lineStrokeColor = CadColor.Green;
    private bool _lineUseLayerColor = true;
    private double _lineLineWeight = CadLineWeight.Default.Value;
    private bool _lineUseLayerLineWeight = true;
    private int _lineZIndex;
    private bool _lineIsVisible = true;
    private CadColor _polylineStrokeColor = CadColor.Green;
    private bool _polylineUseLayerColor = true;
    private double _polylineLineWeight = CadLineWeight.Default.Value;
    private bool _polylineUseLayerLineWeight = true;
    private int _polylineZIndex;
    private bool _polylineIsVisible = true;
    private bool _polylineClosed;
    private StyleId? _polylineFillStyleId;
    private CadColor _polygonStrokeColor = CadColor.Green;
    private bool _polygonUseLayerColor = true;
    private double _polygonLineWeight = CadLineWeight.Default.Value;
    private bool _polygonUseLayerLineWeight = true;
    private int _polygonZIndex;
    private bool _polygonIsVisible = true;
    private StyleId? _polygonFillStyleId;
    private CadColor _splineStrokeColor = CadColor.Green;
    private bool _splineUseLayerColor = true;
    private double _splineLineWeight = CadLineWeight.Default.Value;
    private bool _splineUseLayerLineWeight = true;
    private int _splineZIndex;
    private bool _splineIsVisible = true;
    private bool _splineClosed;
    private StyleId? _splineFillStyleId;
    private CadColor _circleStrokeColor = CadColor.Green;
    private bool _circleUseLayerColor = true;
    private double _circleLineWeight = CadLineWeight.Default.Value;
    private bool _circleUseLayerLineWeight = true;
    private int _circleZIndex;
    private bool _circleIsVisible = true;
    private StyleId? _circleFillStyleId;
    private CadColor _ellipseStrokeColor = CadColor.Green;
    private bool _ellipseUseLayerColor = true;
    private double _ellipseLineWeight = CadLineWeight.Default.Value;
    private bool _ellipseUseLayerLineWeight = true;
    private int _ellipseZIndex;
    private bool _ellipseIsVisible = true;
    private StyleId? _ellipseFillStyleId;
    private CadColor _rectangleStrokeColor = CadColor.Green;
    private bool _rectangleUseLayerColor = true;
    private double _rectangleLineWeight = CadLineWeight.Default.Value;
    private bool _rectangleUseLayerLineWeight = true;
    private int _rectangleZIndex;
    private bool _rectangleIsVisible = true;
    private StyleId? _rectangleFillStyleId;
    private double _rectangleCornerRadiusX;
    private double _rectangleCornerRadiusY;
    private string _text = "Text";
    private bool _textInverted;
    private double _textInvertedMarginFactor = CadText.DefaultInvertedMarginFactor;
    private CadColor _textStrokeColor = CadColor.Green;
    private bool _textUseLayerColor = true;
    private double _textLineWeight = CadLineWeight.Default.Value;
    private bool _textUseLayerLineWeight = true;
    private int _textZIndex;
    private bool _textIsVisible = true;
    private StyleId? _textStyleId;
    private CadColor _arcStrokeColor = CadColor.Green;
    private bool _arcUseLayerColor = true;
    private double _arcLineWeight = CadLineWeight.Default.Value;
    private bool _arcUseLayerLineWeight = true;
    private int _arcZIndex;
    private bool _arcIsVisible = true;

    public event EventHandler? DefaultsChanged;

    public CadColor LineStrokeColor
    {
        get => _lineStrokeColor;
        set => SetDrawingSetting(ref _lineStrokeColor, value);
    }

    public bool LineUseLayerColor
    {
        get => _lineUseLayerColor;
        set => SetDrawingSetting(ref _lineUseLayerColor, value);
    }

    public double LineLineWeight
    {
        get => _lineLineWeight;
        set => SetDrawingSetting(ref _lineLineWeight, value, IsFinitePositive(value));
    }

    public bool LineUseLayerLineWeight
    {
        get => _lineUseLayerLineWeight;
        set => SetDrawingSetting(ref _lineUseLayerLineWeight, value);
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

    public bool PolylineUseLayerColor
    {
        get => _polylineUseLayerColor;
        set => SetDrawingSetting(ref _polylineUseLayerColor, value);
    }

    public double PolylineLineWeight
    {
        get => _polylineLineWeight;
        set => SetDrawingSetting(ref _polylineLineWeight, value, IsFinitePositive(value));
    }

    public bool PolylineUseLayerLineWeight
    {
        get => _polylineUseLayerLineWeight;
        set => SetDrawingSetting(ref _polylineUseLayerLineWeight, value);
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

    public bool PolygonUseLayerColor
    {
        get => _polygonUseLayerColor;
        set => SetDrawingSetting(ref _polygonUseLayerColor, value);
    }

    public double PolygonLineWeight
    {
        get => _polygonLineWeight;
        set => SetDrawingSetting(ref _polygonLineWeight, value, IsFinitePositive(value));
    }

    public bool PolygonUseLayerLineWeight
    {
        get => _polygonUseLayerLineWeight;
        set => SetDrawingSetting(ref _polygonUseLayerLineWeight, value);
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

    public bool SplineUseLayerColor
    {
        get => _splineUseLayerColor;
        set => SetDrawingSetting(ref _splineUseLayerColor, value);
    }

    public double SplineLineWeight
    {
        get => _splineLineWeight;
        set => SetDrawingSetting(ref _splineLineWeight, value, IsFinitePositive(value));
    }

    public bool SplineUseLayerLineWeight
    {
        get => _splineUseLayerLineWeight;
        set => SetDrawingSetting(ref _splineUseLayerLineWeight, value);
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

    public bool CircleUseLayerColor
    {
        get => _circleUseLayerColor;
        set => SetDrawingSetting(ref _circleUseLayerColor, value);
    }

    public double CircleLineWeight
    {
        get => _circleLineWeight;
        set => SetDrawingSetting(ref _circleLineWeight, value, IsFinitePositive(value));
    }

    public bool CircleUseLayerLineWeight
    {
        get => _circleUseLayerLineWeight;
        set => SetDrawingSetting(ref _circleUseLayerLineWeight, value);
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

    public bool EllipseUseLayerColor
    {
        get => _ellipseUseLayerColor;
        set => SetDrawingSetting(ref _ellipseUseLayerColor, value);
    }

    public double EllipseLineWeight
    {
        get => _ellipseLineWeight;
        set => SetDrawingSetting(ref _ellipseLineWeight, value, IsFinitePositive(value));
    }

    public bool EllipseUseLayerLineWeight
    {
        get => _ellipseUseLayerLineWeight;
        set => SetDrawingSetting(ref _ellipseUseLayerLineWeight, value);
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

    public bool RectangleUseLayerColor
    {
        get => _rectangleUseLayerColor;
        set => SetDrawingSetting(ref _rectangleUseLayerColor, value);
    }

    public double RectangleLineWeight
    {
        get => _rectangleLineWeight;
        set => SetDrawingSetting(ref _rectangleLineWeight, value, IsFinitePositive(value));
    }

    public bool RectangleUseLayerLineWeight
    {
        get => _rectangleUseLayerLineWeight;
        set => SetDrawingSetting(ref _rectangleUseLayerLineWeight, value);
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
        set => SetDrawingSetting(ref _rectangleCornerRadiusX, value, IsFiniteNonNegative(value));
    }

    public double RectangleCornerRadiusY
    {
        get => _rectangleCornerRadiusY;
        set => SetDrawingSetting(ref _rectangleCornerRadiusY, value, IsFiniteNonNegative(value));
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
        set => SetDrawingSetting(ref _textInvertedMarginFactor, value, IsFiniteNonNegative(value));
    }

    public CadColor TextStrokeColor
    {
        get => _textStrokeColor;
        set => SetDrawingSetting(ref _textStrokeColor, value);
    }

    public bool TextUseLayerColor
    {
        get => _textUseLayerColor;
        set => SetDrawingSetting(ref _textUseLayerColor, value);
    }

    public double TextLineWeight
    {
        get => _textLineWeight;
        set => SetDrawingSetting(ref _textLineWeight, value, IsFinitePositive(value));
    }

    public bool TextUseLayerLineWeight
    {
        get => _textUseLayerLineWeight;
        set => SetDrawingSetting(ref _textUseLayerLineWeight, value);
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

    public bool ArcUseLayerColor
    {
        get => _arcUseLayerColor;
        set => SetDrawingSetting(ref _arcUseLayerColor, value);
    }

    public double ArcLineWeight
    {
        get => _arcLineWeight;
        set => SetDrawingSetting(ref _arcLineWeight, value, IsFinitePositive(value));
    }

    public bool ArcUseLayerLineWeight
    {
        get => _arcUseLayerLineWeight;
        set => SetDrawingSetting(ref _arcUseLayerLineWeight, value);
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

    private void UpdateStrokeColors(CadColor newColor)
    {
        BeginUpdate();
        try
        {
            if (LineUseLayerColor) LineStrokeColor = newColor;
            if (PolylineUseLayerColor) PolylineStrokeColor = newColor;
            if (PolygonUseLayerColor) PolygonStrokeColor = newColor;
            if (SplineUseLayerColor) SplineStrokeColor = newColor;
            if (CircleUseLayerColor) CircleStrokeColor = newColor;
            if (EllipseUseLayerColor) EllipseStrokeColor = newColor;
            if (RectangleUseLayerColor) RectangleStrokeColor = newColor;
            if (TextUseLayerColor) TextStrokeColor = newColor;
            if (ArcUseLayerColor) ArcStrokeColor = newColor;
        }
        finally
        {
            EndUpdate();
        }
    }

    private void UpdateLineWeights(double newLineWeight)
    {
        BeginUpdate();
        try
        {
            if (LineUseLayerLineWeight) LineLineWeight = newLineWeight;
            if (PolylineUseLayerLineWeight) PolylineLineWeight = newLineWeight;
            if (PolygonUseLayerLineWeight) PolygonLineWeight = newLineWeight;
            if (SplineUseLayerLineWeight) SplineLineWeight = newLineWeight;
            if (CircleUseLayerLineWeight) CircleLineWeight = newLineWeight;
            if (EllipseUseLayerLineWeight) EllipseLineWeight = newLineWeight;
            if (RectangleUseLayerLineWeight) RectangleLineWeight = newLineWeight;
            if (TextUseLayerLineWeight) TextLineWeight = newLineWeight;
            if (ArcUseLayerLineWeight) ArcLineWeight = newLineWeight;
        }
        finally
        {
            EndUpdate();
        }
    }

    public void UpdateLayerDefaults(CadColor newColor, double newLineWeight)
    {
        BeginUpdate();
        try
        {
            UpdateStrokeColors(newColor);
            UpdateLineWeights(newLineWeight);
        }
        finally
        {
            EndUpdate();
        }
    }

    private bool SetDrawingSetting<T>(
        ref T field,
        T value,
        bool isValid = true,
        [CallerMemberName] string? propertyName = null)
    {
        if (!isValid)
            return false;

        if (!SetProperty(ref field, value, propertyName))
            return false;

        _defaultsChanged = true;
        if (_updateDepth == 0)
            PublishDefaultsChanged();
        return true;
    }

    private void BeginUpdate() => _updateDepth++;

    private void EndUpdate()
    {
        if (_updateDepth <= 0)
            throw new InvalidOperationException("Drawing defaults update scope is unbalanced.");

        _updateDepth--;
        if (_updateDepth == 0 && _defaultsChanged)
            PublishDefaultsChanged();
    }

    private void PublishDefaultsChanged()
    {
        _defaultsChanged = false;
        DefaultsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private static bool IsFiniteNonNegative(double value)
    {
        return value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

}
