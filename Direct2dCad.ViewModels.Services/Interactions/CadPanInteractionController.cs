using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;

namespace Direct2dCad.ViewModels.Interactions;

internal sealed class CadPanInteractionController
{
    private CadPointD? _lastPanPoint;

    public bool IsPanning { get; private set; }

    public void Begin(CadPointD screen)
    {
        IsPanning = true;
        _lastPanPoint = screen;
    }

    public void End()
    {
        IsPanning = false;
        _lastPanPoint = null;
    }

    public bool Move(CadEditor editor, CadPointD screen)
    {
        if (!IsPanning || _lastPanPoint is null)
            return false;

        var delta = screen - _lastPanPoint.Value;
        _lastPanPoint = screen;
        editor.Execute(new PanViewportCommand(delta));
        return true;
    }
}
