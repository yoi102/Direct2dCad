using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering.Handles;

namespace Direct2dCad.ViewModels;

internal sealed class GripDragState
{
    public GripDragState(CadGripHandle handle, CadPointD pointerWorld, int pointIndex)
    {
        Handle = handle;
        StartPointerWorld = pointerWorld;
        CurrentPointerWorld = pointerWorld;
        PointIndex = pointIndex;
    }

    public CadGripHandle Handle { get; }
    public CadPointD StartPointerWorld { get; }
    public CadPointD CurrentPointerWorld { get; set; }
    public int PointIndex { get; }
    public CadVectorD Delta => CurrentPointerWorld - StartPointerWorld;
    public CadPointD DraggedGripPosition => Handle.Position + Delta;
}
