using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Runtime.CompilerServices;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.Db.Data.Text;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Geometry;
using Direct2dCad.ViewModels.Rendering;
using static Direct2dCad.ViewModels.Geometry.CadDrawingGeometryFactory;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel : ObservableObject, IDisposable
{
    private const int DeferredGripHandleRenderDelayMilliseconds = 120;
    private readonly CadTransientScene _transientScene = new();
    private readonly CadHandleScene _handleScene = new();
    private readonly CadHandleSceneBuilder _handleSceneBuilder = new();
    private readonly CadHandleHitTester _handleHitTester = new();
    private readonly List<CadPointD> _pendingPolylinePoints = [];
    private readonly List<CadPointD> _pendingPolygonPoints = [];
    private readonly List<CadPointD> _pendingSplinePoints = [];
    private readonly List<CadPointD> _pendingEllipsePoints = [];
    private LayerId _drawingLayerId = LayerId.Default;
    private CadPointD? _pendingWorldPoint;
    private CadPointD? _pendingArcStartPoint;
    private CadPointD? _pendingCircleSecondPoint;
    private CadPointD? _currentMousePoint;
    private CadPointD? _lastPanPoint;
    private CadPointD? _selectionDragStart;
    private GripDragState? _activeGripDrag;
    private ClipboardSnapshot? _clipboard;
    private bool _isPastePreviewActive;
    private bool _isRenderAttached;
    private bool _isApplyingTextMeasurementChanges;
    private bool _isInitialViewportViewApplied;
    private bool _disposed;
    private int _deferredGripHandleRenderVersion;
    private double _viewportWidth = 1.0;
    private double _viewportHeight = 1.0;
    private CadRenderInvalidation _lastOverlayInvalidation = CadRenderInvalidation.FromScreenRect(default);
    private CadColor _drawingLineStrokeColor = CadColor.White;
    private double _drawingLineLineWeight = CadLineWeight.Default.Value;
    private int _drawingLineZIndex;
    private bool _drawingLineIsVisible = true;
    private CadColor _drawingPolylineStrokeColor = CadColor.White;
    private double _drawingPolylineLineWeight = CadLineWeight.Default.Value;
    private int _drawingPolylineZIndex;
    private bool _drawingPolylineIsVisible = true;
    private bool _drawingPolylineClosed;
    private StyleId? _drawingPolylineFillStyleId;
    private CadColor _drawingPolygonStrokeColor = CadColor.White;
    private double _drawingPolygonLineWeight = CadLineWeight.Default.Value;
    private int _drawingPolygonZIndex;
    private bool _drawingPolygonIsVisible = true;
    private StyleId? _drawingPolygonFillStyleId;
    private CadColor _drawingSplineStrokeColor = CadColor.White;
    private double _drawingSplineLineWeight = CadLineWeight.Default.Value;
    private int _drawingSplineZIndex;
    private bool _drawingSplineIsVisible = true;
    private bool _drawingSplineClosed;
    private CadColor _drawingCircleStrokeColor = CadColor.White;
    private double _drawingCircleLineWeight = CadLineWeight.Default.Value;
    private int _drawingCircleZIndex;
    private bool _drawingCircleIsVisible = true;
    private StyleId? _drawingCircleFillStyleId;
    private CadColor _drawingEllipseStrokeColor = CadColor.White;
    private double _drawingEllipseLineWeight = CadLineWeight.Default.Value;
    private int _drawingEllipseZIndex;
    private bool _drawingEllipseIsVisible = true;
    private StyleId? _drawingEllipseFillStyleId;
    private CadColor _drawingRectangleStrokeColor = CadColor.White;
    private double _drawingRectangleLineWeight = CadLineWeight.Default.Value;
    private int _drawingRectangleZIndex;
    private bool _drawingRectangleIsVisible = true;
    private StyleId? _drawingRectangleFillStyleId;
    private double _drawingRectangleCornerRadiusX;
    private double _drawingRectangleCornerRadiusY;
    private string _drawingText = "Text";
    private bool _drawingTextInverted;
    private double _drawingTextInvertedMarginFactor = CadText.DefaultInvertedMarginFactor;
    private CadColor _drawingTextStrokeColor = CadColor.White;
    private double _drawingTextLineWeight = CadLineWeight.Default.Value;
    private int _drawingTextZIndex;
    private bool _drawingTextIsVisible = true;
    private StyleId? _drawingTextStyleId;
    private CadColor _drawingArcStrokeColor = CadColor.White;
    private double _drawingArcLineWeight = CadLineWeight.Default.Value;
    private int _drawingArcZIndex;
    private bool _drawingArcIsVisible = true;

    [ObservableProperty]
    public partial CadEditor CadEditor { get; private set; } = new(CadDocument.Create("Untitled"));

    public Direct2DImageRenderHost Direct2DImageRenderHost { get; } = new();

    [ObservableProperty]
    public partial double CurrentPointerWorldX { get; private set; }

    [ObservableProperty]
    public partial double CurrentPointerWorldY { get; private set; }

    [ObservableProperty]
    public partial CadCanvasToolMode CadCanvasToolMode { get; internal set; } = CadCanvasToolMode.Select;

    public LayerId DrawingLayerId
    {
        get => ResolveDrawingLayerId();
        set
        {
            var previousLayerId = ResolveDrawingLayerId();
            var resolvedLayerId = ResolveExistingDrawingLayerId(value);
            if (_drawingLayerId.Equals(resolvedLayerId))
                return;

            var previousLayer = CadEditor.Document.GetLayer(previousLayerId);
            var newLayer = CadEditor.Document.GetLayer(resolvedLayerId);
            _drawingLayerId = resolvedLayerId;
            OnPropertyChanged();
            UpdateDrawingDefaultsForLayerSelection(previousLayer, newLayer);
            RaiseInteractionStateChanged();
            RequestRender();
        }
    }

    public CadColor DrawingLineStrokeColor
    {
        get => _drawingLineStrokeColor;
        set => SetDrawingSetting(ref _drawingLineStrokeColor, value);
    }

    public double DrawingLineLineWeight
    {
        get => _drawingLineLineWeight;
        set => SetDrawingSetting(ref _drawingLineLineWeight, value, IsFinitePositive(value));
    }

    public int DrawingLineZIndex
    {
        get => _drawingLineZIndex;
        set => SetDrawingSetting(ref _drawingLineZIndex, value);
    }

    public bool DrawingLineIsVisible
    {
        get => _drawingLineIsVisible;
        set => SetDrawingSetting(ref _drawingLineIsVisible, value);
    }

    public CadColor DrawingPolylineStrokeColor
    {
        get => _drawingPolylineStrokeColor;
        set => SetDrawingSetting(ref _drawingPolylineStrokeColor, value);
    }

    public double DrawingPolylineLineWeight
    {
        get => _drawingPolylineLineWeight;
        set => SetDrawingSetting(ref _drawingPolylineLineWeight, value, IsFinitePositive(value));
    }

    public int DrawingPolylineZIndex
    {
        get => _drawingPolylineZIndex;
        set => SetDrawingSetting(ref _drawingPolylineZIndex, value);
    }

    public bool DrawingPolylineIsVisible
    {
        get => _drawingPolylineIsVisible;
        set => SetDrawingSetting(ref _drawingPolylineIsVisible, value);
    }

    public bool DrawingPolylineClosed
    {
        get => _drawingPolylineClosed;
        set => SetDrawingSetting(ref _drawingPolylineClosed, value);
    }

    public StyleId? DrawingPolylineFillStyleId
    {
        get => _drawingPolylineFillStyleId;
        set => SetDrawingSetting(ref _drawingPolylineFillStyleId, value);
    }

    public CadColor DrawingPolygonStrokeColor
    {
        get => _drawingPolygonStrokeColor;
        set => SetDrawingSetting(ref _drawingPolygonStrokeColor, value);
    }

    public double DrawingPolygonLineWeight
    {
        get => _drawingPolygonLineWeight;
        set => SetDrawingSetting(ref _drawingPolygonLineWeight, value, IsFinitePositive(value));
    }

    public int DrawingPolygonZIndex
    {
        get => _drawingPolygonZIndex;
        set => SetDrawingSetting(ref _drawingPolygonZIndex, value);
    }

    public bool DrawingPolygonIsVisible
    {
        get => _drawingPolygonIsVisible;
        set => SetDrawingSetting(ref _drawingPolygonIsVisible, value);
    }

    public StyleId? DrawingPolygonFillStyleId
    {
        get => _drawingPolygonFillStyleId;
        set => SetDrawingSetting(ref _drawingPolygonFillStyleId, value);
    }

    public CadColor DrawingSplineStrokeColor
    {
        get => _drawingSplineStrokeColor;
        set => SetDrawingSetting(ref _drawingSplineStrokeColor, value);
    }

    public double DrawingSplineLineWeight
    {
        get => _drawingSplineLineWeight;
        set => SetDrawingSetting(ref _drawingSplineLineWeight, value, IsFinitePositive(value));
    }

    public int DrawingSplineZIndex
    {
        get => _drawingSplineZIndex;
        set => SetDrawingSetting(ref _drawingSplineZIndex, value);
    }

    public bool DrawingSplineIsVisible
    {
        get => _drawingSplineIsVisible;
        set => SetDrawingSetting(ref _drawingSplineIsVisible, value);
    }

    public bool DrawingSplineClosed
    {
        get => _drawingSplineClosed;
        set => SetDrawingSetting(ref _drawingSplineClosed, value);
    }

    public CadColor DrawingCircleStrokeColor
    {
        get => _drawingCircleStrokeColor;
        set => SetDrawingSetting(ref _drawingCircleStrokeColor, value);
    }

    public double DrawingCircleLineWeight
    {
        get => _drawingCircleLineWeight;
        set => SetDrawingSetting(ref _drawingCircleLineWeight, value, IsFinitePositive(value));
    }

    public int DrawingCircleZIndex
    {
        get => _drawingCircleZIndex;
        set => SetDrawingSetting(ref _drawingCircleZIndex, value);
    }

    public bool DrawingCircleIsVisible
    {
        get => _drawingCircleIsVisible;
        set => SetDrawingSetting(ref _drawingCircleIsVisible, value);
    }

    public StyleId? DrawingCircleFillStyleId
    {
        get => _drawingCircleFillStyleId;
        set => SetDrawingSetting(ref _drawingCircleFillStyleId, value);
    }

    public CadColor DrawingEllipseStrokeColor
    {
        get => _drawingEllipseStrokeColor;
        set => SetDrawingSetting(ref _drawingEllipseStrokeColor, value);
    }

    public double DrawingEllipseLineWeight
    {
        get => _drawingEllipseLineWeight;
        set => SetDrawingSetting(ref _drawingEllipseLineWeight, value, IsFinitePositive(value));
    }

    public int DrawingEllipseZIndex
    {
        get => _drawingEllipseZIndex;
        set => SetDrawingSetting(ref _drawingEllipseZIndex, value);
    }

    public bool DrawingEllipseIsVisible
    {
        get => _drawingEllipseIsVisible;
        set => SetDrawingSetting(ref _drawingEllipseIsVisible, value);
    }

    public StyleId? DrawingEllipseFillStyleId
    {
        get => _drawingEllipseFillStyleId;
        set => SetDrawingSetting(ref _drawingEllipseFillStyleId, value);
    }

    public CadColor DrawingRectangleStrokeColor
    {
        get => _drawingRectangleStrokeColor;
        set => SetDrawingSetting(ref _drawingRectangleStrokeColor, value);
    }

    public double DrawingRectangleLineWeight
    {
        get => _drawingRectangleLineWeight;
        set => SetDrawingSetting(ref _drawingRectangleLineWeight, value, IsFinitePositive(value));
    }

    public int DrawingRectangleZIndex
    {
        get => _drawingRectangleZIndex;
        set => SetDrawingSetting(ref _drawingRectangleZIndex, value);
    }

    public bool DrawingRectangleIsVisible
    {
        get => _drawingRectangleIsVisible;
        set => SetDrawingSetting(ref _drawingRectangleIsVisible, value);
    }

    public StyleId? DrawingRectangleFillStyleId
    {
        get => _drawingRectangleFillStyleId;
        set => SetDrawingSetting(ref _drawingRectangleFillStyleId, value);
    }

    public double DrawingRectangleCornerRadiusX
    {
        get => _drawingRectangleCornerRadiusX;
        set => SetDrawingSetting(ref _drawingRectangleCornerRadiusX, value);
    }

    public double DrawingRectangleCornerRadiusY
    {
        get => _drawingRectangleCornerRadiusY;
        set => SetDrawingSetting(ref _drawingRectangleCornerRadiusY, value);
    }

    public string DrawingText
    {
        get => _drawingText;
        set => SetDrawingSetting(ref _drawingText, value);
    }

    public bool DrawingTextInverted
    {
        get => _drawingTextInverted;
        set => SetDrawingSetting(ref _drawingTextInverted, value);
    }

    public double DrawingTextInvertedMarginFactor
    {
        get => _drawingTextInvertedMarginFactor;
        set => SetDrawingSetting(ref _drawingTextInvertedMarginFactor, value);
    }

    public CadColor DrawingTextStrokeColor
    {
        get => _drawingTextStrokeColor;
        set => SetDrawingSetting(ref _drawingTextStrokeColor, value);
    }

    public double DrawingTextLineWeight
    {
        get => _drawingTextLineWeight;
        set => SetDrawingSetting(ref _drawingTextLineWeight, value, IsFinitePositive(value));
    }

    public int DrawingTextZIndex
    {
        get => _drawingTextZIndex;
        set => SetDrawingSetting(ref _drawingTextZIndex, value);
    }

    public bool DrawingTextIsVisible
    {
        get => _drawingTextIsVisible;
        set => SetDrawingSetting(ref _drawingTextIsVisible, value);
    }

    public StyleId? DrawingTextStyleId
    {
        get => _drawingTextStyleId;
        set => SetDrawingSetting(ref _drawingTextStyleId, value);
    }

    public CadColor DrawingArcStrokeColor
    {
        get => _drawingArcStrokeColor;
        set => SetDrawingSetting(ref _drawingArcStrokeColor, value);
    }

    public double DrawingArcLineWeight
    {
        get => _drawingArcLineWeight;
        set => SetDrawingSetting(ref _drawingArcLineWeight, value, IsFinitePositive(value));
    }

    public int DrawingArcZIndex
    {
        get => _drawingArcZIndex;
        set => SetDrawingSetting(ref _drawingArcZIndex, value);
    }

    public bool DrawingArcIsVisible
    {
        get => _drawingArcIsVisible;
        set => SetDrawingSetting(ref _drawingArcIsVisible, value);
    }

    public event EventHandler? ViewSettingsChanged;
    public event EventHandler? InteractionStateChanged;

    public bool IsPanning { get; private set; }
    public CadUserSettings UserSettings { get; private set; } = CadUserSettings.CreateDefault();

    public CadDocumentViewModel()
    {
        CadEditor.EditorStateChanged += OnEditorStateChanged;
    }

    internal void ReplaceEditor(CadEditor editor)
    {
        var wasAttached = _isRenderAttached;
        if (wasAttached)
            DetachRenderResources();

        CadEditor.EditorStateChanged -= OnEditorStateChanged;
        CadEditor = editor ?? throw new ArgumentNullException(nameof(editor));
        CadEditor.EditorStateChanged += OnEditorStateChanged;
        _isInitialViewportViewApplied = false;
        CadEditor.Viewport.SetSize(_viewportWidth, _viewportHeight);
        ApplyInitialViewportViewIfNeeded();
        RefreshPointerWorldStatus();
        ClearInteractionState(clearClipboard: true, render: false);
        _handleScene.Clear();

        if (wasAttached)
            AttachRenderResources();

        RaiseInteractionStateChanged();
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
        ApplyInitialViewportViewIfNeeded();
        RefreshPointerWorldStatus();
    }

    public void SetRenderSize(int width, int height)
    {
        Direct2DImageRenderHost.SetSize(Math.Max(1, width), Math.Max(1, height));
    }

    private void ApplyInitialViewportViewIfNeeded()
    {
        if (_isInitialViewportViewApplied || _viewportWidth <= 1.0 || _viewportHeight <= 1.0)
            return;

        var zoom = CadEditor.Viewport.Zoom;
        var origin = CadEditor.Document.ViewSettings.Origin.Position;
        var offset = new CadPointD(
            _viewportWidth * 0.5 - origin.X * zoom,
            _viewportHeight * 0.5 + origin.Y * zoom);

        CadEditor.Viewport.SetView(zoom, offset);
        _isInitialViewportViewApplied = true;
    }

    public void ApplyUserSettings(CadUserSettings? settings)
    {
        UserSettings = settings ?? CadUserSettings.CreateDefault();
        UserSettings.Normalize();
        RequestRender();
    }

    public void SetBackgroundColor(CadColor color)
    {
        if (CadEditor.Document.ViewSettings.BackgroundColor == color)
            return;

        CadEditor.Document.ViewSettings.BackgroundColor = color;
        ViewSettingsChanged?.Invoke(this, EventArgs.Empty);
        RequestRender();
    }

    public void UpdateDrawingDefaultsForLayerAppearance(
        LayerId layerId,
        CadColor previousColor,
        CadLineWeight previousLineWeight,
        CadColor newColor,
        CadLineWeight newLineWeight)
    {
        if (!layerId.Equals(ResolveDrawingLayerId()))
            return;

        UpdateDrawingStrokeColors(previousColor, newColor);
        UpdateDrawingLineWeights(
            ResolveDrawingLineWeightDisplayValue(previousLineWeight),
            ResolveDrawingLineWeightDisplayValue(newLineWeight));
    }

    private void UpdateDrawingDefaultsForLayerSelection(CadLayer previousLayer, CadLayer newLayer)
    {
        UpdateDrawingStrokeColors(ResolveLayerStrokeColor(previousLayer), ResolveLayerStrokeColor(newLayer));
        UpdateDrawingLineWeights(
            ResolveDrawingLineWeightDisplayValue(previousLayer.LineWeight),
            ResolveDrawingLineWeightDisplayValue(newLayer.LineWeight));
    }

    private LayerId ResolveDrawingLayerId()
    {
        if (CadEditor.Document.TryGetLayer(_drawingLayerId, out var layer) && layer is not null)
            return _drawingLayerId;

        _drawingLayerId = LayerId.Default;
        return LayerId.Default;
    }

    private LayerId ResolveExistingDrawingLayerId(LayerId layerId)
    {
        return CadEditor.Document.TryGetLayer(layerId, out var layer) && layer is not null
            ? layerId
            : LayerId.Default;
    }

    private CadLayer ResolveDrawingLayer()
    {
        return CadEditor.Document.GetLayer(ResolveDrawingLayerId());
    }

    private bool SetDrawingSetting<T>(
        ref T field,
        T value,
        bool requestRender = true,
        [CallerMemberName] string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
            return false;

        RaiseInteractionStateChanged();

        if (requestRender)
            RequestRender();

        return true;
    }

    private void UpdateDrawingStrokeColors(CadColor previousColor, CadColor newColor)
    {
        if (DrawingLineStrokeColor == previousColor) DrawingLineStrokeColor = newColor;
        if (DrawingPolylineStrokeColor == previousColor) DrawingPolylineStrokeColor = newColor;
        if (DrawingPolygonStrokeColor == previousColor) DrawingPolygonStrokeColor = newColor;
        if (DrawingSplineStrokeColor == previousColor) DrawingSplineStrokeColor = newColor;
        if (DrawingCircleStrokeColor == previousColor) DrawingCircleStrokeColor = newColor;
        if (DrawingEllipseStrokeColor == previousColor) DrawingEllipseStrokeColor = newColor;
        if (DrawingRectangleStrokeColor == previousColor) DrawingRectangleStrokeColor = newColor;
        if (DrawingTextStrokeColor == previousColor) DrawingTextStrokeColor = newColor;
        if (DrawingArcStrokeColor == previousColor) DrawingArcStrokeColor = newColor;
    }

    private void UpdateDrawingLineWeights(double previousLineWeight, double newLineWeight)
    {
        if (AreClose(DrawingLineLineWeight, previousLineWeight)) DrawingLineLineWeight = newLineWeight;
        if (AreClose(DrawingPolylineLineWeight, previousLineWeight)) DrawingPolylineLineWeight = newLineWeight;
        if (AreClose(DrawingPolygonLineWeight, previousLineWeight)) DrawingPolygonLineWeight = newLineWeight;
        if (AreClose(DrawingSplineLineWeight, previousLineWeight)) DrawingSplineLineWeight = newLineWeight;
        if (AreClose(DrawingCircleLineWeight, previousLineWeight)) DrawingCircleLineWeight = newLineWeight;
        if (AreClose(DrawingEllipseLineWeight, previousLineWeight)) DrawingEllipseLineWeight = newLineWeight;
        if (AreClose(DrawingRectangleLineWeight, previousLineWeight)) DrawingRectangleLineWeight = newLineWeight;
        if (AreClose(DrawingTextLineWeight, previousLineWeight)) DrawingTextLineWeight = newLineWeight;
        if (AreClose(DrawingArcLineWeight, previousLineWeight)) DrawingArcLineWeight = newLineWeight;
    }

    public CadCanvasInteractionResult SetToolMode(CadCanvasToolMode toolMode)
    {
        CadCanvasToolMode = toolMode;
        ClearInteractionState(clearClipboard: false);
        RaiseInteractionStateChanged();
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
        UpdatePointerWorldStatus(screen);

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

        UpdatePointerWorldStatus(screen);

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
        UpdatePointerWorldStatus(screen);

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
        UpdatePointerWorldStatus(screen);
        RequestRender(
            CadRenderInvalidation.Full,
            drawGripHandles: false,
            updateHandleScene: false);
        ScheduleDeferredGripHandleRender();
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
        _pendingCircleSecondPoint = null;
        _pendingPolylinePoints.Clear();
        _pendingPolygonPoints.Clear();
        _pendingSplinePoints.Clear();
        _pendingEllipsePoints.Clear();
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
                CompletePolygon();
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

    private void RequestRender(
        CadRenderInvalidation? invalidation,
        bool drawGripHandles = true,
        bool updateHandleScene = true)
    {
        UpdateTextMeasurements();
        var requestedInvalidation = invalidation ?? CadRenderInvalidation.Full;
        CadRenderInvalidation effectiveInvalidation;

        if (requestedInvalidation.IsFull)
        {
            UpdateOverlayScenes(updateHandleScene);
            _lastOverlayInvalidation = CreateOverlayInvalidation(drawGripHandles);
            effectiveInvalidation = CadRenderInvalidation.Full;
        }
        else
        {
            var overlayInvalidation = UpdateOverlayScenesAndCreateInvalidation(
                drawGripHandles,
                updateHandleScene);
            effectiveInvalidation = requestedInvalidation.Union(overlayInvalidation);
        }

        Direct2DImageRenderHost.SetRenderOptions(CreateRenderOptions(drawGripHandles));
        Direct2DImageRenderHost.Render(effectiveInvalidation);
    }

    private void ScheduleDeferredGripHandleRender()
    {
        var context = SynchronizationContext.Current;
        if (context is null)
            return;

        var version = Interlocked.Increment(ref _deferredGripHandleRenderVersion);
        _ = Task.Delay(DeferredGripHandleRenderDelayMilliseconds).ContinueWith(
            task =>
            {
                if (!task.IsCompletedSuccessfully)
                    return;

                context.Post(
                    _ =>
                    {
                        if (_disposed ||
                            version != Volatile.Read(ref _deferredGripHandleRenderVersion))
                        {
                            return;
                        }

                        RequestOverlayRender();
                    },
                    null);
            },
            TaskScheduler.Default);
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
                    CadEditor.AddLine(
                        _pendingWorldPoint.Value,
                        world,
                        layerId: ResolveDrawingLayerId(),
                        graphicStyleId: ResolveDrawingLineGraphicStyleId(),
                        lineWeight: ResolveDrawingLineLineWeight(),
                        zIndex: DrawingLineZIndex,
                        isVisible: DrawingLineIsVisible);
                    _pendingWorldPoint = null;
                }
                RequestRender();
                break;

            case CadCanvasToolMode.CircleCenterRadius:
                HandleCircleCenterRadiusClick(world);
                RequestRender();
                break;

            case CadCanvasToolMode.CircleCenterDiameter:
                HandleCircleCenterDiameterClick(world);
                RequestRender();
                break;

            case CadCanvasToolMode.CircleTwoPoint:
                HandleCircleTwoPointClick(world);
                RequestRender();
                break;

            case CadCanvasToolMode.CircleThreePoint:
                HandleCircleThreePointClick(world);
                RequestRender();
                break;

            case CadCanvasToolMode.EllipseCenter:
            case CadCanvasToolMode.EllipseAxisEnd:
            case CadCanvasToolMode.EllipseArc:
                HandleEllipseDrawingClick(world);
                RequestRender();
                break;

            case CadCanvasToolMode.ArcThreePoint:
            case CadCanvasToolMode.ArcStartCenterEnd:
            case CadCanvasToolMode.ArcStartCenterAngle:
            case CadCanvasToolMode.ArcStartCenterLength:
            case CadCanvasToolMode.ArcStartEndAngle:
            case CadCanvasToolMode.ArcStartEndDirection:
            case CadCanvasToolMode.ArcStartEndRadius:
            case CadCanvasToolMode.ArcCenterStartEnd:
            case CadCanvasToolMode.ArcCenterStartAngle:
            case CadCanvasToolMode.ArcCenterStartLength:
            case CadCanvasToolMode.ArcContinue:
                HandleArcDrawingClick(world);
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
                    {
                        CadEditor.AddRectangle(
                            bounds,
                            layerId: ResolveDrawingLayerId(),
                            cornerRadiusX: ResolveDrawingRectangleCornerRadiusX(bounds),
                            cornerRadiusY: ResolveDrawingRectangleCornerRadiusY(bounds),
                            graphicStyleId: ResolveDrawingRectangleGraphicStyleId(),
                            fillStyleId: ResolveDrawingRectangleFillStyleId(),
                            lineWeight: ResolveDrawingRectangleLineWeight(),
                            zIndex: DrawingRectangleZIndex,
                            isVisible: DrawingRectangleIsVisible);
                    }

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
                var drawingTextStyleId = ResolveDrawingTextStyleId();
                CadEditor.AddText(
                    drawingText,
                    world,
                    ResolveTextBoxHeight(drawingText, drawingTextStyleId),
                    layerId: ResolveDrawingLayerId(),
                    graphicStyleId: ResolveDrawingTextGraphicStyleId(),
                    textStyleId: drawingTextStyleId,
                    isInverted: DrawingTextInverted,
                    invertedMarginFactor: ResolveDrawingTextInvertedMarginFactor(),
                    lineWeight: ResolveDrawingTextLineWeight(),
                    zIndex: DrawingTextZIndex,
                    isVisible: DrawingTextIsVisible);
                RequestRender();
                break;

            case CadCanvasToolMode.SetOrigin:
                CadEditor.SetOriginPosition(world);
                RequestRender();
                break;
        }
    }

    private void HandleCircleCenterRadiusClick(CadPointD world)
    {
        if (_pendingWorldPoint is null)
        {
            _pendingWorldPoint = world;
            return;
        }

        var center = _pendingWorldPoint.Value;
        var radius = center.DistanceTo(world);
        AddCircleIfValid(center, radius);
        _pendingWorldPoint = null;
    }

    private void HandleCircleCenterDiameterClick(CadPointD world)
    {
        if (_pendingWorldPoint is null)
        {
            _pendingWorldPoint = world;
            return;
        }

        var center = _pendingWorldPoint.Value;
        var radius = center.DistanceTo(world) * 0.5;
        AddCircleIfValid(center, radius);
        _pendingWorldPoint = null;
    }

    private void HandleCircleTwoPointClick(CadPointD world)
    {
        if (_pendingWorldPoint is null)
        {
            _pendingWorldPoint = world;
            return;
        }

        if (TryCreateCircleFromDiameterPoints(
            _pendingWorldPoint.Value,
            world,
            out var center,
            out var radius))
        {
            AddCircleIfValid(center, radius);
        }

        _pendingWorldPoint = null;
    }

    private void HandleCircleThreePointClick(CadPointD world)
    {
        if (_pendingWorldPoint is null)
        {
            _pendingWorldPoint = world;
            return;
        }

        if (_pendingCircleSecondPoint is null)
        {
            if (_pendingWorldPoint.Value.DistanceTo(world) > double.Epsilon)
                _pendingCircleSecondPoint = world;
            return;
        }

        if (TryCreateCircleFromThreePoints(
            _pendingWorldPoint.Value,
            _pendingCircleSecondPoint.Value,
            world,
            out var center,
            out var radius))
        {
            AddCircleIfValid(center, radius);
        }

        _pendingWorldPoint = null;
        _pendingCircleSecondPoint = null;
    }

    private void AddCircleIfValid(CadPointD center, double radius)
    {
        if (radius <= double.Epsilon ||
            double.IsNaN(radius) ||
            double.IsInfinity(radius))
        {
            return;
        }

        CadEditor.AddCircle(
            center,
            radius,
            layerId: ResolveDrawingLayerId(),
            graphicStyleId: ResolveDrawingCircleGraphicStyleId(),
            fillStyleId: ResolveDrawingCircleFillStyleId(),
            lineWeight: ResolveDrawingCircleLineWeight(),
            zIndex: DrawingCircleZIndex,
            isVisible: DrawingCircleIsVisible);
    }

    private void HandleArcDrawingClick(CadPointD world)
    {
        if (CadCanvasToolMode == CadCanvasToolMode.ArcContinue)
        {
            if (TryGetContinueArcBase(out var start, out var tangent) &&
                TryCreateArcFromStartEndTangent(start, world, tangent, out var continueGeometry))
            {
                AddArcIfValid(continueGeometry);
            }

            return;
        }

        if (_pendingWorldPoint is null)
        {
            _pendingWorldPoint = world;
            return;
        }

        if (_pendingArcStartPoint is null)
        {
            if (_pendingWorldPoint.Value.DistanceTo(world) > double.Epsilon)
                _pendingArcStartPoint = world;
            return;
        }

        if (TryCreateArcFromMode(
            CadCanvasToolMode,
            _pendingWorldPoint.Value,
            _pendingArcStartPoint.Value,
            world,
            out var geometry))
        {
            AddArcIfValid(geometry);
        }

        _pendingWorldPoint = null;
        _pendingArcStartPoint = null;
    }

    private void AddArcIfValid(ArcDrawingGeometry geometry)
    {
        if (!IsValidArcGeometry(geometry.Radius, geometry.SweepAngleRadians))
            return;

        CadEditor.AddArc(
            geometry.Center,
            geometry.Radius,
            geometry.StartAngleRadians,
            geometry.SweepAngleRadians,
            layerId: ResolveDrawingLayerId(),
            graphicStyleId: ResolveDrawingArcGraphicStyleId(),
            lineWeight: ResolveDrawingArcLineWeight(),
            zIndex: DrawingArcZIndex,
            isVisible: DrawingArcIsVisible);
    }

    private void HandleEllipseDrawingClick(CadPointD world)
    {
        _pendingEllipsePoints.Add(world);

        switch (CadCanvasToolMode)
        {
            case CadCanvasToolMode.EllipseCenter when _pendingEllipsePoints.Count == 3:
                if (TryCreateEllipseFromCenter(
                    _pendingEllipsePoints[0],
                    _pendingEllipsePoints[1],
                    _pendingEllipsePoints[2],
                    out var centerGeometry))
                {
                    AddEllipseIfValid(centerGeometry.Center, centerGeometry.RadiusX, centerGeometry.RadiusY);
                }

                _pendingEllipsePoints.Clear();
                break;

            case CadCanvasToolMode.EllipseAxisEnd when _pendingEllipsePoints.Count == 3:
                if (TryCreateEllipseFromAxisEnd(
                    _pendingEllipsePoints[0],
                    _pendingEllipsePoints[1],
                    _pendingEllipsePoints[2],
                    out var axisGeometry))
                {
                    AddEllipseIfValid(axisGeometry.Center, axisGeometry.RadiusX, axisGeometry.RadiusY);
                }

                _pendingEllipsePoints.Clear();
                break;

            case CadCanvasToolMode.EllipseArc when _pendingEllipsePoints.Count == 5:
                if (TryCreateEllipseArcFromPoints(
                    _pendingEllipsePoints[0],
                    _pendingEllipsePoints[1],
                    _pendingEllipsePoints[2],
                    _pendingEllipsePoints[3],
                    _pendingEllipsePoints[4],
                    out var arcGeometry))
                {
                    AddEllipseArcIfValid(arcGeometry);
                }

                _pendingEllipsePoints.Clear();
                break;
        }
    }

    private void AddEllipseIfValid(CadPointD center, double radiusX, double radiusY)
    {
        if (!IsValidEllipseGeometry(radiusX, radiusY))
            return;

        CadEditor.AddEllipse(
            center,
            radiusX,
            radiusY,
            layerId: ResolveDrawingLayerId(),
            graphicStyleId: ResolveDrawingEllipseGraphicStyleId(),
            fillStyleId: ResolveDrawingEllipseFillStyleId(),
            lineWeight: ResolveDrawingEllipseLineWeight(),
            zIndex: DrawingEllipseZIndex,
            isVisible: DrawingEllipseIsVisible);
    }

    private void AddEllipseArcIfValid(EllipseArcDrawingGeometry geometry)
    {
        if (!IsValidEllipseGeometry(geometry.RadiusX, geometry.RadiusY) ||
            !IsValidArcGeometry(1.0, geometry.SweepAngleRadians))
        {
            return;
        }

        CadEditor.AddEllipseArc(
            geometry.Center,
            geometry.RadiusX,
            geometry.RadiusY,
            geometry.StartAngleRadians,
            geometry.SweepAngleRadians,
            layerId: ResolveDrawingLayerId(),
            graphicStyleId: ResolveDrawingEllipseGraphicStyleId(),
            lineWeight: ResolveDrawingEllipseLineWeight(),
            zIndex: DrawingEllipseZIndex,
            isVisible: DrawingEllipseIsVisible);
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
        {
            CadEditor.Selection.Replace(createdIds);
            RaiseInteractionStateChanged();
        }

        _isPastePreviewActive = false;
        RequestRender();
    }

    private void ClearInteractionState(bool clearClipboard, bool render = true)
    {
        _pendingWorldPoint = null;
        _pendingArcStartPoint = null;
        _pendingCircleSecondPoint = null;
        _pendingPolylinePoints.Clear();
        _pendingPolygonPoints.Clear();
        _pendingSplinePoints.Clear();
        _pendingEllipsePoints.Clear();
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

    private void UpdateOverlayScenes(bool updateHandleScene = true)
    {
        UpdateTransientScene();
        if (updateHandleScene)
            UpdateHandleScene();
    }

    private CadRenderInvalidation UpdateOverlayScenesAndCreateInvalidation(
        bool includeGripHandles = true,
        bool updateHandleScene = true)
    {
        var previousOverlay = _lastOverlayInvalidation;
        UpdateOverlayScenes(updateHandleScene);
        var currentOverlay = CreateOverlayInvalidation(includeGripHandles);
        _lastOverlayInvalidation = currentOverlay;
        return previousOverlay.Union(currentOverlay);
    }

    private CadRenderOptions CreateRenderOptions(bool drawGripHandles = true)
    {
        return new CadRenderOptions
        {
            DrawGripHandles = drawGripHandles,
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

    private CadRenderInvalidation CreateOverlayInvalidation(bool includeGripHandles = true)
    {
        return CreateRenderInvalidationCalculator().CreateOverlayInvalidation(
            _transientScene,
            _handleScene,
            includeGripHandles);
    }

    private CadRenderInvalidationCalculator CreateRenderInvalidationCalculator()
    {
        return new CadRenderInvalidationCalculator(
            CadEditor.Document,
            CadEditor.Viewport,
            Direct2DImageRenderHost.TargetWidth,
            Direct2DImageRenderHost.TargetHeight,
            CreateEntityPreviewStyle);
    }

    private bool TryBeginGripDrag(CadPointD screen)
    {
        UpdateHandleScene();

        if (!_handleHitTester.TryHitGrip(_handleScene, CadEditor.Viewport.WorldToScreen, screen, out var grip))
            return false;

        _activeGripDrag = new GripDragState(
            grip,
            ScreenToWorld(screen, snapToGrid: true),
            ResolveGripPointIndex(grip));
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

        if (drag.Handle.Type == CadHandleType.Center)
        {
            AddMoveGripPreview(items, drag);
            return;
        }

        var style = CreateEntityPreviewStyle(entity);
        var auxiliaryStyle = CreateGripAuxiliaryStyle();
        switch (entity)
        {
            case CadLine line:
                AddLineGripPreview(items, line, drag, style);
                break;

            case CadCircle circle:
                AddCircleGripPreview(items, circle, drag, style, auxiliaryStyle);
                break;

            case CadEllipse ellipse:
                AddEllipseGripPreview(items, ellipse, drag, style, auxiliaryStyle);
                break;

            case CadArc arc:
                AddArcGripPreview(items, arc, drag, style, auxiliaryStyle);
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
                AddTextGripPreview(items, text, drag, style, auxiliaryStyle);
                break;

            case CadShapeText shapeText:
                AddShapeTextGripPreview(items, shapeText, drag, style, auxiliaryStyle);
                break;
        }
    }

    private void AddMoveGripPreview(
        List<CadTransientItem> items,
        GripDragState drag)
    {
        foreach (var entityId in ResolveGripDragEntityIds(drag))
        {
            if (CadEditor.Document.TryGetEntity(entityId, out var entity) &&
                entity is not null &&
                !entity.IsErased)
            {
                items.Add(new CadTransientEntityReference(entityId, drag.Delta, CreateEntityPreviewStyle(entity)));
            }
        }
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
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        var radius = circle.Center.DistanceTo(drag.DraggedGripPosition);
        if (radius <= double.Epsilon)
            return;

        items.Add(new CadTransientCircle(circle.Center, radius, style));
        items.Add(new CadTransientLine(circle.Center, drag.DraggedGripPosition, auxiliaryStyle));
    }

    private static void AddEllipseGripPreview(
        List<CadTransientItem> items,
        CadEllipse ellipse,
        GripDragState drag,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (!TryCreateEllipseGripGeometry(ellipse, drag, out var center, out var radiusX, out var radiusY))
            return;

        items.Add(new CadTransientEllipse(center, radiusX, radiusY, style));
        items.Add(new CadTransientLine(center, drag.DraggedGripPosition, auxiliaryStyle));
    }

    private static void AddArcGripPreview(
        List<CadTransientItem> items,
        CadArc arc,
        GripDragState drag,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (!TryCreateArcGripGeometry(arc, drag, out var center, out var radius, out var startAngle, out var sweepAngle))
            return;

        items.Add(new CadTransientArc(center, radius, startAngle, sweepAngle, style));
        items.Add(new CadTransientLine(center, GetArcPoint(center, radius, startAngle), auxiliaryStyle));
        items.Add(new CadTransientLine(center, GetArcPoint(center, radius, startAngle + sweepAngle), auxiliaryStyle));
    }

    private static void AddRectangleGripPreview(
        List<CadTransientItem> items,
        CadRectangle rectangle,
        GripDragState drag,
        CadTransientStyle style)
    {
        if (TryCreateRectangleGripGeometry(rectangle, drag, out var bounds))
            items.Add(new CadTransientRectangle(
                bounds,
                style,
                rectangle.CornerRadiusX,
                rectangle.CornerRadiusY));
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
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
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
            text.InvertedMarginFactor,
            text.TextStyleId));
        items.Add(new CadTransientRectangle(
            text.IsInverted ? bounds.Inflate(height * text.InvertedMarginFactor) : bounds,
            auxiliaryStyle));
    }

    private void AddShapeTextGripPreview(
        List<CadTransientItem> items,
        CadShapeText text,
        GripDragState drag,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
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
            auxiliaryStyle));
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

        var vertexIndex = drag.PointIndex;
        if (vertexIndex < 0)
            return false;
        if (vertexIndex >= points.Length)
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

        var fitPointIndex = drag.PointIndex;
        if (fitPointIndex < 0)
            return false;
        if (fitPointIndex >= fitPoints.Length)
            return false;

        fitPoints[fitPointIndex] = drag.DraggedGripPosition;
        return !closed || fitPoints.Length >= 3;
    }

    private int ResolveGripPointIndex(CadGripHandle grip)
    {
        if (grip.Type != CadHandleType.Vertex ||
            !CadEditor.Document.TryGetEntity(grip.EntityId, out var entity) ||
            entity is null)
        {
            return -1;
        }

        return entity switch
        {
            CadPolyline polyline => FindNearestPointIndex(polyline.Points, grip.Position),
            CadSpline spline => FindNearestPointIndex(spline.FitPoints, grip.Position),
            _ => -1
        };
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
        {
            if (CadEditor.Document.TryGetEntity(entityId, out var entity) &&
                entity is not null &&
                !entity.IsErased)
            {
                items.Add(new CadTransientEntityReference(entityId, delta, CreateEntityPreviewStyle(entity)));
            }
        }

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

    private CadTransientStyle CreateEntityPreviewStyle(
        CadColor strokeColor,
        CadLineWeight lineWeight,
        StyleId? fillStyleId = null)
    {
        var fill = ResolveTransientFill(fillStyleId);
        return new CadTransientStyle(
            strokeColor,
            ResolvePreviewStrokeWidth(lineWeight, ResolveDefaultLayerLineWeight()),
            CadTransientLinePattern.Solid,
            fill.FillColor,
            HatchFill: fill.HatchFill);
    }

    private CadTransientStyle CreateEntityPreviewStyle(CadEntity entity)
    {
        var layer = CadEditor.Document.TryGetLayer(entity.LayerId, out var resolvedLayer) && resolvedLayer is not null
            ? resolvedLayer
            : CadEditor.Document.GetLayer(LayerId.Default);
        var graphic = ResolveEntityGraphicStyle(entity, layer);
        var strokeColor = entity.UseLayerColor
            ? ResolveLayerStrokeColor(layer)
            : graphic?.StrokeColor ?? ResolveLayerStrokeColor(layer);
        var lineWeight = ResolveEntityLineWeight(entity, graphic, layer);

        var fill = ResolveTransientFill(ResolveEntityFillStyleId(entity));
        return new CadTransientStyle(
            strokeColor,
            ResolvePreviewStrokeWidth(lineWeight, layer.LineWeight),
            CadTransientLinePattern.Solid,
            fill.FillColor,
            HatchFill: fill.HatchFill);
    }

    private CadTransientStyle CreateDrawingAuxiliaryStyle(CadColor strokeColor)
    {
        return CadTransientStyle.Construction with
        {
            StrokeColor = strokeColor,
            StrokeWidth = 1.0,
            LinePattern = CadTransientLinePattern.Dash,
            FillColor = null
        };
    }

    private CadTransientStyle CreateGripAuxiliaryStyle()
    {
        return CadTransientStyle.Construction with
        {
            StrokeColor = UserSettings.Interaction.GripPreviewStrokeColor,
            StrokeWidth = UserSettings.Interaction.GripPreviewStrokeWidth,
            LinePattern = CadTransientLinePattern.Dash,
            FillColor = null
        };
    }

    private (CadColor? FillColor, CadTransientHatchFill? HatchFill) ResolveTransientFill(StyleId? fillStyleId)
    {
        if (fillStyleId is not { } styleId ||
            !CadEditor.Document.TryGetStyle(styleId, out var style))
        {
            return (null, null);
        }

        if (style is CadGradientFillStyle { IsSolid: true } fillStyle)
        {
            var color = fillStyle.Stops[0].Color;
            return (color.IsTransparent ? null : color, null);
        }

        if (style is CadHatchFillStyle hatchStyle &&
            CadEditor.Document.TryGetHatchPattern(hatchStyle.PatternId, out var pattern) &&
            pattern is not null)
        {
            return (null, new CadTransientHatchFill(
                hatchStyle.ForegroundColor,
                hatchStyle.HatchScale,
                hatchStyle.HatchAngle,
                hatchStyle.HatchOrigin,
                pattern.Lines.ToArray()));
        }

        return (null, null);
    }

    private CadGraphicStyle? ResolveEntityGraphicStyle(CadEntity entity, CadLayer layer)
    {
        var styleId = ResolveEntityGraphicStyleId(entity) ?? layer.DefaultGraphicStyleId;
        return styleId is { } graphicStyleId &&
               CadEditor.Document.TryGetStyle(graphicStyleId, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic
            : null;
    }

    private CadColor ResolveLayerStrokeColor(CadLayer layer)
    {
        return layer.DefaultGraphicStyleId is { } styleId &&
               CadEditor.Document.TryGetStyle(styleId, out var style) &&
               style is CadGraphicStyle graphic
            ? graphic.StrokeColor
            : layer.Color;
    }

    private static StyleId? ResolveEntityGraphicStyleId(CadEntity entity)
    {
        return entity switch
        {
            CadLine line => line.GraphicStyleId,
            CadCircle circle => circle.GraphicStyleId,
            CadEllipse ellipse => ellipse.GraphicStyleId,
            CadEllipseArc ellipseArc => ellipseArc.GraphicStyleId,
            CadRectangle rectangle => rectangle.GraphicStyleId,
            CadArc arc => arc.GraphicStyleId,
            CadPolyline polyline => polyline.GraphicStyleId,
            CadSpline spline => spline.GraphicStyleId,
            CadText text => text.GraphicStyleId,
            CadShapeText shapeText => shapeText.GraphicStyleId,
            CadBlockReference blockReference => blockReference.GraphicStyleId,
            _ => null
        };
    }

    private static StyleId? ResolveEntityFillStyleId(CadEntity entity)
    {
        return entity switch
        {
            CadCircle circle => circle.FillStyleId,
            CadEllipse ellipse => ellipse.FillStyleId,
            CadRectangle rectangle => rectangle.FillStyleId,
            CadPolyline { Closed: true } polyline => polyline.FillStyleId,
            _ => null
        };
    }

    private static CadLineWeight ResolveEntityLineWeight(
        CadEntity entity,
        CadGraphicStyle? graphic,
        CadLayer layer)
    {
        if (entity.UseLayerLineWeight)
            return layer.LineWeight;

        return entity.LineWeight switch
        {
            { IsByLayer: false } explicitWeight => explicitWeight,
            { IsByLayer: true } => layer.LineWeight,
            _ => graphic?.LineWeight is { IsByLayer: false } styleWeight
                ? styleWeight
                : layer.LineWeight
        };
    }

    private static double ResolvePreviewStrokeWidth(CadLineWeight lineWeight, CadLineWeight layerLineWeight)
    {
        var resolved = lineWeight.IsByLayer ? layerLineWeight : lineWeight;
        return ResolveDrawingLineWeightDisplayValue(resolved);
    }

    private CadTransientStyle CreateDrawingLineTransientStyle()
    {
        return CreateEntityPreviewStyle(DrawingLineStrokeColor, ResolveDrawingLineLineWeight());
    }

    private StyleId? ResolveDrawingLineGraphicStyleId()
    {
        if (DrawingLineStrokeColor == ResolveDefaultLineStrokeColor())
            return null;

        return CadEditor.Document.CreateGraphicStyle(
            $"Line stroke {DrawingLineStrokeColor.A:X2}{DrawingLineStrokeColor.R:X2}{DrawingLineStrokeColor.G:X2}{DrawingLineStrokeColor.B:X2}",
            DrawingLineStrokeColor,
            CadLineWeight.ByLayer,
            LineTypeId.Continuous);
    }

    private CadLineWeight ResolveDrawingLineLineWeight()
    {
        return ResolveDrawingLineWeight(DrawingLineLineWeight);
    }

    private CadColor ResolveDefaultLineStrokeColor()
    {
        return ResolveDefaultDrawingStrokeColor();
    }

    private CadTransientStyle CreateDrawingPolylineTransientStyle()
    {
        return CreateDrawingPolylineTransientStyle(includeFill: false);
    }

    private CadTransientStyle CreateDrawingPolylineTransientStyle(bool includeFill)
    {
        return CreateEntityPreviewStyle(
            DrawingPolylineStrokeColor,
            ResolveDrawingPolylineLineWeight(),
            includeFill ? ResolveDrawingPolylineFillStyleId() : null);
    }

    private CadTransientStyle CreateDrawingPolygonTransientStyle(bool includeFill = true)
    {
        return CreateEntityPreviewStyle(
            DrawingPolygonStrokeColor,
            ResolveDrawingPolygonLineWeight(),
            includeFill ? ResolveDrawingPolygonFillStyleId() : null);
    }

    private StyleId? ResolveDrawingPolylineGraphicStyleId()
    {
        if (DrawingPolylineStrokeColor == ResolveDefaultPolylineStrokeColor())
            return null;

        return CadEditor.Document.CreateGraphicStyle(
            $"Polyline stroke {DrawingPolylineStrokeColor.A:X2}{DrawingPolylineStrokeColor.R:X2}{DrawingPolylineStrokeColor.G:X2}{DrawingPolylineStrokeColor.B:X2}",
            DrawingPolylineStrokeColor,
            CadLineWeight.ByLayer,
            LineTypeId.Continuous);
    }

    private StyleId? ResolveDrawingPolylineFillStyleId()
    {
        return DrawingPolylineFillStyleId is { } styleId &&
               CadEditor.Document.TryGetStyle(styleId, out var style) &&
               style is CadFillStyle
            ? styleId
            : null;
    }

    private CadLineWeight ResolveDrawingPolylineLineWeight()
    {
        return ResolveDrawingLineWeight(DrawingPolylineLineWeight);
    }

    private bool ResolveDrawingPolylineClosed(int pointCount)
    {
        return DrawingPolylineClosed && pointCount >= 3;
    }

    private CadColor ResolveDefaultPolylineStrokeColor()
    {
        return ResolveDefaultDrawingStrokeColor();
    }

    private StyleId? ResolveDrawingPolygonGraphicStyleId()
    {
        if (DrawingPolygonStrokeColor == ResolveDefaultPolygonStrokeColor())
            return null;

        return CadEditor.Document.CreateGraphicStyle(
            $"Polygon stroke {DrawingPolygonStrokeColor.A:X2}{DrawingPolygonStrokeColor.R:X2}{DrawingPolygonStrokeColor.G:X2}{DrawingPolygonStrokeColor.B:X2}",
            DrawingPolygonStrokeColor,
            CadLineWeight.ByLayer,
            LineTypeId.Continuous);
    }

    private StyleId? ResolveDrawingPolygonFillStyleId()
    {
        return DrawingPolygonFillStyleId is { } styleId &&
               CadEditor.Document.TryGetStyle(styleId, out var style) &&
               style is CadFillStyle
            ? styleId
            : null;
    }

    private CadLineWeight ResolveDrawingPolygonLineWeight()
    {
        return ResolveDrawingLineWeight(DrawingPolygonLineWeight);
    }

    private CadColor ResolveDefaultPolygonStrokeColor()
    {
        return ResolveDefaultDrawingStrokeColor();
    }

    private CadTransientStyle CreateDrawingSplineTransientStyle()
    {
        return CreateEntityPreviewStyle(DrawingSplineStrokeColor, ResolveDrawingSplineLineWeight());
    }

    private StyleId? ResolveDrawingSplineGraphicStyleId()
    {
        if (DrawingSplineStrokeColor == ResolveDefaultSplineStrokeColor())
            return null;

        return CadEditor.Document.CreateGraphicStyle(
            $"Spline stroke {DrawingSplineStrokeColor.A:X2}{DrawingSplineStrokeColor.R:X2}{DrawingSplineStrokeColor.G:X2}{DrawingSplineStrokeColor.B:X2}",
            DrawingSplineStrokeColor,
            CadLineWeight.ByLayer,
            LineTypeId.Continuous);
    }

    private CadLineWeight ResolveDrawingSplineLineWeight()
    {
        return ResolveDrawingLineWeight(DrawingSplineLineWeight);
    }

    private bool ResolveDrawingSplineClosed(int fitPointCount)
    {
        return DrawingSplineClosed && fitPointCount >= 3;
    }

    private CadColor ResolveDefaultSplineStrokeColor()
    {
        return ResolveDefaultDrawingStrokeColor();
    }

    private CadTransientStyle CreateDrawingArcTransientStyle()
    {
        return CreateEntityPreviewStyle(DrawingArcStrokeColor, ResolveDrawingArcLineWeight());
    }

    private CadTransientStyle CreateDrawingCircleTransientStyle()
    {
        return CreateEntityPreviewStyle(
            DrawingCircleStrokeColor,
            ResolveDrawingCircleLineWeight(),
            ResolveDrawingCircleFillStyleId());
    }

    private CadTransientStyle CreateDrawingEllipseTransientStyle()
    {
        return CreateEntityPreviewStyle(
            DrawingEllipseStrokeColor,
            ResolveDrawingEllipseLineWeight(),
            ResolveDrawingEllipseFillStyleId());
    }

    private CadTransientStyle CreateDrawingRectangleTransientStyle()
    {
        return CreateEntityPreviewStyle(
            DrawingRectangleStrokeColor,
            ResolveDrawingRectangleLineWeight(),
            ResolveDrawingRectangleFillStyleId());
    }

    private StyleId? ResolveDrawingCircleGraphicStyleId()
    {
        if (DrawingCircleStrokeColor == ResolveDefaultCircleStrokeColor())
            return null;

        return CadEditor.Document.CreateGraphicStyle(
            $"Circle stroke {DrawingCircleStrokeColor.A:X2}{DrawingCircleStrokeColor.R:X2}{DrawingCircleStrokeColor.G:X2}{DrawingCircleStrokeColor.B:X2}",
            DrawingCircleStrokeColor,
            CadLineWeight.ByLayer,
            LineTypeId.Continuous);
    }

    private StyleId? ResolveDrawingCircleFillStyleId()
    {
        return DrawingCircleFillStyleId is { } styleId &&
               CadEditor.Document.TryGetStyle(styleId, out var style) &&
               style is CadFillStyle
            ? styleId
            : null;
    }

    private CadLineWeight ResolveDrawingCircleLineWeight()
    {
        return ResolveDrawingLineWeight(DrawingCircleLineWeight);
    }

    private CadColor ResolveDefaultCircleStrokeColor()
    {
        return ResolveDefaultDrawingStrokeColor();
    }

    private StyleId? ResolveDrawingEllipseGraphicStyleId()
    {
        if (DrawingEllipseStrokeColor == ResolveDefaultEllipseStrokeColor())
            return null;

        return CadEditor.Document.CreateGraphicStyle(
            $"Ellipse stroke {DrawingEllipseStrokeColor.A:X2}{DrawingEllipseStrokeColor.R:X2}{DrawingEllipseStrokeColor.G:X2}{DrawingEllipseStrokeColor.B:X2}",
            DrawingEllipseStrokeColor,
            CadLineWeight.ByLayer,
            LineTypeId.Continuous);
    }

    private StyleId? ResolveDrawingEllipseFillStyleId()
    {
        return DrawingEllipseFillStyleId is { } styleId &&
               CadEditor.Document.TryGetStyle(styleId, out var style) &&
               style is CadFillStyle
            ? styleId
            : null;
    }

    private CadLineWeight ResolveDrawingEllipseLineWeight()
    {
        return ResolveDrawingLineWeight(DrawingEllipseLineWeight);
    }

    private CadColor ResolveDefaultEllipseStrokeColor()
    {
        return ResolveDefaultDrawingStrokeColor();
    }

    private StyleId? ResolveDrawingRectangleGraphicStyleId()
    {
        if (DrawingRectangleStrokeColor == ResolveDefaultRectangleStrokeColor())
            return null;

        return CadEditor.Document.CreateGraphicStyle(
            $"Rectangle stroke {DrawingRectangleStrokeColor.A:X2}{DrawingRectangleStrokeColor.R:X2}{DrawingRectangleStrokeColor.G:X2}{DrawingRectangleStrokeColor.B:X2}",
            DrawingRectangleStrokeColor,
            CadLineWeight.ByLayer,
            LineTypeId.Continuous);
    }

    private StyleId? ResolveDrawingRectangleFillStyleId()
    {
        return DrawingRectangleFillStyleId is { } styleId &&
               CadEditor.Document.TryGetStyle(styleId, out var style) &&
               style is CadFillStyle
            ? styleId
            : null;
    }

    private CadLineWeight ResolveDrawingRectangleLineWeight()
    {
        return ResolveDrawingLineWeight(DrawingRectangleLineWeight);
    }

    private double ResolveDrawingRectangleCornerRadiusX(CadRectD bounds)
    {
        return ResolveRectangleCornerRadius(DrawingRectangleCornerRadiusX, bounds.Width);
    }

    private double ResolveDrawingRectangleCornerRadiusY(CadRectD bounds)
    {
        return ResolveRectangleCornerRadius(DrawingRectangleCornerRadiusY, bounds.Height);
    }

    private static double ResolveRectangleCornerRadius(double radius, double size)
    {
        return radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius)
            ? 0
            : Math.Min(radius, size * 0.5);
    }

    private CadColor ResolveDefaultRectangleStrokeColor()
    {
        return ResolveDefaultDrawingStrokeColor();
    }

    private CadTransientStyle CreateDrawingTextTransientStyle()
    {
        return CreateEntityPreviewStyle(DrawingTextStrokeColor, ResolveDrawingTextLineWeight());
    }

    private StyleId? ResolveDrawingTextGraphicStyleId()
    {
        if (DrawingTextStrokeColor == ResolveDefaultTextStrokeColor())
            return null;

        return CadEditor.Document.CreateGraphicStyle(
            $"Text stroke {DrawingTextStrokeColor.A:X2}{DrawingTextStrokeColor.R:X2}{DrawingTextStrokeColor.G:X2}{DrawingTextStrokeColor.B:X2}",
            DrawingTextStrokeColor,
            CadLineWeight.ByLayer,
            LineTypeId.Continuous);
    }

    private CadLineWeight ResolveDrawingTextLineWeight()
    {
        return ResolveDrawingLineWeight(DrawingTextLineWeight);
    }

    private CadColor ResolveDefaultTextStrokeColor()
    {
        return ResolveDefaultDrawingStrokeColor();
    }

    private StyleId? ResolveDrawingArcGraphicStyleId()
    {
        if (DrawingArcStrokeColor == ResolveDefaultArcStrokeColor())
            return null;

        return CadEditor.Document.CreateGraphicStyle(
            $"Arc stroke {DrawingArcStrokeColor.A:X2}{DrawingArcStrokeColor.R:X2}{DrawingArcStrokeColor.G:X2}{DrawingArcStrokeColor.B:X2}",
            DrawingArcStrokeColor,
            CadLineWeight.ByLayer,
            LineTypeId.Continuous);
    }

    private CadLineWeight ResolveDrawingArcLineWeight()
    {
        return ResolveDrawingLineWeight(DrawingArcLineWeight);
    }

    private CadLineWeight ResolveDrawingLineWeight(double value)
    {
        if (!IsFinitePositive(value))
            return CadLineWeight.ByLayer;

        var layerWeight = ResolveDefaultLayerLineWeight();
        return AreClose(value, ResolveDrawingLineWeightDisplayValue(layerWeight))
            ? CadLineWeight.ByLayer
            : new CadLineWeight(value);
    }

    private CadLineWeight ResolveDefaultLayerLineWeight()
    {
        return ResolveDrawingLayer().LineWeight;
    }

    private static double ResolveDrawingLineWeightDisplayValue(CadLineWeight lineWeight)
    {
        return lineWeight.IsByLayer || lineWeight.Value <= 0
            ? CadLineWeight.Default.Value
            : lineWeight.Value;
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) <= 1e-9;
    }

    private CadColor ResolveDefaultArcStrokeColor()
    {
        return ResolveDefaultDrawingStrokeColor();
    }

    private CadColor ResolveDefaultDrawingStrokeColor()
    {
        return ResolveLayerStrokeColor(ResolveDrawingLayer());
    }

    private void AddDrawingPreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        switch (CadCanvasToolMode)
        {
            case CadCanvasToolMode.Line when _pendingWorldPoint is { } start:
                items.Add(new CadTransientLine(start, mouseWorld, CreateDrawingLineTransientStyle()));
                break;

            case CadCanvasToolMode.CircleCenterRadius:
            case CadCanvasToolMode.CircleCenterDiameter:
            case CadCanvasToolMode.CircleTwoPoint:
            case CadCanvasToolMode.CircleThreePoint:
                AddCircleDrawingPreview(items, mouseWorld);
                break;

            case CadCanvasToolMode.EllipseCenter:
            case CadCanvasToolMode.EllipseAxisEnd:
            case CadCanvasToolMode.EllipseArc:
                AddEllipseDrawingPreview(items, mouseWorld);
                break;

            case CadCanvasToolMode.ArcThreePoint:
            case CadCanvasToolMode.ArcStartCenterEnd:
            case CadCanvasToolMode.ArcStartCenterAngle:
            case CadCanvasToolMode.ArcStartCenterLength:
            case CadCanvasToolMode.ArcStartEndAngle:
            case CadCanvasToolMode.ArcStartEndDirection:
            case CadCanvasToolMode.ArcStartEndRadius:
            case CadCanvasToolMode.ArcCenterStartEnd:
            case CadCanvasToolMode.ArcCenterStartAngle:
            case CadCanvasToolMode.ArcCenterStartLength:
            case CadCanvasToolMode.ArcContinue:
                AddArcDrawingPreview(items, mouseWorld);
                break;

            case CadCanvasToolMode.Rectangle when _pendingWorldPoint is { } firstCorner:
                var bounds = CadRectD.FromLTRB(firstCorner.X, firstCorner.Y, mouseWorld.X, mouseWorld.Y);
                if (IsValidRectangleBounds(bounds))
                    items.Add(new CadTransientRectangle(
                        bounds,
                        CreateDrawingRectangleTransientStyle(),
                        ResolveDrawingRectangleCornerRadiusX(bounds),
                        ResolveDrawingRectangleCornerRadiusY(bounds)));
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
                var drawingTextStyleId = ResolveDrawingTextStyleId();
                var drawingHeight = ResolveTextBoxHeight(drawingText, drawingTextStyleId);
                items.Add(new CadTransientText(
                    drawingText,
                    mouseWorld,
                    drawingHeight,
                    CreateTextBounds(drawingText, mouseWorld, drawingHeight, drawingTextStyleId),
                    CreateDrawingTextTransientStyle(),
                    DrawingTextInverted,
                    ResolveDrawingTextInvertedMarginFactor(),
                    drawingTextStyleId));
                break;


            case CadCanvasToolMode.SetOrigin:
                AddOriginPositionPreview(items, mouseWorld);
                break;
        }
    }

    private void AddEllipseDrawingPreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        var style = CreateDrawingEllipseTransientStyle();
        var auxiliaryStyle = CreateDrawingAuxiliaryStyle(DrawingEllipseStrokeColor);

        switch (CadCanvasToolMode)
        {
            case CadCanvasToolMode.EllipseCenter:
                AddEllipseCenterPreview(items, mouseWorld, style, auxiliaryStyle);
                break;

            case CadCanvasToolMode.EllipseAxisEnd:
                AddEllipseAxisEndPreview(items, mouseWorld, style, auxiliaryStyle);
                break;

            case CadCanvasToolMode.EllipseArc:
                AddEllipseArcPreview(items, mouseWorld, style, auxiliaryStyle);
                break;
        }
    }

    private void AddEllipseCenterPreview(
        List<CadTransientItem> items,
        CadPointD mouseWorld,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (_pendingEllipsePoints.Count == 0)
            return;

        var center = _pendingEllipsePoints[0];
        items.Add(new CadTransientLine(center, mouseWorld, auxiliaryStyle));

        if (_pendingEllipsePoints.Count < 2)
            return;

        if (!TryCreateEllipseFromCenter(center, _pendingEllipsePoints[1], mouseWorld, out var geometry))
            return;

        items.Add(new CadTransientEllipse(geometry.Center, geometry.RadiusX, geometry.RadiusY, style));
        AddEllipseRadiusMeasurements(items, geometry, auxiliaryStyle);
    }

    private void AddEllipseAxisEndPreview(
        List<CadTransientItem> items,
        CadPointD mouseWorld,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (_pendingEllipsePoints.Count == 0)
            return;

        items.Add(new CadTransientLine(_pendingEllipsePoints[0], mouseWorld, auxiliaryStyle));

        if (_pendingEllipsePoints.Count < 2)
            return;

        if (!TryCreateEllipseFromAxisEnd(_pendingEllipsePoints[0], _pendingEllipsePoints[1], mouseWorld, out var geometry))
            return;

        items.Add(new CadTransientEllipse(geometry.Center, geometry.RadiusX, geometry.RadiusY, style));
        AddEllipseRadiusMeasurements(items, geometry, auxiliaryStyle);
    }

    private void AddEllipseArcPreview(
        List<CadTransientItem> items,
        CadPointD mouseWorld,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (_pendingEllipsePoints.Count == 0)
            return;

        var previewPoints = _pendingEllipsePoints.Concat([mouseWorld]).ToArray();

        if (previewPoints.Length >= 2)
            items.Add(new CadTransientLine(previewPoints[0], previewPoints[1], auxiliaryStyle));

        if (previewPoints.Length < 3 ||
            !TryCreateEllipseFromAxisEnd(previewPoints[0], previewPoints[1], previewPoints[2], out var ellipse))
        {
            return;
        }

        items.Add(new CadTransientEllipse(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, auxiliaryStyle));
        AddEllipseRadiusMeasurements(items, ellipse, auxiliaryStyle);

        if (previewPoints.Length >= 4)
        {
            var startAngle = EllipseAngleFrom(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, previewPoints[3]);
            var startPoint = GetEllipsePoint(ellipse.Center, ellipse.RadiusX, ellipse.RadiusY, startAngle);
            items.Add(new CadTransientLine(ellipse.Center, startPoint, auxiliaryStyle));
        }

        if (previewPoints.Length < 5)
            return;

        if (!TryCreateEllipseArcFromPoints(
            previewPoints[0],
            previewPoints[1],
            previewPoints[2],
            previewPoints[3],
            previewPoints[4],
            out var arc))
        {
            return;
        }

        items.Add(new CadTransientEllipseArc(
            arc.Center,
            arc.RadiusX,
            arc.RadiusY,
            arc.StartAngleRadians,
            arc.SweepAngleRadians,
            style));
        var endPoint = GetEllipsePoint(arc.Center, arc.RadiusX, arc.RadiusY, arc.StartAngleRadians + arc.SweepAngleRadians);
        items.Add(new CadTransientLine(arc.Center, endPoint, auxiliaryStyle));
        AddMeasurementPreview(
            items,
            arc.Center,
            endPoint,
            $"A {FormatAngleDegrees(Math.Abs(arc.SweepAngleRadians))}",
            auxiliaryStyle);
    }

    private void AddEllipseRadiusMeasurements(
        List<CadTransientItem> items,
        EllipseDrawingGeometry geometry,
        CadTransientStyle style)
    {
        AddMeasurementPreview(
            items,
            geometry.Center,
            new CadPointD(geometry.Center.X + geometry.RadiusX, geometry.Center.Y),
            $"X {FormatLength(geometry.RadiusX)}",
            style);
        AddMeasurementPreview(
            items,
            geometry.Center,
            new CadPointD(geometry.Center.X, geometry.Center.Y + geometry.RadiusY),
            $"Y {FormatLength(geometry.RadiusY)}",
            style);
    }

    private void AddArcDrawingPreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        var style = CreateDrawingArcTransientStyle();
        var auxiliaryStyle = CreateDrawingAuxiliaryStyle(DrawingArcStrokeColor);

        if (CadCanvasToolMode == CadCanvasToolMode.ArcContinue)
        {
            if (TryGetContinueArcBase(out var start, out var tangent) &&
                TryCreateArcFromStartEndTangent(start, mouseWorld, tangent, out var geometry))
            {
                AddArcGeometryPreview(items, geometry, style, auxiliaryStyle);
                items.Add(new CadTransientLine(start, start + tangent.Normalize() * geometry.Radius * 0.35, auxiliaryStyle));
                AddArcMeasurementPreview(
                    items,
                    start,
                    mouseWorld,
                    $"R {FormatLength(geometry.Radius)}",
                    auxiliaryStyle);
            }

            return;
        }

        if (_pendingWorldPoint is not { } first)
            return;

        if (_pendingArcStartPoint is not { } second)
        {
            AddArcFirstStagePreview(items, first, mouseWorld, auxiliaryStyle);
            return;
        }

        if (!TryCreateArcFromMode(CadCanvasToolMode, first, second, mouseWorld, out var arcGeometry))
        {
            items.Add(new CadTransientLine(first, second, auxiliaryStyle));
            items.Add(new CadTransientLine(second, mouseWorld, auxiliaryStyle));
            return;
        }

        AddArcGeometryPreview(items, arcGeometry, style, auxiliaryStyle);
        AddArcModeAuxiliaryPreview(items, first, second, mouseWorld, arcGeometry, auxiliaryStyle);
        AddArcModeMeasurementPreview(items, first, second, mouseWorld, arcGeometry, auxiliaryStyle);
    }

    private void AddArcFirstStagePreview(
        List<CadTransientItem> items,
        CadPointD first,
        CadPointD mouseWorld,
        CadTransientStyle auxiliaryStyle)
    {
        items.Add(new CadTransientLine(first, mouseWorld, auxiliaryStyle));

        switch (CadCanvasToolMode)
        {
            case CadCanvasToolMode.ArcStartCenterEnd:
            case CadCanvasToolMode.ArcStartCenterAngle:
            case CadCanvasToolMode.ArcStartCenterLength:
                var startCenterRadius = first.DistanceTo(mouseWorld);
                if (startCenterRadius > double.Epsilon)
                    items.Add(new CadTransientCircle(mouseWorld, startCenterRadius, auxiliaryStyle));
                break;

            case CadCanvasToolMode.ArcCenterStartEnd:
            case CadCanvasToolMode.ArcCenterStartAngle:
            case CadCanvasToolMode.ArcCenterStartLength:
                var centerStartRadius = first.DistanceTo(mouseWorld);
                if (centerStartRadius > double.Epsilon)
                    items.Add(new CadTransientCircle(first, centerStartRadius, auxiliaryStyle));
                break;
        }
    }

    private static void AddArcGeometryPreview(
        List<CadTransientItem> items,
        ArcDrawingGeometry geometry,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        items.Add(new CadTransientArc(
            geometry.Center,
            geometry.Radius,
            geometry.StartAngleRadians,
            geometry.SweepAngleRadians,
            style));

        items.Add(new CadTransientLine(
            geometry.Center,
            GetArcPoint(geometry.Center, geometry.Radius, geometry.StartAngleRadians),
            auxiliaryStyle));
        items.Add(new CadTransientLine(
            geometry.Center,
            GetArcPoint(geometry.Center, geometry.Radius, geometry.StartAngleRadians + geometry.SweepAngleRadians),
            auxiliaryStyle));
    }

    private void AddArcModeAuxiliaryPreview(
        List<CadTransientItem> items,
        CadPointD first,
        CadPointD second,
        CadPointD third,
        ArcDrawingGeometry geometry,
        CadTransientStyle auxiliaryStyle)
    {
        switch (CadCanvasToolMode)
        {
            case CadCanvasToolMode.ArcThreePoint:
                items.Add(new CadTransientLine(first, second, auxiliaryStyle));
                items.Add(new CadTransientLine(second, third, auxiliaryStyle));
                break;

            case CadCanvasToolMode.ArcStartEndDirection:
                items.Add(new CadTransientLine(first, third, auxiliaryStyle));
                break;

            case CadCanvasToolMode.ArcStartEndAngle:
            case CadCanvasToolMode.ArcStartEndRadius:
                items.Add(new CadTransientLine(first, second, auxiliaryStyle));
                items.Add(new CadTransientLine(Midpoint(first, second), third, auxiliaryStyle));
                break;

            case CadCanvasToolMode.ArcStartCenterLength:
            case CadCanvasToolMode.ArcCenterStartLength:
                items.Add(new CadTransientLine(GetArcPoint(geometry.Center, geometry.Radius, geometry.StartAngleRadians), third, auxiliaryStyle));
                break;
        }
    }

    private void AddArcModeMeasurementPreview(
        List<CadTransientItem> items,
        CadPointD first,
        CadPointD second,
        CadPointD third,
        ArcDrawingGeometry geometry,
        CadTransientStyle auxiliaryStyle)
    {
        var startPoint = GetArcPoint(geometry.Center, geometry.Radius, geometry.StartAngleRadians);
        var endPoint = GetArcPoint(geometry.Center, geometry.Radius, geometry.StartAngleRadians + geometry.SweepAngleRadians);

        switch (CadCanvasToolMode)
        {
            case CadCanvasToolMode.ArcThreePoint:
                break;

            case CadCanvasToolMode.ArcStartCenterEnd:
            case CadCanvasToolMode.ArcStartCenterAngle:
            case CadCanvasToolMode.ArcCenterStartEnd:
            case CadCanvasToolMode.ArcCenterStartAngle:
            case CadCanvasToolMode.ArcStartEndAngle:
                AddArcMeasurementPreview(
                    items,
                    geometry.Center,
                    endPoint,
                    $"A {FormatAngleDegrees(Math.Abs(geometry.SweepAngleRadians))}",
                    auxiliaryStyle);
                break;

            case CadCanvasToolMode.ArcStartCenterLength:
            case CadCanvasToolMode.ArcCenterStartLength:
                AddArcMeasurementPreview(
                    items,
                    startPoint,
                    endPoint,
                    $"L {FormatLength(startPoint.DistanceTo(endPoint))}",
                    auxiliaryStyle);
                break;

            case CadCanvasToolMode.ArcStartEndRadius:
                AddArcMeasurementPreview(
                    items,
                    geometry.Center,
                    startPoint,
                    $"R {FormatLength(geometry.Radius)}",
                    auxiliaryStyle);
                break;

            case CadCanvasToolMode.ArcStartEndDirection:
                AddArcMeasurementPreview(
                    items,
                    first,
                    third,
                    $"D {FormatAngleDegrees(NormalizePositive(AngleFrom(first, third)))}",
                    auxiliaryStyle);
                break;
        }
    }

    private void AddArcMeasurementPreview(
        List<CadTransientItem> items,
        CadPointD lineStart,
        CadPointD lineEnd,
        string text,
        CadTransientStyle style)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        AddMeasurementPreview(items, lineStart, lineEnd, text, style);
    }

    private void AddCircleDrawingPreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        var style = CreateDrawingCircleTransientStyle();
        var auxiliaryStyle = CreateDrawingAuxiliaryStyle(DrawingCircleStrokeColor);

        switch (CadCanvasToolMode)
        {
            case CadCanvasToolMode.CircleCenterRadius:
                if (_pendingWorldPoint is { } centerRadiusCenter)
                    AddCircleCenterRadiusPreview(items, centerRadiusCenter, mouseWorld, style, auxiliaryStyle);
                break;

            case CadCanvasToolMode.CircleCenterDiameter:
                if (_pendingWorldPoint is { } centerDiameterCenter)
                    AddCircleCenterDiameterPreview(items, centerDiameterCenter, mouseWorld, style, auxiliaryStyle);
                break;

            case CadCanvasToolMode.CircleTwoPoint:
                if (_pendingWorldPoint is { } firstDiameterPoint)
                    AddCircleTwoPointPreview(items, firstDiameterPoint, mouseWorld, style, auxiliaryStyle);
                break;

            case CadCanvasToolMode.CircleThreePoint:
                if (_pendingWorldPoint is { } firstPoint &&
                    _pendingCircleSecondPoint is { } secondPoint)
                {
                    AddCircleThreePointPreview(items, firstPoint, secondPoint, mouseWorld, style, auxiliaryStyle);
                }
                else if (_pendingWorldPoint is { } firstOnlyPoint)
                {
                    items.Add(new CadTransientLine(firstOnlyPoint, mouseWorld, auxiliaryStyle));
                }
                break;
        }
    }

    private void AddCircleCenterRadiusPreview(
        List<CadTransientItem> items,
        CadPointD center,
        CadPointD edge,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        var radius = center.DistanceTo(edge);
        if (!IsValidCircleGeometry(radius))
            return;

        items.Add(new CadTransientCircle(center, radius, style));
        items.Add(new CadTransientLine(center, edge, auxiliaryStyle));
        AddCircleMeasurementPreview(items, center, edge, radius, auxiliaryStyle);
    }

    private void AddCircleCenterDiameterPreview(
        List<CadTransientItem> items,
        CadPointD center,
        CadPointD diameterPoint,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        var radius = center.DistanceTo(diameterPoint) * 0.5;
        if (!IsValidCircleGeometry(radius))
            return;

        items.Add(new CadTransientCircle(center, radius, style));
        var direction = diameterPoint - center;
        var unit = direction.Normalize();
        if (unit == CadVectorD.Zero)
            return;

        var start = center - unit * radius;
        var end = center + unit * radius;
        items.Add(new CadTransientLine(start, end, auxiliaryStyle));
        AddCircleMeasurementPreview(items, start, end, radius * 2.0, auxiliaryStyle);
    }

    private void AddCircleTwoPointPreview(
        List<CadTransientItem> items,
        CadPointD first,
        CadPointD second,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        if (!TryCreateCircleFromDiameterPoints(first, second, out var center, out var radius))
            return;

        items.Add(new CadTransientCircle(center, radius, style));
        items.Add(new CadTransientLine(first, second, auxiliaryStyle));
        AddCircleMeasurementPreview(items, first, second, radius * 2.0, auxiliaryStyle);
    }

    private void AddCircleThreePointPreview(
        List<CadTransientItem> items,
        CadPointD first,
        CadPointD second,
        CadPointD third,
        CadTransientStyle style,
        CadTransientStyle auxiliaryStyle)
    {
        items.Add(new CadTransientLine(first, second, auxiliaryStyle));
        items.Add(new CadTransientLine(second, third, auxiliaryStyle));

        if (!TryCreateCircleFromThreePoints(first, second, third, out var center, out var radius))
            return;

        items.Add(new CadTransientCircle(center, radius, style));
    }

    private void AddCircleMeasurementPreview(
        List<CadTransientItem> items,
        CadPointD lineStart,
        CadPointD lineEnd,
        double value,
        CadTransientStyle style)
    {
        if (value <= double.Epsilon)
            return;

        AddMeasurementPreview(items, lineStart, lineEnd, FormatLength(value), style);
    }

    private void AddMeasurementPreview(
        List<CadTransientItem> items,
        CadPointD lineStart,
        CadPointD lineEnd,
        string text,
        CadTransientStyle style)
    {
        var zoom = Math.Max(CadEditor.Viewport.Zoom, double.Epsilon);
        var textHeight = 13.0 / zoom;
        var padding = 8.0 / zoom;
        var direction = lineEnd - lineStart;
        var unit = direction.Normalize();
        if (unit == CadVectorD.Zero)
            unit = CadVectorD.UnitX;

        var normal = unit.Perpendicular();
        var midpoint = lineStart + direction * 0.5;
        var position = midpoint + normal * padding + unit * padding;
        var width = EstimateTransientLabelWidth(text, textHeight);
        var boundsHeight = textHeight * 1.35;
        var bounds = CadRectD.FromLTRB(
            position.X,
            position.Y,
            position.X + width,
            position.Y + boundsHeight);

        items.Add(new CadTransientText(text, position, textHeight, bounds, style));
    }

    private string FormatLength(double value)
    {
        var precision = Math.Clamp(CadEditor.Document.DocumentSettings.LengthPrecision, 0, 12);
        return value.ToString($"F{precision}");
    }

    private string FormatAngleDegrees(double radians)
    {
        var precision = Math.Clamp(CadEditor.Document.DocumentSettings.AnglePrecision, 0, 12);
        return CadArc.RadiansToDegrees(radians).ToString($"F{precision}");
    }

    private static double EstimateTransientLabelWidth(string text, double height)
    {
        return Math.Max(height * 2.0, text.Length * height * 0.85);
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

        var closed = ResolveDrawingPolylineClosed(_pendingPolylinePoints.Count);
        CadEditor.AddPolyline(
            _pendingPolylinePoints,
            closed,
            layerId: ResolveDrawingLayerId(),
            graphicStyleId: ResolveDrawingPolylineGraphicStyleId(),
            fillStyleId: closed ? ResolveDrawingPolylineFillStyleId() : null,
            lineWeight: ResolveDrawingPolylineLineWeight(),
            zIndex: DrawingPolylineZIndex,
            isVisible: DrawingPolylineIsVisible);
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
        {
            var closed = ResolveDrawingPolylineClosed(previewPoints.Length);
            items.Add(new CadTransientPolyline(previewPoints, closed, CreateDrawingPolylineTransientStyle(closed)));
        }
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

        CadEditor.AddSpline(
            _pendingSplinePoints,
            ResolveDrawingSplineClosed(_pendingSplinePoints.Count),
            layerId: ResolveDrawingLayerId(),
            graphicStyleId: ResolveDrawingSplineGraphicStyleId(),
            lineWeight: ResolveDrawingSplineLineWeight(),
            zIndex: DrawingSplineZIndex,
            isVisible: DrawingSplineIsVisible);
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
        {
            items.Add(new CadTransientSpline(
                previewPoints,
                ResolveDrawingSplineClosed(previewPoints.Length),
                CreateDrawingSplineTransientStyle()));
        }
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
            CompletePolygon();
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
                CreateDrawingPolygonTransientStyle()));
            return;
        }

        var previewPoints = _pendingPolygonPoints
            .Append(mouseWorld)
            .ToArray();

        if (previewPoints.Length >= 3)
        {
            items.Add(new CadTransientPolyline(
                previewPoints,
                Closed: true,
                CreateDrawingPolygonTransientStyle()));
        }
        else if (previewPoints.Length >= 2)
        {
            items.Add(new CadTransientPolyline(
                previewPoints,
                Closed: false,
                CreateDrawingPolygonTransientStyle(includeFill: false)));
        }
    }

    private void CompletePolygon()
    {
        if (_pendingPolygonPoints.Count < 3)
            return;

        CadEditor.AddPolygon(
            _pendingPolygonPoints,
            layerId: ResolveDrawingLayerId(),
            graphicStyleId: ResolveDrawingPolygonGraphicStyleId(),
            fillStyleId: ResolveDrawingPolygonFillStyleId(),
            lineWeight: ResolveDrawingPolygonLineWeight(),
            zIndex: DrawingPolygonZIndex,
            isVisible: DrawingPolygonIsVisible);
        _pendingPolygonPoints.Clear();
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
            case CadSnapMarkerType.InfiniteCross:
                var visibleBounds = CadEditor.Viewport.VisibleWorldBounds;
                if (visibleBounds.IsEmpty)
                    break;

                items.Add(new CadTransientLine(
                    new CadPointD(visibleBounds.MinX, snappedWorld.Y),
                    new CadPointD(visibleBounds.MaxX, snappedWorld.Y),
                    style));
                items.Add(new CadTransientLine(
                    new CadPointD(snappedWorld.X, visibleBounds.MinY),
                    new CadPointD(snappedWorld.X, visibleBounds.MaxY),
                    style));
                break;

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

    private void RefreshPointerWorldStatus()
    {
        if (_currentMousePoint is { } screen)
            UpdatePointerWorldStatus(screen);
    }

    private void UpdatePointerWorldStatus(CadPointD screen)
    {
        var world = ScreenToWorld(screen, snapToGrid: true);
        CurrentPointerWorldX = world.X;
        CurrentPointerWorldY = world.Y;
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

    private StyleId? ResolveDrawingTextStyleId()
    {
        return DrawingTextStyleId is { } styleId &&
               CadEditor.Document.TryGetStyle(styleId, out var style) &&
               style is CadTextStyle
            ? styleId
            : null;
    }

    private double ResolveDrawingTextInvertedMarginFactor()
    {
        return DrawingTextInvertedMarginFactor >= 0 &&
               !double.IsNaN(DrawingTextInvertedMarginFactor) &&
               !double.IsInfinity(DrawingTextInvertedMarginFactor)
            ? DrawingTextInvertedMarginFactor
            : CadText.DefaultInvertedMarginFactor;
    }

    private double ResolveTextBoxHeight(string text, StyleId? textStyleId = null)
    {
        var grid = CadEditor.Document.ViewSettings.Grid;
        var spacingY = grid.GetSnapSpacingY();
        return IsFinitePositive(spacingY)
            ? SnapTextHeightUp(text, spacingY, grid.GetSnapSpacingX(), spacingY, textStyleId) * 25
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

    private bool TryGetContinueArcBase(out CadPointD start, out CadVectorD tangent)
    {
        foreach (var entity in CadEditor.Document.Entities.Values.Reverse())
        {
            if (entity.IsErased)
                continue;

            switch (entity)
            {
                case CadArc arc:
                    start = arc.EndPoint;
                    var radiusVector = start - arc.Center;
                    tangent = arc.SweepAngleRadians > 0
                        ? radiusVector.Perpendicular().Normalize()
                        : (-radiusVector.Perpendicular()).Normalize();
                    return tangent != CadVectorD.Zero;

                case CadLine line:
                    start = line.End;
                    tangent = (line.End - line.Start).Normalize();
                    return tangent != CadVectorD.Zero;
            }
        }

        start = CadPointD.Origin;
        tangent = CadVectorD.Zero;
        return false;
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

        if (e.DocumentChanged)
            RaiseInteractionStateChanged();
    }

    private void OnEditorStateChanged(object? sender, CadEditorCommandResult e)
    {
        if (e.SelectionChanged || e.ViewChanged)
            RaiseInteractionStateChanged();
    }

    private void RaiseInteractionStateChanged()
    {
        InteractionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private CadRenderInvalidation CreateDocumentInvalidation(CadDocumentChangeSet changes)
    {
        return CreateRenderInvalidationCalculator().CreateDocumentInvalidation(changes);
    }

    private static bool CanDuplicate(CadEntity entity)
    {
        return entity is CadLine or CadCircle or CadEllipse or CadEllipseArc or CadArc or CadRectangle or CadPolyline or CadSpline or CadText or CadShapeText;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DetachRenderResources();
        CadEditor.EditorStateChanged -= OnEditorStateChanged;
        Direct2DImageRenderHost.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CadDocumentViewModel));
    }

}
