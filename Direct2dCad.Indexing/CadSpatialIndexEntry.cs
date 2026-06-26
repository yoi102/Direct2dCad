using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Indexing;

public readonly record struct CadSpatialIndexEntry(
    EntityId EntityId,
    CadRectD Bounds);
