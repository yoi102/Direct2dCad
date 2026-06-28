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
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel : ObservableObject, IDisposable
{
    private readonly CadTransientScene _transientScene = new();
    private readonly CadHandleScene _handleScene = new();
    private CadPointD? _pendingWorldPoint;
    private CadPointD? _currentMousePoint;
    private CadPointD? _lastPanPoint;
    private CadPointD? _selectionDragStart;
    private GripDragState? _activeGripDrag;
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
        _handleScene.Clear();

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
        Direct2DImageRenderHost.SetHandleScene(_handleScene);
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
            Cursor: CadCanvasCursorKind.Cross);
    }

    public CadCanvasInteractionResult PointerDown(
        CadPointD screen,
        CadCanvasPointerButton button,
        bool forcePan)
    {
        _currentMousePoint = screen;

        if (forcePan || button is CadCanvasPointerButton.Middle or CadCanvasPointerButton.Right)
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
            if (TryBeginGripDrag(screen))
                return new CadCanvasInteractionResult(true, CaptureMouse: true, Cursor: CadCanvasCursorKind.Hand);

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

        if (_activeGripDrag is not null)
        {
            _activeGripDrag.CurrentPointerWorld = ScreenToWorld(screen, snapToGrid: true);
            RequestRender();
            return new CadCanvasInteractionResult(true, Cursor: CadCanvasCursorKind.Hand);
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
                Cursor: CadCanvasCursorKind.Cross);
        }

        if (button == CadCanvasPointerButton.Left && _activeGripDrag is not null)
        {
            CommitGripDrag(screen);
            return new CadCanvasInteractionResult(
                true,
                ReleaseMouseCapture: true,
                Cursor: CadCanvasCursorKind.Cross);
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
        _activeGripDrag = null;
        return new CadCanvasInteractionResult(
            true,
            ReleaseMouseCapture: true,
            Cursor: CadCanvasCursorKind.Cross);
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
        UpdateOverlayScenes();
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
                var drawingText = ResolveDrawingText();
                CadEditor.AddText(
                    drawingText,
                    world,
                    ResolveTextBoxHeight(drawingText));
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
        _activeGripDrag = null;
        _isPastePreviewActive = false;

        if (clearClipboard)
            _clipboard = null;

        _transientScene.Clear();

        if (render)
        {
            UpdateHandleScene();
            Direct2DImageRenderHost.Render();
        }
    }

    private void UpdateOverlayScenes()
    {
        UpdateTransientScene();
        UpdateHandleScene();
    }

    private void UpdateTransientScene()
    {
        var items = new List<CadTransientItem>();

        if (_currentMousePoint is { } mousePoint)
        {
            var rawMouseWorld = ScreenToWorld(mousePoint);
            var snappedMouseWorld = SnapWorld(rawMouseWorld);
            AddPastePreview(items, snappedMouseWorld);
            AddSelectionWindowPreview(items, mousePoint);
            AddGripDragPreview(items);
            AddDrawingPreview(items, snappedMouseWorld);
            AddSnapMarker(items, rawMouseWorld, snappedMouseWorld);
        }

        _transientScene.Replace(items);
    }

    private void UpdateHandleScene()
    {
        if (_activeGripDrag is not null)
        {
            _handleScene.Clear();
            return;
        }

        var items = new List<CadHandleItem>();
        AddSelectionHandles(items);
        _handleScene.Replace(items);
    }

    private void AddSelectionHandles(List<CadHandleItem> items)
    {
        foreach (var entityId in CadEditor.Selection.EntityIds)
        {
            if (!CadEditor.Document.TryGetEntity(entityId, out var entity) ||
                entity is null ||
                entity.IsErased ||
                !entity.IsVisible ||
                !CadEditor.Document.TryGetLayer(entity.LayerId, out var layer) ||
                layer is null ||
                !layer.IsVisible ||
                layer.IsFrozen)
            {
                continue;
            }

            items.Add(new CadSelectionEntityReference(
                entityId,
                CadVectorD.Zero,
                CadHandleStyle.SelectionOutline));

            if (!entity.IsLocked)
                AddEntityGripHandles(items, entity);
        }
    }

    private static void AddEntityGripHandles(List<CadHandleItem> items, CadEntity entity)
    {
        switch (entity)
        {
            case CadLine line:
                AddGrip(items, entity.Id, line.Start, CadHandleType.Vertex);
                AddGrip(items, entity.Id, line.End, CadHandleType.Vertex);
                AddGrip(items, entity.Id, Midpoint(line.Start, line.End), CadHandleType.Center);
                break;

            case CadCircle circle:
                AddGrip(items, entity.Id, circle.Center, CadHandleType.Center);
                AddGrip(items, entity.Id, new CadPointD(circle.Center.X + circle.Radius, circle.Center.Y), CadHandleType.Radius);
                AddGrip(items, entity.Id, new CadPointD(circle.Center.X, circle.Center.Y + circle.Radius), CadHandleType.Radius);
                AddGrip(items, entity.Id, new CadPointD(circle.Center.X - circle.Radius, circle.Center.Y), CadHandleType.Radius);
                AddGrip(items, entity.Id, new CadPointD(circle.Center.X, circle.Center.Y - circle.Radius), CadHandleType.Radius);
                break;

            case CadText:
                AddBoundsGripHandles(items, entity.Id, entity.Bounds);
                break;

            default:
                if (CanMoveWithGrip(entity) && !entity.Bounds.IsEmpty)
                    AddGrip(items, entity.Id, entity.Bounds.Center, CadHandleType.Center);
                break;
        }
    }

    private static void AddBoundsGripHandles(List<CadHandleItem> items, EntityId entityId, CadRectD bounds)
    {
        if (bounds.IsEmpty)
            return;

        AddGrip(items, entityId, new CadPointD(bounds.MinX, bounds.MinY), CadHandleType.BoundsCorner);
        AddGrip(items, entityId, new CadPointD(bounds.MaxX, bounds.MinY), CadHandleType.BoundsCorner);
        AddGrip(items, entityId, new CadPointD(bounds.MaxX, bounds.MaxY), CadHandleType.BoundsCorner);
        AddGrip(items, entityId, new CadPointD(bounds.MinX, bounds.MaxY), CadHandleType.BoundsCorner);
        AddGrip(items, entityId, bounds.Center, CadHandleType.Center);
    }

    private static void AddGrip(List<CadHandleItem> items, EntityId entityId, CadPointD position, CadHandleType type)
    {
        items.Add(new CadGripHandle(entityId, position, type, CreateGripStyle(type)));
    }

    private static CadHandleStyle CreateGripStyle(CadHandleType type)
    {
        return type switch
        {
            CadHandleType.Center => CadHandleStyle.Grip with { Shape = CadHandleShape.Circle },
            CadHandleType.Radius => CadHandleStyle.Grip with { Shape = CadHandleShape.Diamond },
            _ => CadHandleStyle.Grip
        };
    }

    private static CadPointD Midpoint(CadPointD start, CadPointD end)
    {
        return new CadPointD(
            (start.X + end.X) * 0.5,
            (start.Y + end.Y) * 0.5);
    }

    private bool TryBeginGripDrag(CadPointD screen)
    {
        UpdateHandleScene();

        if (!TryHitGrip(screen, out var grip))
            return false;

        _activeGripDrag = new GripDragState(
            grip,
            ScreenToWorld(screen, snapToGrid: true));
        _selectionDragStart = null;
        _isPastePreviewActive = false;
        RequestRender();
        return true;
    }

    private bool TryHitGrip(CadPointD screen, out CadGripHandle grip)
    {
        grip = default!;
        var closestDistanceSquared = double.PositiveInfinity;

        foreach (var item in _handleScene.Items.OfType<CadGripHandle>())
        {
            var screenPosition = CadEditor.Viewport.WorldToScreen(item.Position);
            var distanceSquared = screenPosition.DistanceSquaredTo(screen);
            var hitRadius = Math.Max(item.Style.Size * 0.5 + 4.0, 7.0);
            var hitRadiusSquared = hitRadius * hitRadius;

            if (distanceSquared > hitRadiusSquared || distanceSquared >= closestDistanceSquared)
                continue;

            closestDistanceSquared = distanceSquared;
            grip = item;
        }

        return closestDistanceSquared < double.PositiveInfinity;
    }

    private void AddGripDragPreview(List<CadTransientItem> items)
    {
        if (_activeGripDrag is not { } drag ||
            !CadEditor.Document.TryGetEntity(drag.Handle.EntityId, out var entity) ||
            entity is null ||
            entity.IsErased)
        {
            return;
        }

        var style = CreateGripPreviewStyle();
        switch (entity)
        {
            case CadLine line:
                AddLineGripPreview(items, line, drag, style);
                break;

            case CadCircle circle:
                AddCircleGripPreview(items, circle, drag, style);
                break;

            case CadText text:
                AddTextGripPreview(items, text, drag, style);
                break;

            default:
                if (drag.Handle.Type == CadHandleType.Center)
                    items.Add(new CadTransientEntityReference(entity.Id, drag.Delta, style));
                break;
        }
    }

    private static void AddLineGripPreview(
        List<CadTransientItem> items,
        CadLine line,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (drag.Handle.Type == CadHandleType.Center)
        {
            items.Add(new CadTransientLine(line.Start + drag.Delta, line.End + drag.Delta, style));
            return;
        }

        var moveStart = IsLineStartGrip(line, drag.Handle.Position);
        items.Add(new CadTransientLine(
            moveStart ? drag.DraggedGripPosition : line.Start,
            moveStart ? line.End : drag.DraggedGripPosition,
            style));
    }

    private static void AddCircleGripPreview(
        List<CadTransientItem> items,
        CadCircle circle,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (drag.Handle.Type == CadHandleType.Center)
        {
            items.Add(new CadTransientCircle(circle.Center + drag.Delta, circle.Radius, style));
            return;
        }

        var radius = circle.Center.DistanceTo(drag.DraggedGripPosition);
        if (radius <= double.Epsilon)
            return;

        items.Add(new CadTransientCircle(circle.Center, radius, style));
        items.Add(new CadTransientLine(circle.Center, drag.DraggedGripPosition, style));
    }

    private void AddTextGripPreview(
        List<CadTransientItem> items,
        CadText text,
        GripDragState drag,
        CadTransientStyle style)
    {
        var grid = CadEditor.Document.ViewSettings.Grid;
        if (!TryCreateTextGripGeometry(
            text,
            drag,
            grid.GetSnapSpacingX(),
            grid.GetSnapSpacingY(),
            out var position,
            out var height))
        {
            return;
        }

        items.Add(new CadTransientText(text.Text, position, height, style));
        items.Add(new CadTransientRectangle(
            CadRectD.FromLTRB(
                position.X,
                position.Y,
                position.X + CadText.EstimateTextWidth(text.Text, height),
                position.Y + height),
            style with { FillColor = null }));
    }

    private void CommitGripDrag(CadPointD screen)
    {
        if (_activeGripDrag is not { } drag)
            return;

        _activeGripDrag = null;
        drag.CurrentPointerWorld = ScreenToWorld(screen, snapToGrid: true);

        if (drag.Delta.Length <= 1e-9)
        {
            RequestRender();
            return;
        }

        if (!CadEditor.Document.TryGetEntity(drag.Handle.EntityId, out var entity) ||
            entity is null ||
            entity.IsErased)
        {
            RequestRender();
            return;
        }

        switch (entity)
        {
            case CadLine line:
                CommitLineGripDrag(line, drag);
                break;

            case CadCircle circle:
                CommitCircleGripDrag(circle, drag);
                break;

            case CadText text:
                CommitTextGripDrag(text, drag);
                break;

            default:
                if (drag.Handle.Type == CadHandleType.Center && CanMoveWithGrip(entity))
                    CadEditor.MoveEntities([entity.Id], drag.Delta);
                break;
        }

        RequestRender();
    }

    private void CommitLineGripDrag(CadLine line, GripDragState drag)
    {
        if (drag.Handle.Type == CadHandleType.Center)
        {
            CadEditor.MoveEntities([line.Id], drag.Delta);
            return;
        }

        var moveStart = IsLineStartGrip(line, drag.Handle.Position);
        CadEditor.SetLineGeometry(
            line.Id,
            moveStart ? drag.DraggedGripPosition : line.Start,
            moveStart ? line.End : drag.DraggedGripPosition);
    }

    private void CommitCircleGripDrag(CadCircle circle, GripDragState drag)
    {
        if (drag.Handle.Type == CadHandleType.Center)
        {
            CadEditor.SetCircleGeometry(circle.Id, circle.Center + drag.Delta, circle.Radius);
            return;
        }

        var radius = circle.Center.DistanceTo(drag.DraggedGripPosition);
        if (radius > double.Epsilon)
            CadEditor.SetCircleGeometry(circle.Id, circle.Center, radius);
    }

    private void CommitTextGripDrag(CadText text, GripDragState drag)
    {
        if (drag.Handle.Type == CadHandleType.Center)
        {
            CadEditor.MoveEntities([text.Id], drag.Delta);
            return;
        }

        var grid = CadEditor.Document.ViewSettings.Grid;
        if (TryCreateTextGripGeometry(
            text,
            drag,
            grid.GetSnapSpacingX(),
            grid.GetSnapSpacingY(),
            out var position,
            out var height))
        {
            CadEditor.SetTextGeometry(text.Id, position, height, text.RotationRadians);
        }
    }

    private static bool TryCreateTextGripGeometry(
        CadText text,
        GripDragState drag,
        double snapSpacingX,
        double snapSpacingY,
        out CadPointD position,
        out double height)
    {
        position = text.Position;
        height = text.Height;

        if (drag.Handle.Type == CadHandleType.Center)
        {
            position = text.Position + drag.Delta;
            return true;
        }

        if (drag.Handle.Type != CadHandleType.BoundsCorner || text.Bounds.IsEmpty)
            return false;

        var bounds = text.Bounds;
        var target = drag.DraggedGripPosition;
        var dragLeft = Math.Abs(drag.Handle.Position.X - bounds.MinX) <= Math.Abs(drag.Handle.Position.X - bounds.MaxX);
        var dragBottom = Math.Abs(drag.Handle.Position.Y - bounds.MinY) <= Math.Abs(drag.Handle.Position.Y - bounds.MaxY);
        var oppositeX = dragLeft ? bounds.MaxX : bounds.MinX;
        var oppositeY = dragBottom ? bounds.MaxY : bounds.MinY;
        var widthFactor = CadText.EstimateTextWidth(text.Text, 1.0);
        var desiredHeight = Math.Abs(target.Y - oppositeY);
        var desiredWidth = Math.Abs(target.X - oppositeX);

        height = SnapTextHeightUp(text.Text, Math.Max(desiredHeight, desiredWidth / widthFactor), snapSpacingX, snapSpacingY);
        var width = height * widthFactor;
        position = new CadPointD(
            dragLeft ? oppositeX - width : oppositeX,
            dragBottom ? oppositeY - height : oppositeY);
        return true;
    }

    private static CadTransientStyle CreateGripPreviewStyle()
    {
        return CadTransientStyle.Construction with
        {
            StrokeColor = CadColor.FromArgb(245, 255, 214, 92),
            StrokeWidth = 1.4,
            LinePattern = CadTransientLinePattern.Dash,
            FillColor = CadColor.FromArgb(22, 255, 214, 92)
        };
    }

    private static bool IsLineStartGrip(CadLine line, CadPointD gripPosition)
    {
        return line.Start.DistanceSquaredTo(gripPosition) <= line.End.DistanceSquaredTo(gripPosition);
    }

    private static bool CanMoveWithGrip(CadEntity entity)
    {
        return entity is CadLine or CadCircle or CadArc or CadPolyline or CadText or CadBlockReference;
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
                var drawingText = ResolveDrawingText();
                items.Add(new CadTransientText(
                    drawingText,
                    mouseWorld,
                    ResolveTextBoxHeight(drawingText),
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

    private string ResolveDrawingText()
    {
        return string.IsNullOrWhiteSpace(DrawingText) ? "Text" : DrawingText;
    }

    private double ResolveTextBoxHeight(string text)
    {
        var grid = CadEditor.Document.ViewSettings.Grid;
        var spacingY = grid.GetSnapSpacingY();
        return IsFinitePositive(spacingY)
            ? SnapTextHeightUp(text, spacingY, grid.GetSnapSpacingX(), spacingY) * 5
            : Math.Max(8.0 / Math.Max(CadEditor.Viewport.Zoom, double.Epsilon) * 5, 1.0);
    }

    private static double SnapTextHeightUp(
        string text,
        double desiredHeight,
        double snapSpacingX,
        double snapSpacingY)
    {
        var heightStep = IsFinitePositive(snapSpacingY)
            ? snapSpacingY
            : IsFinitePositive(snapSpacingX)
                ? snapSpacingX
                : 1.0;
        var startStep = Math.Max(1, (int)Math.Ceiling(Math.Max(desiredHeight, heightStep) / heightStep));

        for (var offset = 0; offset < 128; offset++)
        {
            var height = heightStep * (startStep + offset);
            if (IsDimensionAligned(CadText.EstimateTextWidth(text, height), snapSpacingX))
                return height;
        }

        return heightStep * startStep;
    }

    private static bool IsDimensionAligned(double value, double step)
    {
        if (!IsFinitePositive(step))
            return true;

        var units = value / step;
        return Math.Abs(units - Math.Round(units)) <= 1e-6;
    }

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
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

    private sealed class GripDragState
    {
        public GripDragState(CadGripHandle handle, CadPointD pointerWorld)
        {
            Handle = handle;
            StartPointerWorld = pointerWorld;
            CurrentPointerWorld = pointerWorld;
        }

        public CadGripHandle Handle { get; }
        public CadPointD StartPointerWorld { get; }
        public CadPointD CurrentPointerWorld { get; set; }
        public CadVectorD Delta => CurrentPointerWorld - StartPointerWorld;
        public CadPointD DraggedGripPosition => Handle.Position + Delta;
    }

    private sealed record ClipboardSnapshot(
        EntityId[] EntityIds,
        CadPointD BasePoint,
        CadRectD Bounds);
}
