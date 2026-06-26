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

public sealed record CadTransientRectangle(
    CadRectD Bounds,
    CadTransientStyle Style)
    : CadTransientItem(Style);

public sealed record CadTransientText(
    string Text,
    CadPointD Position,
    double Height,
    CadTransientStyle Style)
    : CadTransientItem(Style);

public sealed record CadTransientEntityReference(
    EntityId EntityId,
    CadVectorD Offset,
    CadTransientStyle Style)
    : CadTransientItem(Style);
