using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadViewportInitializationState
{
    private bool _isInitialViewportViewApplied;

    public double ViewportWidth { get; private set; } = 1.0;

    public double ViewportHeight { get; private set; } = 1.0;

    public void ResetInitialView()
    {
        _isInitialViewportViewApplied = false;
    }

    public void ApplyCurrentSize(CadEditor editor)
    {
        editor.Viewport.SetSize(ViewportWidth, ViewportHeight);
        ApplyInitialViewportViewIfNeeded(editor);
    }

    public void SetViewportSize(CadEditor editor, double width, double height)
    {
        ViewportWidth = Math.Max(1, width);
        ViewportHeight = Math.Max(1, height);
        ApplyCurrentSize(editor);
    }

    private void ApplyInitialViewportViewIfNeeded(CadEditor editor)
    {
        if (_isInitialViewportViewApplied || ViewportWidth <= 1.0 || ViewportHeight <= 1.0)
            return;

        var zoom = editor.Viewport.Zoom;
        var origin = editor.Document.ViewSettings.Origin.Position;
        var offset = new CadPointD(
            ViewportWidth * 0.5 - origin.X * zoom,
            ViewportHeight * 0.5 + origin.Y * zoom);

        editor.Viewport.SetView(zoom, offset);
        _isInitialViewportViewApplied = true;
    }
}
