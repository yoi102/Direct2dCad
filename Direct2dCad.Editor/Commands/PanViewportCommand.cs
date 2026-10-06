using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Editor.Commands;

public sealed class PanViewportCommand : ICadCoalescibleEditorCommand
{
    private CadVectorD _screenDelta;
    private double? _previousZoom;
    private CadPointD _previousOffset;

    public string Name => "Pan View";

    public PanViewportCommand(CadVectorD screenDelta)
    {
        _screenDelta = screenDelta;
    }

    public CadEditorCommandResult Execute(CadEditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _previousZoom = context.Viewport.Zoom;
        _previousOffset = context.Viewport.Offset;
        context.Viewport.PanScreen(_screenDelta);
        return CadEditorCommandResult.View();
    }

    public CadEditorCommandResult Undo(CadEditorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_previousZoom is null)
            return CadEditorCommandResult.Empty;

        context.Viewport.SetView(_previousZoom.Value, _previousOffset);
        return CadEditorCommandResult.View();
    }

    public bool TryMergeExecuted(ICadEditorCommand subsequent)
    {
        if (subsequent is not PanViewportCommand pan || pan._previousZoom is null)
            return false;
        _screenDelta += pan._screenDelta;
        return true;
    }
}
