using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Rendering;

public sealed class CadRenderOptions
{
    public BlockId ActiveOwnerBlockId { get; init; } = BlockId.ModelSpace;
    public LayoutId? ActiveLayoutId { get; init; }
    public LayoutViewportId? ActiveLayoutViewportId { get; init; }
    public bool DrawGrid { get; init; } = true;
    public bool DrawOrigin { get; init; } = true;
    public bool DrawGripHandles { get; init; } = true;
    public bool IsAntialiasingEnabled { get; init; } = true;
    public bool IsTextAntialiasingEnabled { get; init; } = true;
    public bool KeepStrokeWidthScreenConstant { get; init; } = true;
    public double MinimumScreenStrokeWidth { get; init; } = 0.5;
    public IReadOnlySet<EntityId> HiddenEntityIds { get; init; } = new HashSet<EntityId>();
    public CadRectD? DirtyWorldBounds { get; init; }
}
