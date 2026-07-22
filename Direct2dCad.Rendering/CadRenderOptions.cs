using System.Collections.Frozen;
using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering;

public sealed class CadRenderOptions
{
    public static IReadOnlySet<EntityId> NoHiddenEntities { get; } =
        FrozenSet<EntityId>.Empty;

    public BlockId ActiveOwnerBlockId { get; init; } = BlockId.ModelSpace;
    public LayoutId? ActiveLayoutId { get; init; }
    public LayoutViewportId? ActiveLayoutViewportId { get; init; }
    public bool DrawGrid { get; init; } = true;
    public bool DrawOrigin { get; init; } = true;
    public bool DrawGripHandles { get; init; } = true;
    public bool IsAntialiasingEnabled { get; init; } = true;
    public bool IsTextAntialiasingEnabled { get; init; } = true;
    public bool IsLevelOfDetailEnabled { get; init; }
    public bool AllowApproximateScaleFallback { get; init; }
    public double TransformScaleMultiplier { get; init; } = 1.0;
    public bool KeepStrokeWidthScreenConstant { get; init; } = true;
    public double MinimumScreenStrokeWidth { get; init; } = 0.5;
    public IReadOnlySet<EntityId> HiddenEntityIds { get; init; } = NoHiddenEntities;
    public CadRectD? DirtyWorldBounds { get; init; }
    public Func<BlockId, CadRectD, IReadOnlyList<EntityId>>? EntityBoundsQuery { get; init; }
    public Action<BlockId, CadRectD, List<EntityId>>? EntityBoundsQueryInto { get; init; }
}
