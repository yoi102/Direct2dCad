using Direct2dCad.Commands;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;

namespace Direct2dCad.ViewModels.Services.Interactions;

internal sealed class CadLayoutViewportPanController
{
    private PanSession? _session;

    public bool IsPanning => _session is not null;

    public bool Begin(
        CadEditor editor,
        LayoutId layoutId,
        CadLayoutViewport viewport,
        CadPointD screen,
        Guid? batchId = null)
    {
        Cancel();
        if (viewport.IsLocked)
            return false;

        _session = new PanSession(editor, layoutId, viewport,
            CadLayoutViewportSnapshot.From(viewport), screen, batchId);
        return true;
    }

    public bool Move(CadPointD screen)
    {
        if (_session is not { } session)
            return false;
        if (!IsAttached(session) || session.Viewport.IsLocked)
        {
            Cancel();
            return false;
        }

        var viewport = session.Viewport;
        var previousPaper = session.Editor.Viewport.ScreenToWorld(session.LastScreen);
        var currentPaper = session.Editor.Viewport.ScreenToWorld(screen);
        session.LastScreen = screen;
        var dx = (previousPaper.X - currentPaper.X) / viewport.Scale;
        var dy = (previousPaper.Y - currentPaper.Y) / viewport.Scale;
        var cos = Math.Cos(viewport.RotationRadians);
        var sin = Math.Sin(viewport.RotationRadians);
        var delta = new CadVectorD(dx * cos + dy * sin, -dx * sin + dy * cos);
        if (delta.LengthSquared <= double.Epsilon)
            return false;

        viewport.SetView(viewport.Bounds, viewport.ModelCenter + delta,
            viewport.Scale, viewport.RotationRadians);
        session.HasMoved = true;
        return true;
    }

    public bool End()
    {
        var session = _session;
        _session = null;
        if (session is null)
            return false;

        var target = CadLayoutViewportSnapshot.From(session.Viewport);
        var canCommit = IsAttached(session) && !session.Viewport.IsLocked;
        RestorePreview(session);
        if (!canCommit || !session.HasMoved || target == session.Initial)
            return false;

        // Restore the pre-drag state before the command captures its undo snapshot.
        if (session.BatchId is { } batchId)
            session.Editor.SetLayoutViewport(session.LayoutId, session.Viewport.Id, target, batchId);
        else
            session.Editor.SetLayoutViewport(session.LayoutId, session.Viewport.Id, target);
        return true;
    }

    public void Cancel()
    {
        var session = _session;
        _session = null;
        if (session is not null)
            RestorePreview(session);
    }

    private static void RestorePreview(PanSession session) =>
        session.Viewport.SetView(session.Initial.Bounds, session.Initial.ModelCenter,
            session.Initial.Scale, session.Initial.RotationRadians);

    private static bool IsAttached(PanSession session) =>
        session.Editor.Document.TryGetLayout(session.LayoutId, out var layout) &&
        layout is not null && layout.Viewports.Contains(session.Viewport);

    private sealed class PanSession(
        CadEditor editor,
        LayoutId layoutId,
        CadLayoutViewport viewport,
        CadLayoutViewportSnapshot initial,
        CadPointD screen,
        Guid? batchId)
    {
        public CadEditor Editor { get; } = editor;
        public LayoutId LayoutId { get; } = layoutId;
        public CadLayoutViewport Viewport { get; } = viewport;
        public CadLayoutViewportSnapshot Initial { get; } = initial;
        public CadPointD LastScreen { get; set; } = screen;
        public Guid? BatchId { get; } = batchId;
        public bool HasMoved { get; set; }
    }
}
