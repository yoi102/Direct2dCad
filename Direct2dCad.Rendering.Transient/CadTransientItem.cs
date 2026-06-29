using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Transient;

public abstract record CadTransientItem(CadTransientStyle Style);

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
    CadTransientStyle Style)
    : CadTransientItem(Style);

public sealed record CadTransientText(
    string Text,
    CadPointD Position,
    double Height,
    CadRectD Bounds,
    CadTransientStyle Style)
    : CadTransientItem(Style);

public sealed record CadTransientEntityReference(
    EntityId EntityId,
    CadVectorD Offset,
    CadTransientStyle Style)
    : CadTransientItem(Style);
