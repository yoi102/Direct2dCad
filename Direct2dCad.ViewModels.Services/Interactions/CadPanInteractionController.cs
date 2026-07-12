using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadPanInteractionController
{
    private CadPointD? _lastPanPoint;
    private bool _hasMoved;

    public bool IsPanning { get; private set; }

    public void Begin(CadPointD screen)
    {
        IsPanning = true;
        _lastPanPoint = screen;
        _hasMoved = false;
    }

    public bool End()
    {
        var hasMoved = IsPanning && _hasMoved;
        IsPanning = false;
        _lastPanPoint = null;
        _hasMoved = false;
        return hasMoved;
    }

    public bool Move(CadEditor editor, CadPointD screen)
    {
        if (!IsPanning || _lastPanPoint is null)
            return false;

        var delta = screen - _lastPanPoint.Value;
        _lastPanPoint = screen;
        if (delta.LengthSquared <= double.Epsilon)
            return false;

        editor.Execute(new PanViewportCommand(delta));
        _hasMoved = true;
        return true;
    }
}
