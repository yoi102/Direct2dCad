using Direct2dCad.Db;
using Direct2dCad.Db.Cad;

namespace Direct2dCad.ViewModels.Services.Drawing;

public interface ICadDrawingDefaults
{
    CadColor LineStrokeColor { get; }
    bool LineUseLayerColor { get; }
    double LineLineWeight { get; }
    bool LineUseLayerLineWeight { get; }
    int LineZIndex { get; }
    bool LineIsVisible { get; }
    CadColor PolylineStrokeColor { get; }
    bool PolylineUseLayerColor { get; }
    double PolylineLineWeight { get; }
    bool PolylineUseLayerLineWeight { get; }
    int PolylineZIndex { get; }
    bool PolylineIsVisible { get; }
    bool PolylineClosed { get; }
    StyleId? PolylineFillStyleId { get; }
    CadColor PolygonStrokeColor { get; }
    bool PolygonUseLayerColor { get; }
    double PolygonLineWeight { get; }
    bool PolygonUseLayerLineWeight { get; }
    int PolygonZIndex { get; }
    bool PolygonIsVisible { get; }
    StyleId? PolygonFillStyleId { get; }
    CadColor SplineStrokeColor { get; }
    bool SplineUseLayerColor { get; }
    double SplineLineWeight { get; }
    bool SplineUseLayerLineWeight { get; }
    int SplineZIndex { get; }
    bool SplineIsVisible { get; }
    bool SplineClosed { get; }
    StyleId? SplineFillStyleId { get; }
    CadColor CircleStrokeColor { get; }
    bool CircleUseLayerColor { get; }
    double CircleLineWeight { get; }
    bool CircleUseLayerLineWeight { get; }
    int CircleZIndex { get; }
    bool CircleIsVisible { get; }
    StyleId? CircleFillStyleId { get; }
    CadColor EllipseStrokeColor { get; }
    bool EllipseUseLayerColor { get; }
    double EllipseLineWeight { get; }
    bool EllipseUseLayerLineWeight { get; }
    int EllipseZIndex { get; }
    bool EllipseIsVisible { get; }
    StyleId? EllipseFillStyleId { get; }
    CadColor RectangleStrokeColor { get; }
    bool RectangleUseLayerColor { get; }
    double RectangleLineWeight { get; }
    bool RectangleUseLayerLineWeight { get; }
    int RectangleZIndex { get; }
    bool RectangleIsVisible { get; }
    StyleId? RectangleFillStyleId { get; }
    double RectangleCornerRadiusX { get; }
    double RectangleCornerRadiusY { get; }
    string Text { get; }
    bool TextInverted { get; }
    double TextInvertedMarginFactor { get; }
    CadColor TextStrokeColor { get; }
    bool TextUseLayerColor { get; }
    double TextLineWeight { get; }
    bool TextUseLayerLineWeight { get; }
    int TextZIndex { get; }
    bool TextIsVisible { get; }
    StyleId? TextStyleId { get; }
    CadColor ArcStrokeColor { get; }
    bool ArcUseLayerColor { get; }
    double ArcLineWeight { get; }
    bool ArcUseLayerLineWeight { get; }
    int ArcZIndex { get; }
    bool ArcIsVisible { get; }
}
