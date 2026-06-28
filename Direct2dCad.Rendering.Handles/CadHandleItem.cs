using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering.Handles;

public abstract record CadHandleItem(CadHandleStyle Style);

public sealed record CadSelectionEntityReference(
    EntityId EntityId,
    CadVectorD Offset,
    CadHandleStyle Style)
    : CadHandleItem(Style);

public sealed record CadGripHandle(
    EntityId EntityId,
    CadPointD Position,
    CadHandleType Type,
    CadHandleStyle Style)
    : CadHandleItem(Style);
