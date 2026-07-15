using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Transient;

public abstract record CadTransientItem(CadTransientStyle Style);

public sealed record CadTransientGroup(
    IReadOnlyList<CadTransientItem> Items,
    CadMatrixD Transform,
    CadTransientStyle Style = default)
    : CadTransientItem(Style);

public sealed record CadTransientLine(
    CadPointD Start,
    CadPointD End,
    CadTransientStyle Style)
    : CadTransientItem(Style);

public sealed record CadTransientCircle(
    CadPointD Center,
    double Radius,
    CadTransientStyle Style)
    : CadTransientItem(Style);

public sealed record CadTransientEllipse(
    CadPointD Center,
    double RadiusX,
    double RadiusY,
    CadTransientStyle Style)
    : CadTransientItem(Style);

public sealed record CadTransientEllipseArc(
    CadPointD Center,
    double RadiusX,
    double RadiusY,
    double StartAngleRadians,
    double SweepAngleRadians,
    CadTransientStyle Style)
    : CadTransientItem(Style);

public sealed record CadTransientArc(
    CadPointD Center,
    double Radius,
    double StartAngleRadians,
    double SweepAngleRadians,
    CadTransientStyle Style)
    : CadTransientItem(Style);

public sealed record CadTransientPolyline(
    IReadOnlyList<CadPointD> Points,
    bool Closed,
    CadTransientStyle Style)
    : CadTransientItem(Style);

public sealed record CadTransientSpline(
    IReadOnlyList<CadPointD> FitPoints,
    bool Closed,
    CadTransientStyle Style)
    : CadTransientItem(Style);

public sealed record CadTransientRectangle(
    CadRectD Bounds,
    CadTransientStyle Style,
    double CornerRadiusX = 0,
    double CornerRadiusY = 0)
    : CadTransientItem(Style);

public sealed record CadTransientImage(
    CadRectD Bounds,
    int PixelWidth,
    int PixelHeight,
    int Stride,
    byte[] Pixels,
    CadTransientStyle Style,
    EntityId? SourceEntityId = null,
    double Opacity = 1.0,
    double RotationRadians = 0.0)
    : CadTransientItem(Style);

public sealed record CadTransientOleObject(
    CadRectD Bounds,
    byte[] OleBytes,
    CadTransientStyle Style,
    EntityId? SourceEntityId,
    Guid RenderId,
    double Opacity = 1.0)
    : CadTransientItem(Style);

public sealed record CadTransientText(
    string Text,
    CadPointD Position,
    double Height,
    CadRectD Bounds,
    CadTransientStyle Style,
    bool IsInverted = false,
    double InvertedMarginFactor = CadText.DefaultInvertedMarginFactor,
    StyleId? TextStyleId = null,
    double RotationRadians = 0.0,
    CadTransientTextFormat? TextFormat = null)
    : CadTransientItem(Style);

public sealed record CadTransientTextFormat(
    string FontFamily,
    bool IsBold,
    bool IsItalic);

public sealed record CadTransientShapeText(
    string Text,
    CadPointD Position,
    double Height,
    double RotationRadians,
    double WidthFactor,
    double CharacterSpacingFactor,
    double ObliqueAngleRadians,
    CadTransientStyle Style,
    bool IsInverted = false,
    double InvertedMarginFactor = CadShapeText.DefaultInvertedMarginFactor,
    CadShapeFontId ShapeFontId = default)
    : CadTransientItem(Style);

public sealed record CadTransientEntityReference(
    EntityId EntityId,
    CadVectorD Offset,
    CadTransientStyle Style)
    : CadTransientItem(Style);

public sealed record CadTransientBlockReference(
    BlockId DefinitionBlockId,
    CadPointD Position,
    double RotationRadians,
    double ScaleX,
    double ScaleY,
    CadTransientStyle Style)
    : CadTransientItem(Style);
