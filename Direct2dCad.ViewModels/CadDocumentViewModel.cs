using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel : ObservableObject, IDisposable
{
    private const double TwoPi = Math.PI * 2.0;
    private readonly CadTransientScene _transientScene = new();
    private readonly CadHandleScene _handleScene = new();
    private readonly CadHandleSceneBuilder _handleSceneBuilder = new();
    private readonly CadHandleHitTester _handleHitTester = new();
    private readonly List<CadPointD> _pendingPolylinePoints = [];
    private readonly List<CadPointD> _pendingPolygonPoints = [];
    private readonly List<CadPointD> _pendingSplinePoints = [];
    private CadPointD? _pendingWorldPoint;
    private CadPointD? _pendingArcStartPoint;
    private CadPointD? _currentMousePoint;
    private CadPointD? _lastPanPoint;
    private CadPointD? _selectionDragStart;
    private GripDragState? _activeGripDrag;
    private ClipboardSnapshot? _clipboard;
    private bool _isPastePreviewActive;
    private bool _isRenderAttached;
    private bool _isApplyingTextMeasurementChanges;
    private bool _disposed;
    private double _viewportWidth = 1.0;
    private double _viewportHeight = 1.0;
    private CadRenderInvalidation _lastOverlayInvalidation = CadRenderInvalidation.FromScreenRect(default);

    [ObservableProperty]
    public partial CadEditor CadEditor { get; private set; } = new(CadDocument.Create("Untitled"));

    [ObservableProperty]
    public partial Direct2DImageRenderHost Direct2DImageRenderHost { get; private set; } = new();

    [ObservableProperty]
    public partial CadCanvasToolMode CadCanvasToolMode { get; internal set; } = CadCanvasToolMode.Select;

    [ObservableProperty]
    public partial string DrawingText { get; set; } = "Text";

    [ObservableProperty]
    public partial bool DrawingTextInverted { get; set; }

    [ObservableProperty]
    public partial double DrawingTextInvertedMarginFactor { get; set; } = CadText.DefaultInvertedMarginFactor;

    [ObservableProperty]
    public partial ViewModelCadShapeFont DrawingShapeFont { get; set; } = ViewModelCadShapeFont.Unicode;

    public event EventHandler? ViewSettingsChanged;

    public bool IsPanning { get; private set; }
    public CadUserSettings UserSettings { get; private set; } = CadUserSettings.CreateDefault();

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

    public void ApplyUserSettings(CadUserSettings? settings)
    {
        UserSettings = settings ?? CadUserSettings.CreateDefault();
        UserSettings.Normalize();
        RequestRender();
    }

    partial void OnDrawingTextInvertedChanged(bool value)
    {
        RequestRender();
    }

    partial void OnDrawingTextInvertedMarginFactorChanged(double value)
    {
        RequestRender();
    }

    partial void OnDrawingShapeFontChanged(ViewModelCadShapeFont value)
    {
        RequestRender();
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
        var requiresFullRender = false;

        if (IsPanning && _lastPanPoint is not null)
        {
            var delta = screen - _lastPanPoint.Value;
            _lastPanPoint = screen;
            CadEditor.Execute(new PanViewportCommand(delta));
            requiresFullRender = true;
        }

        if (_activeGripDrag is not null)
        {
            _activeGripDrag.CurrentPointerWorld = ScreenToWorld(screen, snapToGrid: true);
            if (requiresFullRender)
                RequestRender();
            else
                RequestOverlayRender();
            return new CadCanvasInteractionResult(true, Cursor: CadCanvasCursorKind.Hand);
        }

        if (requiresFullRender)
            RequestRender();
        else
            RequestOverlayRender();
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
        _pendingPolylinePoints.Clear();
        _pendingPolygonPoints.Clear();
        _pendingSplinePoints.Clear();
        _selectionDragStart = null;
        RequestRender();
        return new CadCanvasInteractionResult(true, Cursor: CadCanvasCursorKind.Cross);
    }

    public CadCanvasInteractionResult CompleteCurrentDrawing()
    {
        switch (CadCanvasToolMode)
        {
            case CadCanvasToolMode.Polyline when _pendingPolylinePoints.Count >= 2:
                CompletePolyline();
                RequestRender();
                return CadCanvasInteractionResult.HandledOnly;

            case CadCanvasToolMode.Polygon when _pendingPolygonPoints.Count >= 3:
                CadEditor.AddPolygon(_pendingPolygonPoints);
                _pendingPolygonPoints.Clear();
                RequestRender();
                return CadCanvasInteractionResult.HandledOnly;

            case CadCanvasToolMode.Spline when _pendingSplinePoints.Count >= 2:
                CompleteSpline();
                RequestRender();
                return CadCanvasInteractionResult.HandledOnly;

            default:
                return CadCanvasInteractionResult.NotHandled;
        }
    }

    public void RequestRender()
    {
        RequestRender(CadRenderInvalidation.Full);
    }

    private void RequestOverlayRender()
    {
        RequestRender(CadRenderInvalidation.FromScreenRect(default));
    }

    private void RequestRender(CadRenderInvalidation? invalidation)
    {
        UpdateTextMeasurements();
        var overlayInvalidation = UpdateOverlayScenesAndCreateInvalidation();
        var effectiveInvalidation = (invalidation ?? CadRenderInvalidation.Full).Union(overlayInvalidation);
        Direct2DImageRenderHost.SetRenderOptions(CreateRenderOptions());
        Direct2DImageRenderHost.Render(effectiveInvalidation);
    }

    private void UpdateTextMeasurements()
    {
        if (!_isRenderAttached || _isApplyingTextMeasurementChanges)
            return;

        var changes = Direct2DImageRenderHost.UpdateTextMeasurements(CadEditor.Document);
        if (!changes.DocumentChanged)
            return;

        try
        {
            _isApplyingTextMeasurementChanges = true;
            CadEditor.PublishDocumentChanges(changes);
        }
        finally
        {
            _isApplyingTextMeasurementChanges = false;
        }
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

            case CadCanvasToolMode.Ellipse:
                if (_pendingWorldPoint is null)
                    _pendingWorldPoint = world;
                else
                {
                    var bounds = CadRectD.FromLTRB(
                        _pendingWorldPoint.Value.X,
                        _pendingWorldPoint.Value.Y,
                        world.X,
                        world.Y);
                    if (TryCreateEllipseGeometry(bounds, out var center, out var radiusX, out var radiusY))
                        CadEditor.AddEllipse(center, radiusX, radiusY);

                    _pendingWorldPoint = null;
                }
                RequestRender();
                break;

            case CadCanvasToolMode.Arc:
                if (_pendingWorldPoint is null)
                {
                    _pendingWorldPoint = world;
                }
                else if (_pendingArcStartPoint is null)
                {
                    if (_pendingWorldPoint.Value.DistanceTo(world) > double.Epsilon)
                        _pendingArcStartPoint = world;
                }
                else
                {
                    if (TryCreateArcGeometry(
                        _pendingWorldPoint.Value,
                        _pendingArcStartPoint.Value,
                        world,
                        out var radius,
                        out var startAngleRadians,
                        out var sweepAngleRadians))
                    {
                        CadEditor.AddArc(
                            _pendingWorldPoint.Value,
                            radius,
                            startAngleRadians,
                            sweepAngleRadians);
                    }

                    _pendingWorldPoint = null;
                    _pendingArcStartPoint = null;
                }
                RequestRender();
                break;

            case CadCanvasToolMode.Rectangle:
                if (_pendingWorldPoint is null)
                    _pendingWorldPoint = world;
                else
                {
                    var bounds = CadRectD.FromLTRB(
                        _pendingWorldPoint.Value.X,
                        _pendingWorldPoint.Value.Y,
                        world.X,
                        world.Y);
                    if (IsValidRectangleBounds(bounds))
                        CadEditor.AddRectangle(bounds);

                    _pendingWorldPoint = null;
                }
                RequestRender();
                break;

            case CadCanvasToolMode.Polyline:
                AddPolylineVertexOrComplete(world);
                RequestRender();
                break;

            case CadCanvasToolMode.Polygon:
                AddPolygonVertexOrComplete(world);
                RequestRender();
                break;

            case CadCanvasToolMode.Spline:
                AddSplineFitPointOrComplete(world);
                RequestRender();
                break;

            case CadCanvasToolMode.Text:
                var drawingText = ResolveDrawingText();
                CadEditor.AddText(
                    drawingText,
                    world,
                    ResolveTextBoxHeight(drawingText),
                    isInverted: DrawingTextInverted,
                    invertedMarginFactor: ResolveDrawingTextInvertedMarginFactor());
                RequestRender();
                break;

            case CadCanvasToolMode.ShapeText:
                var shapeText = ResolveDrawingText();
                CadEditor.AddShapeText(
                    shapeText,
                    world,
                    ResolveTextBoxHeight(shapeText),
                    isInverted: DrawingTextInverted,
                    invertedMarginFactor: ResolveDrawingTextInvertedMarginFactor(),
                    shapeFontId: ResolveDrawingShapeFontId());
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
        _pendingArcStartPoint = null;
        _pendingPolylinePoints.Clear();
        _pendingPolygonPoints.Clear();
        _pendingSplinePoints.Clear();
        _selectionDragStart = null;
        _activeGripDrag = null;
        _isPastePreviewActive = false;

        if (clearClipboard)
            _clipboard = null;

        _transientScene.Clear();

        if (render)
        {
            RequestRender();
        }
    }

    private void UpdateOverlayScenes()
    {
        UpdateTransientScene();
        UpdateHandleScene();
    }

    private CadRenderInvalidation UpdateOverlayScenesAndCreateInvalidation()
    {
        var previousOverlay = _lastOverlayInvalidation;
        UpdateOverlayScenes();
        var currentOverlay = CreateOverlayInvalidation();
        _lastOverlayInvalidation = currentOverlay;
        return previousOverlay.Union(currentOverlay);
    }

    private CadRenderOptions CreateRenderOptions()
    {
        return new CadRenderOptions
        {
            IsAntialiasingEnabled = UserSettings.Rendering.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = UserSettings.Rendering.IsTextAntialiasingEnabled,
            HiddenEntityIds = _activeGripDrag is null
                ? new HashSet<EntityId>()
                : ResolveGripDragEntityIds(_activeGripDrag).ToHashSet()
        };
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

        var items = _handleSceneBuilder.BuildSelectionHandles(
            CadEditor.Document,
            CadEditor.Selection.EntityIds,
            CreateHandleSceneBuildOptions());
        _handleScene.Replace(items);
    }

    private CadRenderInvalidation CreateOverlayInvalidation()
    {
        var invalidation = CadRenderInvalidation.FromScreenRect(default);

        foreach (var item in _transientScene.Items)
            invalidation = invalidation.Union(CreateTransientInvalidation(item));

        foreach (var item in _handleScene.Items)
            invalidation = invalidation.Union(CreateHandleInvalidation(item));

        return invalidation;
    }

    private CadRenderInvalidation CreateTransientInvalidation(CadTransientItem item)
    {
        return item switch
        {
            CadTransientLine line => CreateWorldBoundsInvalidation(BoundsFromPoints(line.Start, line.End)),
            CadTransientCircle circle when circle.Radius > 0 => CreateWorldBoundsInvalidation(CadRectD.FromCenter(circle.Center, circle.Radius * 2, circle.Radius * 2)),
            CadTransientEllipse ellipse when ellipse.RadiusX > 0 && ellipse.RadiusY > 0 => CreateWorldBoundsInvalidation(CadRectD.FromCenter(ellipse.Center, ellipse.RadiusX * 2, ellipse.RadiusY * 2)),
            CadTransientArc arc when arc.Radius > 0 => CreateWorldBoundsInvalidation(CadRectD.FromCenter(arc.Center, arc.Radius * 2, arc.Radius * 2)),
            CadTransientPolyline polyline => CreateWorldBoundsInvalidation(BoundsFromPoints(polyline.Points)),
            CadTransientSpline spline => CreateWorldBoundsInvalidation(BoundsFromPoints(spline.FitPoints)),
            CadTransientRectangle rectangle => CreateWorldBoundsInvalidation(rectangle.Bounds),
            CadTransientText text => CreateWorldBoundsInvalidation(ResolveTransientTextBounds(text)),
            CadTransientShapeText text => CreateWorldBoundsInvalidation(ResolveTransientShapeTextBounds(text)),
            CadTransientEntityReference reference => CreateEntityReferenceInvalidation(reference.EntityId, reference.Offset),
            _ => CadRenderInvalidation.FromScreenRect(default)
        };
    }

    private CadRenderInvalidation CreateHandleInvalidation(CadHandleItem item)
    {
        return item switch
        {
            CadSelectionEntityReference reference => CreateEntityReferenceInvalidation(reference.EntityId, reference.Offset),
            CadGripHandle grip => CreateScreenPointInvalidation(
                CadEditor.Viewport.WorldToScreen(grip.Position),
                Math.Max(grip.Style.Size, grip.Style.StrokeWidth) + 4.0),
            _ => CadRenderInvalidation.FromScreenRect(default)
        };
    }

    private CadRenderInvalidation CreateEntityReferenceInvalidation(EntityId entityId, CadVectorD offset)
    {
        return CadEditor.Document.TryGetEntity(entityId, out var entity) && entity is not null
            ? CreateWorldBoundsInvalidation(entity.Bounds.Translate(offset))
            : CadRenderInvalidation.FromScreenRect(default);
    }

    private CadRenderInvalidation CreateWorldBoundsInvalidation(CadRectD bounds, double paddingPixels = 8.0)
    {
        return CadRenderInvalidation.FromWorldBounds(
            CadEditor.Viewport,
            bounds,
            Direct2DImageRenderHost.TargetWidth,
            Direct2DImageRenderHost.TargetHeight,
            paddingPixels);
    }

    private CadRenderInvalidation CreateScreenPointInvalidation(CadPointD screenPoint, double radiusPixels)
    {
        var radius = Math.Max(1.0, radiusPixels);
        return CadRenderInvalidation.FromScreenRect(new CadScreenRect(
            Math.Max(0, (int)Math.Floor(screenPoint.X - radius)),
            Math.Max(0, (int)Math.Floor(screenPoint.Y - radius)),
            (int)Math.Ceiling(radius * 2),
            (int)Math.Ceiling(radius * 2)));
    }

    private static CadRectD ResolveTransientTextBounds(CadTransientText text)
    {
        return text.IsInverted
            ? text.Bounds.Inflate(text.Height * Math.Max(0, text.InvertedMarginFactor))
            : text.Bounds;
    }

    private static CadRectD ResolveTransientShapeTextBounds(CadTransientShapeText text)
    {
        var bounds = CadShapeFontMetrics.MeasureBounds(
            text.Text,
            text.Position,
            text.Height,
            text.WidthFactor,
            text.CharacterSpacingFactor,
            text.ObliqueAngleRadians,
            text.RotationRadians,
            text.ShapeFontId);

        return text.IsInverted
            ? bounds.Inflate(text.Height * Math.Max(0, text.InvertedMarginFactor))
            : bounds;
    }

    private static CadRectD BoundsFromPoints(CadPointD first, CadPointD second)
    {
        return CadRectD.Empty
            .ExpandToInclude(first)
            .ExpandToInclude(second);
    }

    private static CadRectD BoundsFromPoints(IEnumerable<CadPointD> points)
    {
        var bounds = CadRectD.Empty;
        foreach (var point in points)
            bounds = bounds.ExpandToInclude(point);

        return bounds;
    }

    private bool TryBeginGripDrag(CadPointD screen)
    {
        UpdateHandleScene();

        if (!_handleHitTester.TryHitGrip(_handleScene, CadEditor.Viewport.WorldToScreen, screen, out var grip))
            return false;

        _activeGripDrag = new GripDragState(
            grip,
            ScreenToWorld(screen, snapToGrid: true));
        _selectionDragStart = null;
        _isPastePreviewActive = false;
        RequestRender();
        return true;
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
        if (drag.Handle.Type == CadHandleType.Center)
        {
            AddMoveGripPreview(items, drag, style);
            return;
        }

        switch (entity)
        {
            case CadLine line:
                AddLineGripPreview(items, line, drag, style);
                break;

            case CadCircle circle:
                AddCircleGripPreview(items, circle, drag, style);
                break;

            case CadEllipse ellipse:
                AddEllipseGripPreview(items, ellipse, drag, style);
                break;

            case CadArc arc:
                AddArcGripPreview(items, arc, drag, style);
                break;

            case CadRectangle rectangle:
                AddRectangleGripPreview(items, rectangle, drag, style);
                break;

            case CadPolyline polyline:
                AddPolylineGripPreview(items, polyline, drag, style);
                break;

            case CadSpline spline:
                AddSplineGripPreview(items, spline, drag, style);
                break;

            case CadText text:
                AddTextGripPreview(items, text, drag, style);
                break;

            case CadShapeText shapeText:
                AddShapeTextGripPreview(items, shapeText, drag, style);
                break;
        }
    }

    private void AddMoveGripPreview(
        List<CadTransientItem> items,
        GripDragState drag,
        CadTransientStyle style)
    {
        foreach (var entityId in ResolveGripDragEntityIds(drag))
            items.Add(new CadTransientEntityReference(entityId, drag.Delta, style));
    }

    private static void AddLineGripPreview(
        List<CadTransientItem> items,
        CadLine line,
        GripDragState drag,
        CadTransientStyle style)
    {
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
        var radius = circle.Center.DistanceTo(drag.DraggedGripPosition);
        if (radius <= double.Epsilon)
            return;

        items.Add(new CadTransientCircle(circle.Center, radius, style));
        items.Add(new CadTransientLine(circle.Center, drag.DraggedGripPosition, style));
    }

    private static void AddEllipseGripPreview(
        List<CadTransientItem> items,
        CadEllipse ellipse,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (!TryCreateEllipseGripGeometry(ellipse, drag, out var center, out var radiusX, out var radiusY))
            return;

        items.Add(new CadTransientEllipse(center, radiusX, radiusY, style));
        items.Add(new CadTransientLine(center, drag.DraggedGripPosition, style));
    }

    private static void AddArcGripPreview(
        List<CadTransientItem> items,
        CadArc arc,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (!TryCreateArcGripGeometry(arc, drag, out var center, out var radius, out var startAngle, out var sweepAngle))
            return;

        items.Add(new CadTransientArc(center, radius, startAngle, sweepAngle, style));
        items.Add(new CadTransientLine(center, GetArcPoint(center, radius, startAngle), style));
        items.Add(new CadTransientLine(center, GetArcPoint(center, radius, startAngle + sweepAngle), style));
    }

    private static void AddRectangleGripPreview(
        List<CadTransientItem> items,
        CadRectangle rectangle,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (TryCreateRectangleGripGeometry(rectangle, drag, out var bounds))
            items.Add(new CadTransientRectangle(bounds, style));
    }

    private static void AddPolylineGripPreview(
        List<CadTransientItem> items,
        CadPolyline polyline,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (TryCreatePolylineGripGeometry(polyline, drag, out var points, out var closed))
            items.Add(new CadTransientPolyline(points, closed, style));
    }

    private static void AddSplineGripPreview(
        List<CadTransientItem> items,
        CadSpline spline,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (TryCreateSplineGripGeometry(spline, drag, out var fitPoints, out var closed))
            items.Add(new CadTransientSpline(fitPoints, closed, style));
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

        var bounds = CreateTextBounds(text.Text, position, height, text.TextStyleId);
        items.Add(new CadTransientText(
            text.Text,
            position,
            height,
            bounds,
            style,
            text.IsInverted,
            text.InvertedMarginFactor));
        items.Add(new CadTransientRectangle(
            text.IsInverted ? bounds.Inflate(height * text.InvertedMarginFactor) : bounds,
            style with { FillColor = null }));
    }

    private void AddShapeTextGripPreview(
        List<CadTransientItem> items,
        CadShapeText text,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (!TryCreateShapeTextGripGeometry(text, drag, out var position, out var height))
            return;

        items.Add(new CadTransientShapeText(
            text.Text,
            position,
            height,
            text.RotationRadians,
            text.WidthFactor,
            text.CharacterSpacingFactor,
            text.ObliqueAngleRadians,
            style,
            text.IsInverted,
            text.InvertedMarginFactor,
            text.ShapeFontId));
        items.Add(new CadTransientRectangle(
            CreateShapeTextPreviewBounds(
                text.Text,
                position,
                height,
                text.WidthFactor,
                text.CharacterSpacingFactor,
                text.ObliqueAngleRadians,
                text.RotationRadians,
                text.IsInverted,
                text.InvertedMarginFactor,
                text.ShapeFontId),
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

        if (drag.Handle.Type == CadHandleType.Center)
        {
            CommitMoveGripDrag(drag);
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

            case CadEllipse ellipse:
                CommitEllipseGripDrag(ellipse, drag);
                break;

            case CadArc arc:
                CommitArcGripDrag(arc, drag);
                break;

            case CadRectangle rectangle:
                CommitRectangleGripDrag(rectangle, drag);
                break;

            case CadPolyline polyline:
                CommitPolylineGripDrag(polyline, drag);
                break;

            case CadSpline spline:
                CommitSplineGripDrag(spline, drag);
                break;

            case CadText text:
                CommitTextGripDrag(text, drag);
                break;

            case CadShapeText shapeText:
                CommitShapeTextGripDrag(shapeText, drag);
                break;
        }

        RequestRender();
    }

    private void CommitMoveGripDrag(GripDragState drag)
    {
        var entityIds = ResolveGripDragEntityIds(drag).ToArray();
        if (entityIds.Length > 0)
            CadEditor.MoveEntities(entityIds, drag.Delta);
    }

    private void CommitLineGripDrag(CadLine line, GripDragState drag)
    {
        var moveStart = IsLineStartGrip(line, drag.Handle.Position);
        CadEditor.SetLineGeometry(
            line.Id,
            moveStart ? drag.DraggedGripPosition : line.Start,
            moveStart ? line.End : drag.DraggedGripPosition);
    }

    private void CommitCircleGripDrag(CadCircle circle, GripDragState drag)
    {
        var radius = circle.Center.DistanceTo(drag.DraggedGripPosition);
        if (radius > double.Epsilon)
            CadEditor.SetCircleGeometry(circle.Id, circle.Center, radius);
    }

    private void CommitEllipseGripDrag(CadEllipse ellipse, GripDragState drag)
    {
        if (TryCreateEllipseGripGeometry(ellipse, drag, out var center, out var radiusX, out var radiusY))
            CadEditor.SetEllipseGeometry(ellipse.Id, center, radiusX, radiusY);
    }

    private void CommitArcGripDrag(CadArc arc, GripDragState drag)
    {
        if (TryCreateArcGripGeometry(arc, drag, out var center, out var radius, out var startAngle, out var sweepAngle))
            CadEditor.SetArcGeometry(arc.Id, center, radius, startAngle, sweepAngle);
    }

    private void CommitRectangleGripDrag(CadRectangle rectangle, GripDragState drag)
    {
        if (TryCreateRectangleGripGeometry(rectangle, drag, out var bounds))
            CadEditor.SetRectangleGeometry(rectangle.Id, bounds);
    }

    private void CommitPolylineGripDrag(CadPolyline polyline, GripDragState drag)
    {
        if (TryCreatePolylineGripGeometry(polyline, drag, out var points, out var closed))
            CadEditor.SetPolylineGeometry(polyline.Id, points, closed);
    }

    private void CommitSplineGripDrag(CadSpline spline, GripDragState drag)
    {
        if (TryCreateSplineGripGeometry(spline, drag, out var fitPoints, out var closed))
            CadEditor.SetSplineGeometry(spline.Id, fitPoints, closed);
    }

    private void CommitTextGripDrag(CadText text, GripDragState drag)
    {
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

    private void CommitShapeTextGripDrag(CadShapeText text, GripDragState drag)
    {
        if (TryCreateShapeTextGripGeometry(text, drag, out var position, out var height))
        {
            CadEditor.SetShapeTextGeometry(
                text.Id,
                position,
                height,
                text.RotationRadians,
                text.WidthFactor,
                text.CharacterSpacingFactor,
                text.ObliqueAngleRadians);
        }
    }

    private bool TryCreateTextGripGeometry(
        CadText text,
        GripDragState drag,
        double snapSpacingX,
        double snapSpacingY,
        out CadPointD position,
        out double height)
    {
        position = text.Position;
        height = text.Height;

        if (drag.Handle.Type != CadHandleType.BoundsCorner || text.Bounds.IsEmpty)
            return false;

        var bounds = text.Bounds;
        var target = drag.DraggedGripPosition;
        var dragLeft = Math.Abs(drag.Handle.Position.X - bounds.MinX) <= Math.Abs(drag.Handle.Position.X - bounds.MaxX);
        var dragBottom = Math.Abs(drag.Handle.Position.Y - bounds.MinY) <= Math.Abs(drag.Handle.Position.Y - bounds.MaxY);
        var oppositeX = dragLeft ? bounds.MaxX : bounds.MinX;
        var oppositeY = dragBottom ? bounds.MaxY : bounds.MinY;
        var widthFactor = GetCachedTextWidthFactor(text);
        var marginFactor = text.IsInverted ? text.InvertedMarginFactor : 0;
        var heightScale = 1.0 + marginFactor * 2.0;
        var widthScale = widthFactor + marginFactor * 2.0;
        var desiredHeight = Math.Abs(target.Y - oppositeY);
        var desiredWidth = Math.Abs(target.X - oppositeX);

        height = SnapTextHeightUp(
            text.Text,
            Math.Max(desiredHeight / heightScale, desiredWidth / widthScale),
            snapSpacingX,
            snapSpacingY,
            text.TextStyleId);
        var width = MeasureTextWidth(text.Text, height, text.TextStyleId);
        var margin = height * marginFactor;
        var outerWidth = width + margin * 2.0;
        var outerHeight = height + margin * 2.0;
        position = new CadPointD(
            (dragLeft ? oppositeX - outerWidth : oppositeX) + margin,
            (dragBottom ? oppositeY - outerHeight : oppositeY) + margin);
        return true;
    }

    private static bool TryCreateShapeTextGripGeometry(
        CadShapeText text,
        GripDragState drag,
        out CadPointD position,
        out double height)
    {
        position = text.Position;
        height = text.Height;

        if (drag.Handle.Type != CadHandleType.BoundsCorner || text.Bounds.IsEmpty)
            return false;

        var bounds = text.Bounds;
        var target = drag.DraggedGripPosition;
        var dragLeft = Math.Abs(drag.Handle.Position.X - bounds.MinX) <= Math.Abs(drag.Handle.Position.X - bounds.MaxX);
        var dragBottom = Math.Abs(drag.Handle.Position.Y - bounds.MinY) <= Math.Abs(drag.Handle.Position.Y - bounds.MaxY);
        var oppositeX = dragLeft ? bounds.MaxX : bounds.MinX;
        var oppositeY = dragBottom ? bounds.MaxY : bounds.MinY;
        var widthFactor = GetCachedShapeTextWidthFactor(text);
        var marginFactor = text.IsInverted ? text.InvertedMarginFactor : 0;
        var heightScale = 1.0 + marginFactor * 2.0;
        var widthScale = widthFactor + marginFactor * 2.0;
        var desiredHeight = Math.Abs(target.Y - oppositeY);
        var desiredWidth = Math.Abs(target.X - oppositeX);

        height = Math.Max(desiredHeight / heightScale, desiredWidth / widthScale);
        if (!IsFinitePositive(height))
            return false;

        var width = Math.Max(text.TextBounds.Width / Math.Max(text.Height, double.Epsilon) * height, height * text.WidthFactor);
        var margin = height * marginFactor;
        var outerWidth = width + margin * 2.0;
        var outerHeight = height + margin * 2.0;
        position = new CadPointD(
            (dragLeft ? oppositeX - outerWidth : oppositeX) + margin,
            (dragBottom ? oppositeY - outerHeight : oppositeY) + margin);
        return true;
    }

    private static bool TryCreateRectangleGripGeometry(
        CadRectangle rectangle,
        GripDragState drag,
        out CadRectD bounds)
    {
        bounds = rectangle.Bounds;

        if (drag.Handle.Type != CadHandleType.BoundsCorner || rectangle.Bounds.IsEmpty)
            return false;

        var oldBounds = rectangle.Bounds;
        var target = drag.DraggedGripPosition;
        var dragLeft = Math.Abs(drag.Handle.Position.X - oldBounds.MinX) <= Math.Abs(drag.Handle.Position.X - oldBounds.MaxX);
        var dragBottom = Math.Abs(drag.Handle.Position.Y - oldBounds.MinY) <= Math.Abs(drag.Handle.Position.Y - oldBounds.MaxY);
        var oppositeX = dragLeft ? oldBounds.MaxX : oldBounds.MinX;
        var oppositeY = dragBottom ? oldBounds.MaxY : oldBounds.MinY;

        bounds = CadRectD.FromLTRB(oppositeX, oppositeY, target.X, target.Y);
        return IsValidRectangleBounds(bounds);
    }

    private static bool TryCreateEllipseGripGeometry(
        CadEllipse ellipse,
        GripDragState drag,
        out CadPointD center,
        out double radiusX,
        out double radiusY)
    {
        center = ellipse.Center;
        radiusX = ellipse.RadiusX;
        radiusY = ellipse.RadiusY;

        if (drag.Handle.Type != CadHandleType.Radius)
            return false;

        var isHorizontalRadiusGrip =
            Math.Abs(drag.Handle.Position.X - ellipse.Center.X) >=
            Math.Abs(drag.Handle.Position.Y - ellipse.Center.Y);

        if (isHorizontalRadiusGrip)
            radiusX = Math.Abs(drag.DraggedGripPosition.X - ellipse.Center.X);
        else
            radiusY = Math.Abs(drag.DraggedGripPosition.Y - ellipse.Center.Y);

        return IsValidEllipseGeometry(radiusX, radiusY);
    }

    private static bool TryCreatePolylineGripGeometry(
        CadPolyline polyline,
        GripDragState drag,
        out CadPointD[] points,
        out bool closed)
    {
        points = polyline.Points.ToArray();
        closed = polyline.Closed;

        if (drag.Handle.Type != CadHandleType.Vertex || points.Length < 2)
            return false;

        var vertexIndex = FindNearestPointIndex(points, drag.Handle.Position);
        if (vertexIndex < 0)
            return false;

        points[vertexIndex] = drag.DraggedGripPosition;
        return !closed || points.Length >= 3;
    }

    private static bool TryCreateSplineGripGeometry(
        CadSpline spline,
        GripDragState drag,
        out CadPointD[] fitPoints,
        out bool closed)
    {
        fitPoints = spline.FitPoints.ToArray();
        closed = spline.Closed;

        if (drag.Handle.Type != CadHandleType.Vertex || fitPoints.Length < 2)
            return false;

        var fitPointIndex = FindNearestPointIndex(fitPoints, drag.Handle.Position);
        if (fitPointIndex < 0)
            return false;

        fitPoints[fitPointIndex] = drag.DraggedGripPosition;
        return !closed || fitPoints.Length >= 3;
    }

    private static int FindNearestPointIndex(IReadOnlyList<CadPointD> points, CadPointD target)
    {
        var index = -1;
        var bestDistance = double.PositiveInfinity;

        for (var i = 0; i < points.Count; i++)
        {
            var distance = points[i].DistanceSquaredTo(target);
            if (distance >= bestDistance)
                continue;

            index = i;
            bestDistance = distance;
        }

        return index;
    }

    private static bool TryCreateArcGripGeometry(
        CadArc arc,
        GripDragState drag,
        out CadPointD center,
        out double radius,
        out double startAngleRadians,
        out double sweepAngleRadians)
    {
        center = arc.Center;
        radius = arc.Radius;
        startAngleRadians = arc.StartAngleRadians;
        sweepAngleRadians = arc.SweepAngleRadians;

        if (drag.Handle.Type == CadHandleType.Radius)
        {
            radius = center.DistanceTo(drag.DraggedGripPosition);
            return radius > double.Epsilon;
        }

        if (drag.Handle.Type != CadHandleType.Vertex)
            return false;

        var targetRadius = center.DistanceTo(drag.DraggedGripPosition);
        if (targetRadius <= double.Epsilon)
            return false;

        radius = targetRadius;
        var targetAngle = AngleFrom(center, drag.DraggedGripPosition);
        if (IsArcStartGrip(arc, drag.Handle.Position))
        {
            startAngleRadians = targetAngle;
            sweepAngleRadians = ResolveSweepAngle(
                startAngleRadians,
                arc.EndAngleRadians,
                arc.SweepAngleRadians >= 0);
        }
        else
        {
            sweepAngleRadians = ResolveSweepAngle(
                startAngleRadians,
                targetAngle,
                arc.SweepAngleRadians >= 0);
        }

        return IsValidArcGeometry(radius, sweepAngleRadians);
    }

    private CadTransientStyle CreateGripPreviewStyle()
    {
        return CadTransientStyle.Construction with
        {
            StrokeColor = UserSettings.Interaction.GripPreviewStrokeColor,
            StrokeWidth = UserSettings.Interaction.GripPreviewStrokeWidth,
            LinePattern = CadTransientLinePattern.Dash,
            FillColor = UserSettings.Interaction.GripPreviewFillColor
        };
    }

    private IEnumerable<EntityId> ResolveGripDragEntityIds(GripDragState drag)
    {
        if (drag.Handle.Type != CadHandleType.Center)
            return [drag.Handle.EntityId];

        var selectedEntityIds = CadEditor.Selection.EntityIds;
        if (!selectedEntityIds.Contains(drag.Handle.EntityId))
            return [drag.Handle.EntityId];

        var movableSelectedEntityIds = selectedEntityIds
            .Where(IsMovableByCenterGrip)
            .Distinct()
            .ToArray();

        return movableSelectedEntityIds.Length > 0
            ? movableSelectedEntityIds
            : [drag.Handle.EntityId];
    }

    private bool IsMovableByCenterGrip(EntityId entityId)
    {
        return CadEditor.Document.TryGetEntity(entityId, out var entity) &&
               entity is not null &&
               !entity.IsErased &&
               !entity.IsLocked &&
               CadHandleSceneBuilder.SupportsCenterGrip(entity);
    }

    private static bool IsLineStartGrip(CadLine line, CadPointD gripPosition)
    {
        return line.Start.DistanceSquaredTo(gripPosition) <= line.End.DistanceSquaredTo(gripPosition);
    }

    private static bool IsArcStartGrip(CadArc arc, CadPointD gripPosition)
    {
        return arc.StartPoint.DistanceSquaredTo(gripPosition) <= arc.EndPoint.DistanceSquaredTo(gripPosition);
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
                ? CreateSelectionWindowStyle()
                : CreateSelectionCrossingStyle()));
    }

    private CadHandleSceneBuildOptions CreateHandleSceneBuildOptions()
    {
        var interaction = UserSettings.Interaction;
        return CadHandleSceneBuildOptions.Default with
        {
            SelectionOutlineStyle = CadHandleStyle.SelectionOutline with
            {
                StrokeColor = interaction.SelectedEntityStrokeColor,
                StrokeWidth = interaction.SelectedEntityStrokeWidth
            },
            GripStyle = CadHandleStyle.Grip with
            {
                StrokeColor = interaction.GripStrokeColor,
                FillColor = interaction.GripFillColor,
                Size = interaction.GripSize,
                StrokeWidth = interaction.GripStrokeWidth
            }
        };
    }

    private CadTransientStyle CreateSelectionWindowStyle()
    {
        var interaction = UserSettings.Interaction;
        return CadTransientStyle.SelectionWindow with
        {
            StrokeColor = interaction.SelectionWindowStrokeColor,
            FillColor = interaction.SelectionWindowFillColor,
            StrokeWidth = interaction.SelectionWindowStrokeWidth
        };
    }

    private CadTransientStyle CreateSelectionCrossingStyle()
    {
        var interaction = UserSettings.Interaction;
        return CadTransientStyle.SelectionCrossing with
        {
            StrokeColor = interaction.SelectionCrossingStrokeColor,
            FillColor = interaction.SelectionCrossingFillColor,
            StrokeWidth = interaction.SelectionCrossingStrokeWidth
        };
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

            case CadCanvasToolMode.Ellipse when _pendingWorldPoint is { } firstCorner:
                var ellipseBounds = CadRectD.FromLTRB(firstCorner.X, firstCorner.Y, mouseWorld.X, mouseWorld.Y);
                if (TryCreateEllipseGeometry(ellipseBounds, out var ellipseCenter, out var radiusX, out var radiusY))
                    items.Add(new CadTransientEllipse(ellipseCenter, radiusX, radiusY, CadTransientStyle.Construction));
                break;

            case CadCanvasToolMode.Arc when _pendingWorldPoint is { } arcCenter && _pendingArcStartPoint is null:
                var previewRadius = arcCenter.DistanceTo(mouseWorld);
                if (previewRadius > double.Epsilon)
                {
                    items.Add(new CadTransientCircle(arcCenter, previewRadius, CadTransientStyle.Construction));
                    items.Add(new CadTransientLine(arcCenter, mouseWorld, CadTransientStyle.Construction));
                }
                break;

            case CadCanvasToolMode.Arc when _pendingWorldPoint is { } arcCenter && _pendingArcStartPoint is { } arcStart:
                if (TryCreateArcGeometry(
                    arcCenter,
                    arcStart,
                    mouseWorld,
                    out var arcRadius,
                    out var arcStartAngle,
                    out var arcSweepAngle))
                {
                    items.Add(new CadTransientArc(arcCenter, arcRadius, arcStartAngle, arcSweepAngle, CadTransientStyle.Construction));
                    items.Add(new CadTransientLine(arcCenter, arcStart, CadTransientStyle.Construction));
                    items.Add(new CadTransientLine(arcCenter, GetArcPoint(arcCenter, arcRadius, arcStartAngle + arcSweepAngle), CadTransientStyle.Construction));
                }
                break;

            case CadCanvasToolMode.Rectangle when _pendingWorldPoint is { } firstCorner:
                var bounds = CadRectD.FromLTRB(firstCorner.X, firstCorner.Y, mouseWorld.X, mouseWorld.Y);
                if (IsValidRectangleBounds(bounds))
                    items.Add(new CadTransientRectangle(bounds, CadTransientStyle.Construction));
                break;

            case CadCanvasToolMode.Polyline:
                AddPolylineDrawingPreview(items, mouseWorld);
                break;

            case CadCanvasToolMode.Polygon:
                AddPolygonDrawingPreview(items, mouseWorld);
                break;

            case CadCanvasToolMode.Spline:
                AddSplineDrawingPreview(items, mouseWorld);
                break;

            case CadCanvasToolMode.Text:
                var drawingText = ResolveDrawingText();
                var drawingHeight = ResolveTextBoxHeight(drawingText);
                items.Add(new CadTransientText(
                    drawingText,
                    mouseWorld,
                    drawingHeight,
                    CreateTextBounds(drawingText, mouseWorld, drawingHeight),
                    CadTransientStyle.Construction,
                    DrawingTextInverted,
                    ResolveDrawingTextInvertedMarginFactor()));
                break;

            case CadCanvasToolMode.ShapeText:
                var drawingShapeText = ResolveDrawingText();
                items.Add(new CadTransientShapeText(
                    drawingShapeText,
                    mouseWorld,
                    ResolveTextBoxHeight(drawingShapeText),
                    0,
                    CadStrokeFont.DefaultWidthFactor,
                    CadStrokeFont.DefaultCharacterSpacingFactor,
                    CadStrokeFont.DefaultObliqueAngleRadians,
                    CadTransientStyle.Construction,
                    DrawingTextInverted,
                    ResolveDrawingTextInvertedMarginFactor(),
                    ResolveDrawingShapeFontId()));
                break;

            case CadCanvasToolMode.SetOrigin:
                AddOriginPositionPreview(items, mouseWorld);
                break;
        }
    }

    private void AddPolylineVertexOrComplete(CadPointD world)
    {
        if (_pendingPolylinePoints.Count >= 2 && IsPolylineFinishPoint(world))
        {
            CompletePolyline();
            return;
        }

        if (_pendingPolylinePoints.Count == 0 ||
            !_pendingPolylinePoints[^1].NearEquals(world))
        {
            _pendingPolylinePoints.Add(world);
        }
    }

    private void CompletePolyline()
    {
        if (_pendingPolylinePoints.Count < 2)
            return;

        CadEditor.AddPolyline(_pendingPolylinePoints, closed: false);
        _pendingPolylinePoints.Clear();
    }

    private void AddPolylineDrawingPreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        if (_pendingPolylinePoints.Count == 0)
            return;

        var previewPoints = _pendingPolylinePoints
            .Append(mouseWorld)
            .ToArray();

        if (previewPoints.Length >= 2)
            items.Add(new CadTransientPolyline(previewPoints, Closed: false, CadTransientStyle.Construction));
    }

    private bool IsPolylineFinishPoint(CadPointD world)
    {
        return _pendingPolylinePoints.Count >= 2 &&
               _pendingPolylinePoints[^1].DistanceTo(world) <= ResolvePolylineFinishTolerance();
    }

    private double ResolvePolylineFinishTolerance()
    {
        var screenTolerance = 8.0 / Math.Max(CadEditor.Viewport.Zoom, double.Epsilon);
        var grid = CadEditor.Document.ViewSettings.Grid;
        var snapSpacing = Math.Min(grid.GetSnapSpacingX(), grid.GetSnapSpacingY());

        return IsFinitePositive(snapSpacing)
            ? Math.Max(1e-9, Math.Min(screenTolerance, snapSpacing * 0.49))
            : screenTolerance;
    }

    private void AddSplineFitPointOrComplete(CadPointD world)
    {
        if (_pendingSplinePoints.Count >= 2 && IsSplineFinishPoint(world))
        {
            CompleteSpline();
            return;
        }

        if (_pendingSplinePoints.Count == 0 ||
            !_pendingSplinePoints[^1].NearEquals(world))
        {
            _pendingSplinePoints.Add(world);
        }
    }

    private void CompleteSpline()
    {
        if (_pendingSplinePoints.Count < 2)
            return;

        CadEditor.AddSpline(_pendingSplinePoints);
        _pendingSplinePoints.Clear();
    }

    private void AddSplineDrawingPreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        if (_pendingSplinePoints.Count == 0)
            return;

        var previewPoints = _pendingSplinePoints
            .Append(mouseWorld)
            .ToArray();

        if (previewPoints.Length >= 2)
            items.Add(new CadTransientSpline(previewPoints, Closed: false, CadTransientStyle.Construction));
    }

    private bool IsSplineFinishPoint(CadPointD world)
    {
        return _pendingSplinePoints.Count >= 2 &&
               _pendingSplinePoints[^1].DistanceTo(world) <= ResolveSplineFinishTolerance();
    }

    private double ResolveSplineFinishTolerance()
    {
        var screenTolerance = 8.0 / Math.Max(CadEditor.Viewport.Zoom, double.Epsilon);
        var grid = CadEditor.Document.ViewSettings.Grid;
        var snapSpacing = Math.Min(grid.GetSnapSpacingX(), grid.GetSnapSpacingY());

        return IsFinitePositive(snapSpacing)
            ? Math.Max(1e-9, Math.Min(screenTolerance, snapSpacing * 0.49))
            : screenTolerance;
    }

    private void AddPolygonVertexOrComplete(CadPointD world)
    {
        if (_pendingPolygonPoints.Count >= 3 && IsPolygonClosePoint(world))
        {
            CadEditor.AddPolygon(_pendingPolygonPoints);
            _pendingPolygonPoints.Clear();
            return;
        }

        if (_pendingPolygonPoints.Count == 0 ||
            !_pendingPolygonPoints[^1].NearEquals(world))
        {
            _pendingPolygonPoints.Add(world);
        }
    }

    private void AddPolygonDrawingPreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        if (_pendingPolygonPoints.Count == 0)
            return;

        if (_pendingPolygonPoints.Count >= 3 && IsPolygonClosePoint(mouseWorld))
        {
            items.Add(new CadTransientPolyline(
                _pendingPolygonPoints.ToArray(),
                Closed: true,
                CadTransientStyle.Construction));
            return;
        }

        var previewPoints = _pendingPolygonPoints
            .Append(mouseWorld)
            .ToArray();

        if (previewPoints.Length >= 2)
            items.Add(new CadTransientPolyline(previewPoints, Closed: false, CadTransientStyle.Construction));

        if (_pendingPolygonPoints.Count >= 2)
            items.Add(new CadTransientLine(mouseWorld, _pendingPolygonPoints[0], CadTransientStyle.Construction));
    }

    private bool IsPolygonClosePoint(CadPointD world)
    {
        return _pendingPolygonPoints.Count >= 3 &&
               _pendingPolygonPoints[0].DistanceTo(world) <= ResolvePolygonCloseTolerance();
    }

    private double ResolvePolygonCloseTolerance()
    {
        var screenTolerance = 8.0 / Math.Max(CadEditor.Viewport.Zoom, double.Epsilon);
        var grid = CadEditor.Document.ViewSettings.Grid;
        var snapSpacing = Math.Min(grid.GetSnapSpacingX(), grid.GetSnapSpacingY());

        return IsFinitePositive(snapSpacing)
            ? Math.Max(1e-9, Math.Min(screenTolerance, snapSpacing * 0.49))
            : screenTolerance;
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

    private double ResolveDrawingTextInvertedMarginFactor()
    {
        return DrawingTextInvertedMarginFactor >= 0 &&
               !double.IsNaN(DrawingTextInvertedMarginFactor) &&
               !double.IsInfinity(DrawingTextInvertedMarginFactor)
            ? DrawingTextInvertedMarginFactor
            : CadText.DefaultInvertedMarginFactor;
    }

    private CadShapeFontId ResolveDrawingShapeFontId()
    {
        return DrawingShapeFont switch
        {
            ViewModelCadShapeFont.Simplex => CadShapeFontId.Simplex,
            ViewModelCadShapeFont.MonoLine => CadShapeFontId.MonoLine,
            ViewModelCadShapeFont.BoxFallback => CadShapeFontId.BoxFallback,
            _ => CadShapeFontId.Unicode
        };
    }

    private double ResolveTextBoxHeight(string text)
    {
        var grid = CadEditor.Document.ViewSettings.Grid;
        var spacingY = grid.GetSnapSpacingY();
        return IsFinitePositive(spacingY)
            ? SnapTextHeightUp(text, spacingY, grid.GetSnapSpacingX(), spacingY) * 25
            : Math.Max(8.0 / Math.Max(CadEditor.Viewport.Zoom, double.Epsilon) * 25, 1.0);
    }

    private double SnapTextHeightUp(
        string text,
        double desiredHeight,
        double snapSpacingX,
        double snapSpacingY,
        StyleId? textStyleId = null)
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
            if (IsDimensionAligned(MeasureTextWidth(text, height, textStyleId), snapSpacingX))
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

    private CadRectD CreateTextBounds(
        string text,
        CadPointD position,
        double height,
        StyleId? textStyleId = null)
    {
        return Direct2DImageRenderHost.TryMeasureTextBounds(
            CadEditor.Document,
            text,
            position,
            height,
            textStyleId,
            out var bounds)
            ? bounds
            : CadText.CreateUnmeasuredBounds(position, height);
    }

    private static double GetCachedTextWidthFactor(CadText text)
    {
        return IsFinitePositive(text.Height) && IsFinitePositive(text.LocalBounds.Width)
            ? text.LocalBounds.Width / text.Height
            : 1.0;
    }

    private static double GetCachedShapeTextWidthFactor(CadShapeText text)
    {
        return IsFinitePositive(text.Height) && IsFinitePositive(text.TextBounds.Width)
            ? Math.Max(text.TextBounds.Width / text.Height, 1e-6)
            : Math.Max(text.WidthFactor, 1e-6);
    }

    private static CadRectD CreateShapeTextPreviewBounds(
        string text,
        CadPointD position,
        double height,
        double widthFactor,
        double characterSpacingFactor,
        double obliqueAngleRadians,
        double rotationRadians,
        bool isInverted,
        double invertedMarginFactor,
        CadShapeFontId shapeFontId)
    {
        var bounds = CadShapeFontMetrics.MeasureBounds(
            text,
            position,
            height,
            widthFactor,
            characterSpacingFactor,
            obliqueAngleRadians,
            rotationRadians,
            shapeFontId);

        return isInverted
            ? bounds.Inflate(height * Math.Max(invertedMarginFactor, 0))
            : bounds;
    }

    private double MeasureTextWidth(string text, double height, StyleId? textStyleId = null)
    {
        if (Direct2DImageRenderHost.TryMeasureTextBounds(
            CadEditor.Document,
            text,
            CadPointD.Origin,
            height,
            textStyleId,
            out var bounds))
        {
            return bounds.Width;
        }

        return CadText.CreateUnmeasuredBounds(CadPointD.Origin, height).Width;
    }

    private static bool TryCreateArcGeometry(
        CadPointD center,
        CadPointD start,
        CadPointD end,
        out double radius,
        out double startAngleRadians,
        out double sweepAngleRadians)
    {
        radius = center.DistanceTo(start);
        startAngleRadians = AngleFrom(center, start);
        sweepAngleRadians = ResolveSweepAngle(startAngleRadians, AngleFrom(center, end), counterClockwise: true);
        return center.DistanceTo(end) > double.Epsilon &&
               IsValidArcGeometry(radius, sweepAngleRadians);
    }

    private static bool IsValidArcGeometry(double radius, double sweepAngleRadians)
    {
        return radius > double.Epsilon &&
               Math.Abs(sweepAngleRadians) > 1e-9 &&
               Math.Abs(sweepAngleRadians) <= TwoPi;
    }

    private static bool TryCreateEllipseGeometry(
        CadRectD bounds,
        out CadPointD center,
        out double radiusX,
        out double radiusY)
    {
        center = bounds.Center;
        radiusX = bounds.Width * 0.5;
        radiusY = bounds.Height * 0.5;
        return IsValidEllipseGeometry(radiusX, radiusY);
    }

    private static bool IsValidEllipseGeometry(double radiusX, double radiusY)
    {
        return radiusX > double.Epsilon &&
               radiusY > double.Epsilon &&
               !double.IsNaN(radiusX) &&
               !double.IsNaN(radiusY) &&
               !double.IsInfinity(radiusX) &&
               !double.IsInfinity(radiusY);
    }

    private static double AngleFrom(CadPointD center, CadPointD point)
    {
        return Math.Atan2(point.Y - center.Y, point.X - center.X);
    }

    private static double ResolveSweepAngle(double startAngleRadians, double endAngleRadians, bool counterClockwise)
    {
        return counterClockwise
            ? NormalizePositive(endAngleRadians - startAngleRadians)
            : -NormalizePositive(startAngleRadians - endAngleRadians);
    }

    private static double NormalizePositive(double angleRadians)
    {
        var result = angleRadians % TwoPi;
        return result < 0 ? result + TwoPi : result;
    }

    private static CadPointD GetArcPoint(CadPointD center, double radius, double angleRadians)
    {
        return new CadPointD(
            center.X + Math.Cos(angleRadians) * radius,
            center.Y + Math.Sin(angleRadians) * radius);
    }

    private static bool IsValidRectangleBounds(CadRectD bounds)
    {
        return !bounds.IsEmpty &&
               bounds.Width > 0 &&
               bounds.Height > 0 &&
               !double.IsNaN(bounds.Width) &&
               !double.IsNaN(bounds.Height) &&
               !double.IsInfinity(bounds.Width) &&
               !double.IsInfinity(bounds.Height);
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
        if (!_isApplyingTextMeasurementChanges)
            RequestRender(CreateDocumentInvalidation(e));

        if (e.AffectsViewSettings)
            ViewSettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private CadRenderInvalidation CreateDocumentInvalidation(CadDocumentChangeSet changes)
    {
        if (changes.AffectsDocumentStructure || changes.AffectsViewSettings)
            return CadRenderInvalidation.Full;

        var bounds = CadRectD.Empty;
        foreach (var change in changes.EntityChanges)
        {
            if (RequiresFullRender(change))
                return CadRenderInvalidation.Full;

            if (!CadEditor.Document.TryGetEntity(change.EntityId, out var entity) ||
                entity is null ||
                entity.IsErased ||
                !entity.IsVisible)
            {
                return CadRenderInvalidation.Full;
            }

            bounds = bounds.Union(entity.Bounds);
        }

        return bounds.IsEmpty
            ? CadRenderInvalidation.Full
            : CreateWorldBoundsInvalidation(bounds);
    }

    private static bool RequiresFullRender(CadEntityChange change)
    {
        var kind = change.Kind;
        if (kind.HasFlag(CadEntityChangeKind.Deleted) ||
            kind.HasFlag(CadEntityChangeKind.DrawOrder) ||
            kind.HasFlag(CadEntityChangeKind.Layer))
        {
            return true;
        }

        if (kind.HasFlag(CadEntityChangeKind.Geometry) &&
            !kind.HasFlag(CadEntityChangeKind.Created))
        {
            return true;
        }

        return kind.HasFlag(CadEntityChangeKind.Visibility) &&
               !kind.HasFlag(CadEntityChangeKind.Created);
    }

    private static bool CanDuplicate(CadEntity entity)
    {
        return entity is CadLine or CadCircle or CadEllipse or CadArc or CadRectangle or CadPolyline or CadSpline or CadText or CadShapeText;
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
