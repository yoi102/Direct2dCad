using Direct2dCad.Db;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadSpaceViewportState
{
    private readonly Dictionary<LayoutId, ViewState> _layoutViews = [];
    private ViewState? _modelView;

    public void Capture(CadViewport viewport, LayoutId? layoutId)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        if (viewport.ViewWidth <= 1 || viewport.ViewHeight <= 1)
            return;

        var state = new ViewState(viewport.Zoom, viewport.Offset);
        if (layoutId is { } id)
            _layoutViews[id] = state;
        else
            _modelView = state;
    }

    public bool TryRestore(CadViewport viewport, LayoutId? layoutId)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        var state = layoutId is { } id
            ? _layoutViews.GetValueOrDefault(id)
            : _modelView;
        if (state is null)
            return false;

        viewport.SetView(state.Zoom, state.Offset);
        return true;
    }

    public void Reset()
    {
        _modelView = null;
        _layoutViews.Clear();
    }

    private sealed record ViewState(double Zoom, CadPointD Offset);
}
