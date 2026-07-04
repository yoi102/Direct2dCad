using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.ViewModels;

internal sealed record ClipboardSnapshot(
    EntityId[] EntityIds,
    CadPointD BasePoint,
    CadRectD Bounds);
