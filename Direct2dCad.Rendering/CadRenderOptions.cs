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
    public bool DrawLayoutGuides { get; init; } = true;
    public bool IsAntialiasingEnabled { get; init; } = true;
    public bool IsTextAntialiasingEnabled { get; init; } = true;
    public bool IsLevelOfDetailEnabled { get; init; }
    public bool AllowApproximateTileScaleFallback { get; init; }
    public bool IsBackgroundChunkRecordingEnabled { get; init; }
    public bool IsParallelRenderingEnabled { get; init; }
    public CadParallelRenderingMode ParallelRenderingMode { get; init; } =
        CadParallelRenderingMode.MultipleDevices;
    public int ParallelRenderingWorkerCount { get; init; } = 2;
    public int ParallelRenderingEntityThreshold { get; init; } = 1000;
    public bool EnableGeometryRealizations { get; init; } = true;
    public double TransformScaleMultiplier { get; init; } = 1.0;
    // Model space uses a stable on-screen representation of the plotted millimeter width.
    // Layout space disables this so the line width follows the paper zoom.
    public bool KeepStrokeWidthScreenConstant { get; init; } = true;
    public double MinimumScreenStrokeWidth { get; init; } = 0.5;
    // Converts plotted paper millimeters to the current entity owner's world units.
    // Paper space is 1; a model shown through a layout viewport is 1 / viewport.Scale.
    public double EntityLineWeightWorldScale { get; init; } = 1.0;
    public IReadOnlySet<EntityId> HiddenEntityIds { get; init; } = NoHiddenEntities;
    public CadRectD? DirtyWorldBounds { get; init; }
    public Func<BlockId, CadRectD, IReadOnlyList<EntityId>>? EntityBoundsQuery { get; init; }
    public Action<BlockId, CadRectD, List<EntityId>>? EntityBoundsQueryInto { get; init; }
    public Func<BlockId, CadRectD, int>? EntityBoundsCount { get; init; }
}
