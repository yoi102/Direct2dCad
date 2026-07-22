using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class GripDragState
{
    public GripDragState(
        CadGripHandle handle,
        CadPointD pointerWorld,
        int pointIndex,
        IReadOnlySet<EntityId> hiddenEntityIds)
    {
        Handle = handle;
        StartPointerWorld = pointerWorld;
        CurrentPointerWorld = pointerWorld;
        PointIndex = pointIndex;
        HiddenEntityIds = hiddenEntityIds;
    }

    public CadGripHandle Handle { get; }
    public CadPointD StartPointerWorld { get; }
    public CadPointD CurrentPointerWorld { get; set; }
    public int PointIndex { get; }
    public IReadOnlySet<EntityId> HiddenEntityIds { get; }
    public IReadOnlyList<CadTransientItem>? MovePreviewItems { get; set; }
    public CadRectD MovePreviewBounds { get; set; } = CadRectD.Empty;
    public CadTransientStyle MovePreviewStyle { get; set; }
    public CadVectorD Delta => CurrentPointerWorld - StartPointerWorld;
    public CadPointD DraggedGripPosition => Handle.Position + Delta;
}
