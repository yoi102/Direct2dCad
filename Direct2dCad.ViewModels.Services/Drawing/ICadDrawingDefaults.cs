using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.ViewModels.Services.Drawing;

public interface ICadDrawingDefaults
{
    CadColor LineStrokeColor { get; }
    double LineLineWeight { get; }
    int LineZIndex { get; }
    bool LineIsVisible { get; }
    CadColor PolylineStrokeColor { get; }
    double PolylineLineWeight { get; }
    int PolylineZIndex { get; }
    bool PolylineIsVisible { get; }
    bool PolylineClosed { get; }
    StyleId? PolylineFillStyleId { get; }
    CadColor PolygonStrokeColor { get; }
    double PolygonLineWeight { get; }
    int PolygonZIndex { get; }
    bool PolygonIsVisible { get; }
    StyleId? PolygonFillStyleId { get; }
    CadColor SplineStrokeColor { get; }
    double SplineLineWeight { get; }
    int SplineZIndex { get; }
    bool SplineIsVisible { get; }
    bool SplineClosed { get; }
    StyleId? SplineFillStyleId { get; }
    CadColor CircleStrokeColor { get; }
    double CircleLineWeight { get; }
    int CircleZIndex { get; }
    bool CircleIsVisible { get; }
    StyleId? CircleFillStyleId { get; }
    CadColor EllipseStrokeColor { get; }
    double EllipseLineWeight { get; }
    int EllipseZIndex { get; }
    bool EllipseIsVisible { get; }
    StyleId? EllipseFillStyleId { get; }
    CadColor RectangleStrokeColor { get; }
    double RectangleLineWeight { get; }
    int RectangleZIndex { get; }
    bool RectangleIsVisible { get; }
    StyleId? RectangleFillStyleId { get; }
    double RectangleCornerRadiusX { get; }
    double RectangleCornerRadiusY { get; }
    string Text { get; }
    bool TextInverted { get; }
    double TextInvertedMarginFactor { get; }
    CadColor TextStrokeColor { get; }
    double TextLineWeight { get; }
    int TextZIndex { get; }
    bool TextIsVisible { get; }
    StyleId? TextStyleId { get; }
    CadColor ArcStrokeColor { get; }
    double ArcLineWeight { get; }
    int ArcZIndex { get; }
    bool ArcIsVisible { get; }
}
