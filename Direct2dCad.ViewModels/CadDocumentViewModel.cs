using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Rendering.Direct2D;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel : ObservableObject, IDisposable
{
    private readonly CadTransientScene _transientScene = new();
    private CadPointD? _pendingWorldPoint;
    private CadPointD? _currentMousePoint;
    private CadPointD? _lastPanPoint;
    private CadPointD? _selectionDragStart;
    private ClipboardSnapshot? _clipboard;
    private bool _isPastePreviewActive;
    private bool _isRenderAttached;
    private bool _disposed;
    private double _viewportWidth = 1.0;
    private double _viewportHeight = 1.0;

    [ObservableProperty]
    public partial CadEditor CadEditor { get; private set; } = new(CadDocument.Create("Untitled"));

    [ObservableProperty]
    public partial Direct2DImageRenderHost Direct2DImageRenderHost { get; private set; } = new();

    [ObservableProperty]
    public partial CadCanvasToolMode CadCanvasToolMode { get; internal set; } = CadCanvasToolMode.Select;

    [ObservableProperty]
    public partial string DrawingText { get; set; } = "Text";

    public event EventHandler? ViewSettingsChanged;

    public bool IsPanning { get; private set; }

    internal void ReplaceEditor(CadEditor editor)
    {
        var wasAttached = _isRenderAttached;
        if (wasAttached)
            DetachRenderResources();

        CadEditor = editor ?? throw new ArgumentNullException(nameof(editor));
        CadEditor.Viewport.SetSize(_viewportWidth, _viewportHeight);
        ClearInteractionState(clearClipboard: true, render: false);

        if (wasAttached)
            AttachRenderResources();

        RequestRender();
    }

    public void AttachRenderResources()
    {
        ThrowIfDisposed();

        if (_isRenderAttached)
            return;

        Direct2DImageRenderHost.SetScene(CadEditor.Document, CadEditor.Viewport);
        Direct2DImageRenderHost.SetTransientScene(_transientScene);
        CadEditor.DocumentChanged += OnDocumentChanged;
        CadEditor.RegisterGeometryResourceManager(Direct2DImageRenderHost.GeometryResourceManager);
        _isRenderAttached = true;
    }

    public void DetachRenderResources()
    {
        if (!_isRenderAttached)
            return;

        CadEditor.DocumentChanged -= OnDocumentChanged;
        CadEditor.UnregisterGeometryResourceManager(Direct2DImageRenderHost.GeometryResourceManager);
        _isRenderAttached = false;
    }

    public void SetViewportSize(double width, double height)
    {
        _viewportWidth = Math.Max(1, width);
        _viewportHeight = Math.Max(1, height);
        CadEditor.Viewport.SetSize(_viewportWidth, _viewportHeight);
    }

    public void SetRenderSize(int width, int height)
    {
        Direct2DImageRenderHost.SetSize(Math.Max(1, width), Math.Max(1, height));
    }

    public CadCanvasInteractionResult SetToolMode(CadCanvasToolMode toolMode)
    {
        CadCanvasToolMode = toolMode;
        ClearInteractionState(clearClipboard: false);
        return new CadCanvasInteractionResult(
            true,
            ReleaseMouseCapture: true,
            Cursor: CadCanvasToolMode == CadCanvasToolMode.Pan ? CadCanvasCursorKind.Hand : CadCanvasCursorKind.Cross);
    }

    public CadCanvasInteractionResult PointerDown(
        CadPointD screen,
        CadCanvasPointerButton button,
        bool forcePan)
    {
        _currentMousePoint = screen;

        if (forcePan || button is CadCanvasPointerButton.Middle or CadCanvasPointerButton.Right || CadCanvasToolMode == CadCanvasToolMode.Pan)
        {
            BeginPan(screen);
            return new CadCanvasInteractionResult(true, CaptureMouse: true, Cursor: CadCanvasCursorKind.Hand);
        }

        if (button != CadCanvasPointerButton.Left)
            return CadCanvasInteractionResult.NotHandled;

        if (_isPastePreviewActive)
        {
            CommitPaste(screen);
            return CadCanvasInteractionResult.HandledOnly;
        }

        if (CadCanvasToolMode == CadCanvasToolMode.Select)
        {
            _selectionDragStart = screen;
            RequestRender();
            return new CadCanvasInteractionResult(true, CaptureMouse: true);
        }

        HandleDrawingClick(screen);
        return CadCanvasInteractionResult.HandledOnly;
    }

    public CadCanvasInteractionResult PointerMove(CadPointD screen)
    {
        _currentMousePoint = screen;

        if (IsPanning && _lastPanPoint is not null)
        {
            var delta = screen - _lastPanPoint.Value;
            _lastPanPoint = screen;
            CadEditor.Execute(new PanViewportCommand(delta));
        }

        RequestRender();
        return CadCanvasInteractionResult.HandledOnly;
    }

    public CadCanvasInteractionResult PointerUp(CadPointD screen, CadCanvasPointerButton button)
    {
        _currentMousePoint = screen;

        if (IsPanning)
        {
            EndPan();
            return new CadCanvasInteractionResult(
                true,
                ReleaseMouseCapture: true,
                Cursor: CadCanvasToolMode == CadCanvasToolMode.Pan ? CadCanvasCursorKind.Hand : CadCanvasCursorKind.Cross);
        }

        if (CadCanvasToolMode == CadCanvasToolMode.Select &&
            button == CadCanvasPointerButton.Left &&
            _selectionDragStart is not null)
        {
            CompleteSelection(screen);
            return new CadCanvasInteractionResult(true, ReleaseMouseCapture: true);
        }

        return CadCanvasInteractionResult.NotHandled;
    }

    public CadCanvasInteractionResult MouseWheel(CadPointD screen, int delta)
    {
        var factor = delta > 0 ? 1.1 : 1.0 / 1.1;
        CadEditor.Execute(new ZoomViewportCommand(screen, factor));
        RequestRender();
        return CadCanvasInteractionResult.HandledOnly;
    }

    public CadCanvasInteractionResult Escape()
    {
        ClearInteractionState(clearClipboard: false);
        EndPan();
        return new CadCanvasInteractionResult(
            true,
            ReleaseMouseCapture: true,
            Cursor: CadCanvasToolMode == CadCanvasToolMode.Pan ? CadCanvasCursorKind.Hand : CadCanvasCursorKind.Cross);
    }

    public void Undo()
    {
        CadEditor.Undo();
        RequestRender();
    }

    public void Redo()
    {
        CadEditor.Redo();
        RequestRender();
    }

    [RelayCommand]
    public void FitToWindow()
    {
        CadEditor.Execute(new FitViewportCommand());
        RequestRender();
    }

    public void CopySelection()
    {
        if (CadEditor.Selection.Count == 0)
            return;

        var entityIds = new List<EntityId>();
        var bounds = CadRectD.Empty;

        foreach (var entityId in CadEditor.Selection.EntityIds)
        {
            if (!CadEditor.Document.TryGetEntity(entityId, out var entity) ||
                entity is null ||
                entity.IsErased ||
                !CanDuplicate(entity))
            {
                continue;
            }

            entityIds.Add(entityId);
            bounds = bounds.Union(entity.Bounds);
        }

        if (entityIds.Count == 0 || bounds.IsEmpty)
            return;

        _clipboard = new ClipboardSnapshot(entityIds.ToArray(), bounds.Center, bounds);
    }

    public CadCanvasInteractionResult BeginPastePreview()
    {
        if (_clipboard is null)
            CopySelection();

        if (_clipboard is null)
            return CadCanvasInteractionResult.NotHandled;

        _isPastePreviewActive = true;
        _pendingWorldPoint = null;
        _selectionDragStart = null;
        RequestRender();
        return new CadCanvasInteractionResult(true, Cursor: CadCanvasCursorKind.Cross);
    }

    public void RequestRender()
    {
        UpdateTransientScene();
        Direct2DImageRenderHost.Render();
    }

    private void BeginPan(CadPointD screen)
    {
        IsPanning = true;
        _lastPanPoint = screen;
    }

    private void EndPan()
    {
        IsPanning = false;
        _lastPanPoint = null;
    }

    private void HandleDrawingClick(CadPointD screen)
    {
        var world = ScreenToWorld(screen, snapToGrid: true);

        switch (CadCanvasToolMode)
        {
            case CadCanvasToolMode.Line:
                if (_pendingWorldPoint is null)
                    _pendingWorldPoint = world;
                else
                {
                    CadEditor.AddLine(_pendingWorldPoint.Value, world);
                    _pendingWorldPoint = null;
                }
                RequestRender();
                break;

            case CadCanvasToolMode.Circle:
                if (_pendingWorldPoint is null)
                    _pendingWorldPoint = world;
                else
                {
                    var radius = _pendingWorldPoint.Value.DistanceTo(world);
                    if (radius > 0)
                        CadEditor.AddCircle(_pendingWorldPoint.Value, radius);

                    _pendingWorldPoint = null;
                }
                RequestRender();
                break;

            case CadCanvasToolMode.Text:
                CadEditor.AddText(
                    string.IsNullOrWhiteSpace(DrawingText) ? "Text" : DrawingText,
                    world,
                    Math.Max(8.0 / CadEditor.Viewport.Zoom, 1.0));
                RequestRender();
                break;

            case CadCanvasToolMode.SetOrigin:
                CadEditor.SetOriginPosition(world);
                RequestRender();
                break;
        }
    }

    private void CompleteSelection(CadPointD endScreen)
    {
        if (_selectionDragStart is null)
            return;

        var startScreen = _selectionDragStart.Value;
        _selectionDragStart = null;

        if ((endScreen - startScreen).Length < 4)
        {
            CadEditor.Execute(new ClickSelectCommand(
                ScreenToWorld(endScreen),
                6.0 / CadEditor.Viewport.Zoom));
            RequestRender();
            return;
        }

        var p1 = ScreenToWorld(startScreen);
        var p2 = ScreenToWorld(endScreen);
        var area = CadRectD.FromLTRB(p1.X, p1.Y, p2.X, p2.Y);
        CadEditor.Execute(new BoxSelectCommand(
            area,
            requireContained: IsSelectionWindow(startScreen, endScreen)));
        RequestRender();
    }

    private void CommitPaste(CadPointD screen)
    {
        if (_clipboard is null)
            return;

        var target = ScreenToWorld(screen, snapToGrid: true);
        var delta = target - _clipboard.BasePoint;
        var createdIds = CadEditor.DuplicateEntities(_clipboard.EntityIds, delta);
        if (createdIds.Count > 0)
            CadEditor.Selection.Replace(createdIds);

        _isPastePreviewActive = false;
        RequestRender();
    }

    private void ClearInteractionState(bool clearClipboard, bool render = true)
    {
        _pendingWorldPoint = null;
        _selectionDragStart = null;
        _isPastePreviewActive = false;

        if (clearClipboard)
            _clipboard = null;

        _transientScene.Clear();

        if (render)
            Direct2DImageRenderHost.Render();
    }

    private void UpdateTransientScene()
    {
        var items = new List<CadTransientItem>();
        AddSelectionHighlights(items);

        if (_currentMousePoint is { } mousePoint)
        {
            var rawMouseWorld = ScreenToWorld(mousePoint);
            var snappedMouseWorld = SnapWorld(rawMouseWorld);
            AddPastePreview(items, snappedMouseWorld);
            AddSelectionWindowPreview(items, mousePoint);
            AddDrawingPreview(items, snappedMouseWorld);
            AddSnapMarker(items, rawMouseWorld, snappedMouseWorld);
        }

        _transientScene.Replace(items);
    }

    private void AddSelectionHighlights(List<CadTransientItem> items)
    {
        foreach (var entityId in CadEditor.Selection.EntityIds)
        {
            items.Add(new CadTransientEntityReference(
                entityId,
                CadVectorD.Zero,
                CadTransientStyle.SelectionHighlight));
        }
    }

    private void AddPastePreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        if (!_isPastePreviewActive || _clipboard is null)
            return;

        var delta = mouseWorld - _clipboard.BasePoint;
        foreach (var entityId in _clipboard.EntityIds)
            items.Add(new CadTransientEntityReference(entityId, delta, CadTransientStyle.PastePreview));

        items.Add(new CadTransientRectangle(
            _clipboard.Bounds.Translate(delta),
            CadTransientStyle.PastePreview));
    }

    private void AddSelectionWindowPreview(List<CadTransientItem> items, CadPointD mousePoint)
    {
        if (_selectionDragStart is null || (mousePoint - _selectionDragStart.Value).Length < 4)
            return;

        items.Add(new CadTransientRectangle(
            ToWorldRect(_selectionDragStart.Value, mousePoint),
            IsSelectionWindow(_selectionDragStart.Value, mousePoint)
                ? CadTransientStyle.SelectionWindow
                : CadTransientStyle.SelectionCrossing));
    }

    private void AddDrawingPreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        switch (CadCanvasToolMode)
        {
            case CadCanvasToolMode.Line when _pendingWorldPoint is { } start:
                items.Add(new CadTransientLine(start, mouseWorld, CadTransientStyle.Construction));
                break;

            case CadCanvasToolMode.Circle when _pendingWorldPoint is { } center:
                var radius = center.DistanceTo(mouseWorld);
                if (radius > 0)
                {
                    items.Add(new CadTransientCircle(center, radius, CadTransientStyle.Construction));
                    items.Add(new CadTransientLine(center, mouseWorld, CadTransientStyle.Construction));
                }
                break;

            case CadCanvasToolMode.Text:
                items.Add(new CadTransientText(
                    string.IsNullOrWhiteSpace(DrawingText) ? "Text" : DrawingText,
                    mouseWorld,
                    Math.Max(8.0 / CadEditor.Viewport.Zoom, 1.0),
                    CadTransientStyle.Construction));
                break;

            case CadCanvasToolMode.SetOrigin:
                AddOriginPositionPreview(items, mouseWorld);
                break;
        }
    }

    private void AddOriginPositionPreview(List<CadTransientItem> items, CadPointD position)
    {
        var origin = CadEditor.Document.ViewSettings.Origin;
        var halfSize = Math.Max(origin.Size, 1.0) * 0.5 / Math.Max(CadEditor.Viewport.Zoom, double.Epsilon);
        var style = CadTransientStyle.Construction with
        {
            StrokeColor = origin.Color,
            LinePattern = CadTransientLinePattern.Dash,
            StrokeWidth = origin.StrokeWidth > 0 ? origin.StrokeWidth : 1.0
        };

        switch (origin.MarkerType)
        {
            case CadOriginMarkerType.X:
                items.Add(new CadTransientLine(
                    new CadPointD(position.X - halfSize, position.Y - halfSize),
                    new CadPointD(position.X + halfSize, position.Y + halfSize),
                    style));
                items.Add(new CadTransientLine(
                    new CadPointD(position.X - halfSize, position.Y + halfSize),
                    new CadPointD(position.X + halfSize, position.Y - halfSize),
                    style));
                break;

            case CadOriginMarkerType.Circle:
                items.Add(new CadTransientCircle(position, halfSize, style));
                break;

            case CadOriginMarkerType.Square:
                items.Add(new CadTransientRectangle(
                    CadRectD.FromLTRB(
                        position.X - halfSize,
                        position.Y - halfSize,
                        position.X + halfSize,
                        position.Y + halfSize),
                    style));
                break;

            default:
                items.Add(new CadTransientLine(
                    new CadPointD(position.X - halfSize, position.Y),
                    new CadPointD(position.X + halfSize, position.Y),
                    style));
                items.Add(new CadTransientLine(
                    new CadPointD(position.X, position.Y - halfSize),
                    new CadPointD(position.X, position.Y + halfSize),
                    style));
                break;
        }
    }

    private void AddSnapMarker(List<CadTransientItem> items, CadPointD rawWorld, CadPointD snappedWorld)
    {
        var grid = CadEditor.Document.ViewSettings.Grid;
        if (grid.SnapMarkerType == CadSnapMarkerType.None || rawWorld == snappedWorld)
            return;

        var markerLength = grid.SnapMarkerLength > 0 ? grid.SnapMarkerLength : 14.0;
        var halfSize = markerLength * 0.5 / Math.Max(CadEditor.Viewport.Zoom, double.Epsilon);
        var style = CadTransientStyle.Construction with
        {
            StrokeColor = grid.SnapMarkerColor,
            LinePattern = CadTransientLinePattern.Solid,
            StrokeWidth = grid.SnapMarkerStrokeWidth > 0 ? grid.SnapMarkerStrokeWidth : 1.25
        };

        switch (grid.SnapMarkerType)
        {
            case CadSnapMarkerType.X:
                items.Add(new CadTransientLine(
                    new CadPointD(snappedWorld.X - halfSize, snappedWorld.Y - halfSize),
                    new CadPointD(snappedWorld.X + halfSize, snappedWorld.Y + halfSize),
                    style));
                items.Add(new CadTransientLine(
                    new CadPointD(snappedWorld.X - halfSize, snappedWorld.Y + halfSize),
                    new CadPointD(snappedWorld.X + halfSize, snappedWorld.Y - halfSize),
                    style));
                break;

            case CadSnapMarkerType.Square:
                items.Add(new CadTransientRectangle(
                    CadRectD.FromLTRB(
                        snappedWorld.X - halfSize,
                        snappedWorld.Y - halfSize,
                        snappedWorld.X + halfSize,
                        snappedWorld.Y + halfSize),
                    style));
                break;

            default:
                items.Add(new CadTransientLine(
                    new CadPointD(snappedWorld.X - halfSize, snappedWorld.Y),
                    new CadPointD(snappedWorld.X + halfSize, snappedWorld.Y),
                    style));
                items.Add(new CadTransientLine(
                    new CadPointD(snappedWorld.X, snappedWorld.Y - halfSize),
                    new CadPointD(snappedWorld.X, snappedWorld.Y + halfSize),
                    style));
                break;
        }
    }

    private CadPointD ScreenToWorld(CadPointD screen)
    {
        return CadEditor.Viewport.ScreenToWorld(screen);
    }

    private CadPointD ScreenToWorld(CadPointD screen, bool snapToGrid)
    {
        var world = ScreenToWorld(screen);
        return snapToGrid ? SnapWorld(world) : world;
    }

    private CadPointD SnapWorld(CadPointD world)
    {
        var grid = CadEditor.Document.ViewSettings.Grid;
        var spacingX = grid.GetSnapSpacingX();
        var spacingY = grid.GetSnapSpacingY();

        if (spacingX <= 0 || spacingY <= 0)
            return world;

        var origin = CadEditor.Document.ViewSettings.Origin.Position;
        return new CadPointD(
            origin.X + Math.Round((world.X - origin.X) / spacingX) * spacingX,
            origin.Y + Math.Round((world.Y - origin.Y) / spacingY) * spacingY);
    }

    private CadRectD ToWorldRect(CadPointD startScreen, CadPointD endScreen)
    {
        var p1 = ScreenToWorld(startScreen);
        var p2 = ScreenToWorld(endScreen);
        return CadRectD.FromLTRB(p1.X, p1.Y, p2.X, p2.Y);
    }

    private static bool IsSelectionWindow(CadPointD startScreen, CadPointD endScreen)
    {
        return endScreen.X >= startScreen.X;
    }

    private void OnDocumentChanged(object? sender, CadDocumentChangeSet e)
    {
        RequestRender();

        if (e.AffectsViewSettings)
            ViewSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool CanDuplicate(CadEntity entity)
    {
        return entity is CadLine or CadCircle or CadText;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DetachRenderResources();
        Direct2DImageRenderHost.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CadDocumentViewModel));
    }

    private sealed record ClipboardSnapshot(
        EntityId[] EntityIds,
        CadPointD BasePoint,
        CadRectD Bounds);
}
