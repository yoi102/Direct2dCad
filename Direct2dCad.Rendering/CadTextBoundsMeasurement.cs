using Direct2dCad.Db;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering;

/// <summary>Owned metric values and the text inputs they were measured from; no live entity is retained.</summary>
public readonly record struct CadTextBoundsMeasurement(
    EntityId EntityId, string Text, double Height, StyleId? TextStyleId, CadRectD LocalBounds)
{
    public bool Matches(CadText entity) => EntityId == entity.Id && Text == entity.Text &&
        Height == entity.Height && TextStyleId == entity.TextStyleId;
}
