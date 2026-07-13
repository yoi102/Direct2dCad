using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Commands;

public sealed class CreateLayoutCommand(
    string name,
    double paperWidth = 420,
    double paperHeight = 297) : ICadCommand
{
    private CadLayout? _layout;
    public string Name => "Create Layout";
    public LayoutId? CreatedLayoutId => _layout?.Id;

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_layout is null)
        {
            var id = document.CreateLayout(name, paperWidth, paperHeight);
            _layout = document.GetLayout(id);
        }
        else
        {
            document.RestoreLayout(_layout);
        }

        return CadDocumentChangeSet.Empty.WithLayoutStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_layout is null)
            return CadDocumentChangeSet.Empty;
        document.DetachLayout(_layout.Id);
        return CadDocumentChangeSet.Empty.WithLayoutStructureChanged();
    }
}

public sealed class DeleteLayoutCommand(LayoutId layoutId) : ICadCommand
{
    private CadLayout? _layout;
    public string Name => "Delete Layout";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        _layout = document.DetachLayout(layoutId);
        var entityIds = document.GetBlock(_layout.PaperSpaceBlockId).EntityIds;
        foreach (var entityId in entityIds)
            document.GetEntity(entityId).Erase();
        return CadDocumentChangeSet.ForEntities(
                entityIds,
                CadEntityChangeKind.Deleted | CadEntityChangeKind.Visibility)
            .WithLayoutStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_layout is null)
            return CadDocumentChangeSet.Empty;
        document.RestoreLayout(_layout);
        var entityIds = document.GetBlock(_layout.PaperSpaceBlockId).EntityIds;
        foreach (var entityId in entityIds)
            document.GetEntity(entityId).Restore();
        return CadDocumentChangeSet.ForEntities(
                entityIds,
                CadEntityChangeKind.Created | CadEntityChangeKind.Visibility)
            .WithLayoutStructureChanged();
    }
}

public sealed class RenameLayoutCommand(LayoutId layoutId, string name) : ICadCommand
{
    private string? _previousName;
    public string Name => "Rename Layout";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        _previousName = document.GetLayout(layoutId).Name;
        document.RenameLayout(layoutId, name);
        return CadDocumentChangeSet.Empty.WithLayoutStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previousName is null)
            return CadDocumentChangeSet.Empty;
        document.RenameLayout(layoutId, _previousName);
        return CadDocumentChangeSet.Empty.WithLayoutStructureChanged();
    }
}

public readonly record struct CadLayoutPaperSnapshot(
    double Width,
    double Height,
    double MarginLeft,
    double MarginTop,
    double MarginRight,
    double MarginBottom)
{
    public static CadLayoutPaperSnapshot From(CadLayout layout) => new(
        layout.PaperWidth,
        layout.PaperHeight,
        layout.MarginLeft,
        layout.MarginTop,
        layout.MarginRight,
        layout.MarginBottom);
}

public sealed class SetLayoutPaperCommand(
    LayoutId layoutId,
    CadLayoutPaperSnapshot target) : ICadCommand
{
    private CadLayoutPaperSnapshot? _previous;
    public string Name => "Set Layout Paper";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        _previous = CadLayoutPaperSnapshot.From(document.GetLayout(layoutId));
        Apply(document, target);
        return CadDocumentChangeSet.Empty.WithLayoutsChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previous is null)
            return CadDocumentChangeSet.Empty;
        Apply(document, _previous.Value);
        return CadDocumentChangeSet.Empty.WithLayoutsChanged();
    }

    private void Apply(CadDocument document, CadLayoutPaperSnapshot value) =>
        document.SetLayoutPaper(
            layoutId,
            value.Width,
            value.Height,
            value.MarginLeft,
            value.MarginTop,
            value.MarginRight,
            value.MarginBottom);
}

public sealed class AddLayoutViewportCommand(
    LayoutId layoutId,
    CadRectD bounds,
    CadPointD modelCenter,
    double scale,
    double rotationRadians = 0) : ICadCommand
{
    private CadLayoutViewport? _viewport;
    public string Name => "Add Layout Viewport";
    public LayoutViewportId? CreatedViewportId => _viewport?.Id;

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        if (_viewport is null)
        {
            var viewportId = document.AddLayoutViewport(
                layoutId,
                bounds,
                modelCenter,
                scale,
                rotationRadians);
            _viewport = document.GetLayout(layoutId).GetViewport(viewportId);
        }
        else
        {
            document.RestoreLayoutViewport(layoutId, _viewport);
        }
        return CadDocumentChangeSet.Empty.WithLayoutStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_viewport is null)
            return CadDocumentChangeSet.Empty;
        document.RemoveLayoutViewport(layoutId, _viewport.Id);
        return CadDocumentChangeSet.Empty.WithLayoutStructureChanged();
    }
}

public sealed class RemoveLayoutViewportCommand(
    LayoutId layoutId,
    LayoutViewportId viewportId) : ICadCommand
{
    private CadLayoutViewport? _viewport;
    public string Name => "Remove Layout Viewport";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var layout = document.GetLayout(layoutId);
        _viewport = layout.GetViewport(viewportId);
        document.RemoveLayoutViewport(layoutId, viewportId);
        return CadDocumentChangeSet.Empty.WithLayoutStructureChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_viewport is null)
            return CadDocumentChangeSet.Empty;

        document.RestoreLayoutViewport(layoutId, _viewport);
        return CadDocumentChangeSet.Empty.WithLayoutStructureChanged();
    }
}

public readonly record struct CadLayoutViewportSnapshot(
    CadRectD Bounds,
    CadPointD ModelCenter,
    double Scale,
    double RotationRadians,
    bool IsVisible,
    bool IsLocked)
{
    public static CadLayoutViewportSnapshot From(CadLayoutViewport viewport) => new(
        viewport.Bounds,
        viewport.ModelCenter,
        viewport.Scale,
        viewport.RotationRadians,
        viewport.IsVisible,
        viewport.IsLocked);

    public void ApplyTo(CadLayoutViewport viewport)
    {
        viewport.SetView(Bounds, ModelCenter, Scale, RotationRadians);
        viewport.SetVisible(IsVisible);
        viewport.SetLocked(IsLocked);
    }
}

public sealed class SetLayoutViewportCommand(
    LayoutId layoutId,
    LayoutViewportId viewportId,
    CadLayoutViewportSnapshot target) : ICadCommand
{
    private CadLayoutViewportSnapshot? _previous;
    public string Name => "Set Layout Viewport";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var viewport = document.GetLayout(layoutId).GetViewport(viewportId);
        _previous = CadLayoutViewportSnapshot.From(viewport);
        target.ApplyTo(viewport);
        return CadDocumentChangeSet.Empty.WithLayoutsChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previous is null)
            return CadDocumentChangeSet.Empty;
        _previous.Value.ApplyTo(document.GetLayout(layoutId).GetViewport(viewportId));
        return CadDocumentChangeSet.Empty.WithLayoutsChanged();
    }
}

public sealed class SetLayoutPaperColorCommand(LayoutId layoutId, CadColor color) : ICadCommand
{
    private CadColor? _previous;
    public string Name => "Set Layout Paper Color";

    public CadDocumentChangeSet Execute(CadDocument document)
    {
        var layout = document.GetLayout(layoutId);
        _previous = layout.PaperColor;
        layout.SetPaperColor(color);
        return CadDocumentChangeSet.Empty.WithLayoutsChanged();
    }

    public CadDocumentChangeSet Undo(CadDocument document)
    {
        if (_previous is null)
            return CadDocumentChangeSet.Empty;
        document.GetLayout(layoutId).SetPaperColor(_previous.Value);
        return CadDocumentChangeSet.Empty.WithLayoutsChanged();
    }
}
