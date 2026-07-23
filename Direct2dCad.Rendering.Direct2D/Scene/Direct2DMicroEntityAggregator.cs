using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;

namespace Direct2dCad.Rendering.Direct2D.Scene;

internal sealed class Direct2DMicroEntityAggregator
{
    private const int MinimumCandidateCount = 1024;
    private const double MaximumProjectedExtent = 1.0;
    private const double CellSizePixels = 2.0;
    private readonly Dictionary<long, RankedVisibleEntity> _topmostByCell = [];
    private readonly List<RankedVisibleEntity> _ordered = [];
    private readonly List<Direct2DVisibleEntity> _result = [];

    public IReadOnlyList<Direct2DVisibleEntity> Aggregate(
        IEnumerable<Direct2DVisibleEntity> visibleEntities,
        Direct2DOwnerRenderPacket renderPacket,
        CadViewport viewport,
        CadRenderOptions options,
        out int candidateCount,
        out int submittedCount)
    {
        ArgumentNullException.ThrowIfNull(visibleEntities);
        ArgumentNullException.ThrowIfNull(renderPacket);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(options);

        _topmostByCell.Clear();
        _ordered.Clear();
        _result.Clear();
        candidateCount = 0;

        var screenScale =
            Math.Max(viewport.Zoom, double.Epsilon) *
            Math.Max(options.TransformScaleMultiplier, double.Epsilon);

        foreach (var visible in visibleEntities)
        {
            var rank = renderPacket.GetRank(visible.Entity.Id);
            if (!IsMicroEntity(visible, screenScale, viewport, options))
            {
                _ordered.Add(new RankedVisibleEntity(rank, visible, null));
                continue;
            }

            candidateCount++;
            var screen = viewport.WorldToScreen(visible.Entity.Bounds.Center);
            var cellX = (int)Math.Floor(screen.X / CellSizePixels);
            var cellY = (int)Math.Floor(screen.Y / CellSizePixels);
            var cellKey = ((long)cellX << 32) | (uint)cellY;
            var ranked = new RankedVisibleEntity(rank, visible, cellKey);
            _ordered.Add(ranked);
            _topmostByCell[cellKey] = ranked;
        }

        if (_result.Capacity < _ordered.Count)
            _result.Capacity = _ordered.Count;
        foreach (var item in _ordered)
        {
            if (item.CellKey is { } cellKey &&
                (!_topmostByCell.TryGetValue(cellKey, out var representative) ||
                 representative.Rank != item.Rank ||
                 representative.Visible.Entity.Id != item.Visible.Entity.Id))
            {
                continue;
            }

            _result.Add(item.Visible);
        }

        submittedCount = _topmostByCell.Count;
        return _result;
    }

    public static bool ShouldAggregate(
        Direct2DOwnerRenderPacket renderPacket,
        CadRenderOptions options)
    {
        return options.IsLevelOfDetailEnabled &&
               options.DirtyWorldBounds is null or { IsEmpty: true } &&
               renderPacket.Entries.Count >= MinimumCandidateCount;
    }

    private static bool IsMicroEntity(
        Direct2DVisibleEntity visible,
        double screenScale,
        CadViewport viewport,
        CadRenderOptions options)
    {
        var entity = visible.Entity;
        if (entity is CadText or
            CadShapeText or
            CadImage or
            CadOleObject or
            CadBlockReference ||
            entity.Bounds.IsEmpty)
        {
            return false;
        }

        var width = Math.Abs(entity.Bounds.Width) * screenScale;
        var height = Math.Abs(entity.Bounds.Height) * screenScale;
        return width <= MaximumProjectedExtent &&
               height <= MaximumProjectedExtent &&
               Direct2DEntityLevelOfDetail.Resolve(
                   entity,
                   visible.Resources,
                   viewport,
                   options) == Direct2DEntityRenderDetail.Simplified;
    }

    private readonly record struct RankedVisibleEntity(
        int Rank,
        Direct2DVisibleEntity Visible,
        long? CellKey);
}
