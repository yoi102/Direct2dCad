using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Drawing;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Rendering;
using Direct2dCad.ViewModels.Services.Snapping;
using Direct2dCad.ViewModels.Services.Styling;
using Direct2dCad.ViewModels.Services.Text;
using Direct2dCad.ViewModels.Services.ViewServices;
using MessagePipe;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel : ObservableObject, ICadDocumentViewModelMessageSource, IDisposable
{
    private readonly IPublisher<CadDocumentInteractionStateChangedMessage> _interactionStateChangedPublisher;
    private readonly IPublisher<CadDocumentViewSettingsChangedMessage> _viewSettingsChangedPublisher;
    private readonly IDisposable _oleObjectUpdatedSubscription;
    private readonly Guid _oleEditSessionId = Guid.NewGuid();
    private readonly HashSet<EntityId> _openOleEditEntityIds = [];
    private bool _isApplyingOleHostUpdate;
    private readonly CadOverlaySceneCoordinator _overlayScenes = new();
    private readonly CadRenderResourceCoordinator _renderResources = new();
    private readonly CadGripDragController _gripDrag = new(new CadHandleHitTester());
    private readonly CadViewportInitializationState _viewportInitialization = new();
    private readonly CadPanInteractionController _pan = new();
    private readonly CadPasteInteractionController _paste;
    private readonly IImageImportService _imageImportService;
    private readonly IOleImportService _oleImportService;
    private readonly CadSelectionDragController _selectionDrag = new();
    private readonly CadDrawingDefaults _drawingDefaults = new();
    private readonly CadDrawingSessionState _drawingState = new();
    private LayerId _drawingLayerId = LayerId.Default;
    private LayerId _pasteTargetLayerId = LayerId.Default;
    private CadPointD? _currentMousePoint;
    private bool _disposed;

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

    public LayerId PasteTargetLayerId
    {
        get => ResolveExistingPasteTargetLayerId();
        set
        {
            var resolvedLayerId = ResolveExistingDrawingLayerId(value);
            if (_pasteTargetLayerId.Equals(resolvedLayerId))
                return;

            _pasteTargetLayerId = resolvedLayerId;
            OnPropertyChanged();
            RaiseInteractionStateChanged();
            RequestRender();
        }
    }

    public bool IsPastePreviewActive => _paste.IsPreviewActive;

    public CadColor DrawingLineStrokeColor
    {
        get => _drawingDefaults.LineStrokeColor;
        set => _drawingDefaults.LineStrokeColor = value;
    }

    public double DrawingLineLineWeight
    {
        get => _drawingDefaults.LineLineWeight;
        set => _drawingDefaults.LineLineWeight = value;
    }

    public int DrawingLineZIndex
    {
        get => _drawingDefaults.LineZIndex;
        set => _drawingDefaults.LineZIndex = value;
    }

    public bool DrawingLineIsVisible
    {
        get => _drawingDefaults.LineIsVisible;
        set => _drawingDefaults.LineIsVisible = value;
    }

    public CadColor DrawingPolylineStrokeColor
    {
        get => _drawingDefaults.PolylineStrokeColor;
        set => _drawingDefaults.PolylineStrokeColor = value;
    }

    public double DrawingPolylineLineWeight
    {
        get => _drawingDefaults.PolylineLineWeight;
        set => _drawingDefaults.PolylineLineWeight = value;
    }

    public int DrawingPolylineZIndex
    {
        get => _drawingDefaults.PolylineZIndex;
        set => _drawingDefaults.PolylineZIndex = value;
    }

    public bool DrawingPolylineIsVisible
    {
        get => _drawingDefaults.PolylineIsVisible;
        set => _drawingDefaults.PolylineIsVisible = value;
    }

    public bool DrawingPolylineClosed
    {
        get => _drawingDefaults.PolylineClosed;
        set => _drawingDefaults.PolylineClosed = value;
    }

    public StyleId? DrawingPolylineFillStyleId
    {
        get => _drawingDefaults.PolylineFillStyleId;
        set => _drawingDefaults.PolylineFillStyleId = value;
    }

    public CadColor DrawingPolygonStrokeColor
    {
        get => _drawingDefaults.PolygonStrokeColor;
        set => _drawingDefaults.PolygonStrokeColor = value;
    }

    public double DrawingPolygonLineWeight
    {
        get => _drawingDefaults.PolygonLineWeight;
        set => _drawingDefaults.PolygonLineWeight = value;
    }

    public int DrawingPolygonZIndex
    {
        get => _drawingDefaults.PolygonZIndex;
        set => _drawingDefaults.PolygonZIndex = value;
    }

    public bool DrawingPolygonIsVisible
    {
        get => _drawingDefaults.PolygonIsVisible;
        set => _drawingDefaults.PolygonIsVisible = value;
    }

    public StyleId? DrawingPolygonFillStyleId
    {
        get => _drawingDefaults.PolygonFillStyleId;
        set => _drawingDefaults.PolygonFillStyleId = value;
    }

    public CadColor DrawingSplineStrokeColor
    {
        get => _drawingDefaults.SplineStrokeColor;
        set => _drawingDefaults.SplineStrokeColor = value;
    }

    public double DrawingSplineLineWeight
    {
        get => _drawingDefaults.SplineLineWeight;
        set => _drawingDefaults.SplineLineWeight = value;
    }

    public int DrawingSplineZIndex
    {
        get => _drawingDefaults.SplineZIndex;
        set => _drawingDefaults.SplineZIndex = value;
    }

    public bool DrawingSplineIsVisible
    {
        get => _drawingDefaults.SplineIsVisible;
        set => _drawingDefaults.SplineIsVisible = value;
    }

    public bool DrawingSplineClosed
    {
        get => _drawingDefaults.SplineClosed;
        set => _drawingDefaults.SplineClosed = value;
    }

    public StyleId? DrawingSplineFillStyleId
    {
        get => _drawingDefaults.SplineFillStyleId;
        set => _drawingDefaults.SplineFillStyleId = value;
    }

    public CadColor DrawingCircleStrokeColor
    {
        get => _drawingDefaults.CircleStrokeColor;
        set => _drawingDefaults.CircleStrokeColor = value;
    }

    public double DrawingCircleLineWeight
    {
        get => _drawingDefaults.CircleLineWeight;
        set => _drawingDefaults.CircleLineWeight = value;
    }

    public int DrawingCircleZIndex
    {
        get => _drawingDefaults.CircleZIndex;
        set => _drawingDefaults.CircleZIndex = value;
    }

    public bool DrawingCircleIsVisible
    {
        get => _drawingDefaults.CircleIsVisible;
        set => _drawingDefaults.CircleIsVisible = value;
    }

    public StyleId? DrawingCircleFillStyleId
    {
        get => _drawingDefaults.CircleFillStyleId;
        set => _drawingDefaults.CircleFillStyleId = value;
    }

    public CadColor DrawingEllipseStrokeColor
    {
        get => _drawingDefaults.EllipseStrokeColor;
        set => _drawingDefaults.EllipseStrokeColor = value;
    }

    public double DrawingEllipseLineWeight
    {
        get => _drawingDefaults.EllipseLineWeight;
        set => _drawingDefaults.EllipseLineWeight = value;
    }

    public int DrawingEllipseZIndex
    {
        get => _drawingDefaults.EllipseZIndex;
        set => _drawingDefaults.EllipseZIndex = value;
    }

    public bool DrawingEllipseIsVisible
    {
        get => _drawingDefaults.EllipseIsVisible;
        set => _drawingDefaults.EllipseIsVisible = value;
    }

    public StyleId? DrawingEllipseFillStyleId
    {
        get => _drawingDefaults.EllipseFillStyleId;
        set => _drawingDefaults.EllipseFillStyleId = value;
    }

    public CadColor DrawingRectangleStrokeColor
    {
        get => _drawingDefaults.RectangleStrokeColor;
        set => _drawingDefaults.RectangleStrokeColor = value;
    }

    public double DrawingRectangleLineWeight
    {
        get => _drawingDefaults.RectangleLineWeight;
        set => _drawingDefaults.RectangleLineWeight = value;
    }

    public int DrawingRectangleZIndex
    {
        get => _drawingDefaults.RectangleZIndex;
        set => _drawingDefaults.RectangleZIndex = value;
    }

    public bool DrawingRectangleIsVisible
    {
        get => _drawingDefaults.RectangleIsVisible;
        set => _drawingDefaults.RectangleIsVisible = value;
    }

    public StyleId? DrawingRectangleFillStyleId
    {
        get => _drawingDefaults.RectangleFillStyleId;
        set => _drawingDefaults.RectangleFillStyleId = value;
    }

    public double DrawingRectangleCornerRadiusX
    {
        get => _drawingDefaults.RectangleCornerRadiusX;
        set => _drawingDefaults.RectangleCornerRadiusX = value;
    }

    public double DrawingRectangleCornerRadiusY
    {
        get => _drawingDefaults.RectangleCornerRadiusY;
        set => _drawingDefaults.RectangleCornerRadiusY = value;
    }

    public string DrawingText
    {
        get => _drawingDefaults.Text;
        set => _drawingDefaults.Text = value;
    }

    public bool DrawingTextInverted
    {
        get => _drawingDefaults.TextInverted;
        set => _drawingDefaults.TextInverted = value;
    }

    public double DrawingTextInvertedMarginFactor
    {
        get => _drawingDefaults.TextInvertedMarginFactor;
        set => _drawingDefaults.TextInvertedMarginFactor = value;
    }

    public CadColor DrawingTextStrokeColor
    {
        get => _drawingDefaults.TextStrokeColor;
        set => _drawingDefaults.TextStrokeColor = value;
    }

    public double DrawingTextLineWeight
    {
        get => _drawingDefaults.TextLineWeight;
        set => _drawingDefaults.TextLineWeight = value;
    }

    public int DrawingTextZIndex
    {
        get => _drawingDefaults.TextZIndex;
        set => _drawingDefaults.TextZIndex = value;
    }

    public bool DrawingTextIsVisible
    {
        get => _drawingDefaults.TextIsVisible;
        set => _drawingDefaults.TextIsVisible = value;
    }

    public StyleId? DrawingTextStyleId
    {
        get => _drawingDefaults.TextStyleId;
        set => _drawingDefaults.TextStyleId = value;
    }

    public CadColor DrawingArcStrokeColor
    {
        get => _drawingDefaults.ArcStrokeColor;
        set => _drawingDefaults.ArcStrokeColor = value;
    }

    public double DrawingArcLineWeight
    {
        get => _drawingDefaults.ArcLineWeight;
        set => _drawingDefaults.ArcLineWeight = value;
    }

    public int DrawingArcZIndex
    {
        get => _drawingDefaults.ArcZIndex;
        set => _drawingDefaults.ArcZIndex = value;
    }

    public bool DrawingArcIsVisible
    {
        get => _drawingDefaults.ArcIsVisible;
        set => _drawingDefaults.ArcIsVisible = value;
    }

    public bool IsPanning => _pan.IsPanning;
    public CadUserSettings UserSettings { get; private set; } = CadUserSettings.CreateDefault();

    public CadDocumentViewModel(
        IPublisher<CadDocumentInteractionStateChangedMessage> interactionStateChangedPublisher,
        IPublisher<CadDocumentViewSettingsChangedMessage> viewSettingsChangedPublisher,
        ISubscriber<CadOleObjectUpdatedMessage> oleObjectUpdatedSubscriber,
        ICadClipboardStore clipboardStore,
        IImageImportService imageImportService,
        IOleImportService oleImportService)
    {
        _interactionStateChangedPublisher = interactionStateChangedPublisher;
        _viewSettingsChangedPublisher = viewSettingsChangedPublisher;
        _imageImportService = imageImportService ?? throw new ArgumentNullException(nameof(imageImportService));
        _oleImportService = oleImportService ?? throw new ArgumentNullException(nameof(oleImportService));
        _oleObjectUpdatedSubscription = (oleObjectUpdatedSubscriber ?? throw new ArgumentNullException(nameof(oleObjectUpdatedSubscriber)))
            .Subscribe(OnOleObjectUpdated);
        Direct2DImageRenderHost.SetOleDrawCallback(DrawOleObjectForRender);
        Direct2DImageRenderHost.SetOleReleaseCallback(ReleaseOleRenderSession);
        _paste = new CadPasteInteractionController(clipboardStore);
        _drawingDefaults.SettingChanged += OnDrawingDefaultChanged;
        CadEditor.EditorStateChanged += OnEditorStateChanged;
    }

    internal void ReplaceEditor(CadEditor editor)
    {
        var wasAttached = _renderResources.IsAttached;
        if (wasAttached)
            DetachRenderResources();

        _oleImportService.EndEditSessions(_oleEditSessionId);
        _oleImportService.ReleaseRenderSessions(_oleEditSessionId);
        _openOleEditEntityIds.Clear();
        CadEditor.EditorStateChanged -= OnEditorStateChanged;
        CadEditor = editor ?? throw new ArgumentNullException(nameof(editor));
        CadEditor.EditorStateChanged += OnEditorStateChanged;
        _viewportInitialization.ResetInitialView();
        _viewportInitialization.ApplyCurrentSize(CadEditor);
        RefreshPointerWorldStatus();
        _pasteTargetLayerId = ResolveExistingDrawingLayerId(_pasteTargetLayerId);
        ClearInteractionState(clearClipboard: false, render: false);
        _overlayScenes.ClearHandleScene();

        if (wasAttached)
            AttachRenderResources();

        RaiseInteractionStateChanged();
        RequestRender();
    }

    public void AttachRenderResources()
    {
        ThrowIfDisposed();
        _renderResources.Attach(
            CadEditor,
            Direct2DImageRenderHost,
            _overlayScenes.TransientScene,
            _overlayScenes.HandleScene,
            OnDocumentChanged);
    }

    public void DetachRenderResources()
    {
        _renderResources.Detach(CadEditor, Direct2DImageRenderHost, OnDocumentChanged);
    }

    public void SetViewportSize(double width, double height)
    {
        _viewportInitialization.SetViewportSize(CadEditor, width, height);
        RefreshPointerWorldStatus();
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

    public void SetBackgroundColor(CadColor color)
    {
        if (CadEditor.Document.ViewSettings.BackgroundColor == color)
            return;

        CadEditor.Document.ViewSettings.BackgroundColor = color;
        PublishViewSettingsChanged();
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

        _drawingDefaults.UpdateStrokeColors(previousColor, newColor);
        _drawingDefaults.UpdateLineWeights(
            ResolveDrawingLineWeightDisplayValue(previousLineWeight),
            ResolveDrawingLineWeightDisplayValue(newLineWeight));
    }

    private void UpdateDrawingDefaultsForLayerSelection(CadLayer previousLayer, CadLayer newLayer)
    {
        _drawingDefaults.UpdateStrokeColors(ResolveLayerStrokeColor(previousLayer), ResolveLayerStrokeColor(newLayer));
        _drawingDefaults.UpdateLineWeights(
            ResolveDrawingLineWeightDisplayValue(previousLayer.LineWeight),
            ResolveDrawingLineWeightDisplayValue(newLayer.LineWeight));
    }

    private LayerId ResolveDrawingLayerId()
    {
        if (CadEditor.Document.TryGetLayer(_drawingLayerId, out var layer) && layer is not null)
            return _drawingLayerId;

        _drawingLayerId = ResolveFallbackDrawingLayerId();
        return _drawingLayerId;
    }

    private LayerId ResolveExistingDrawingLayerId(LayerId layerId)
    {
        return CadEditor.Document.TryGetLayer(layerId, out var layer) && layer is not null
            ? layerId
            : ResolveFallbackDrawingLayerId();
    }

    private LayerId ResolveExistingPasteTargetLayerId()
    {
        _pasteTargetLayerId = ResolveExistingDrawingLayerId(_pasteTargetLayerId);
        return _pasteTargetLayerId;
    }

    private LayerId ResolveFallbackDrawingLayerId()
    {
        return CadEditor.Document.Layers.Values
            .OrderBy(x => CadEditor.Document.DocumentSettings.LayerDrawingPriority.GetPriority(x.Id))
            .ThenBy(x => x.Id.Value)
            .FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("Document must contain at least one layer.");
    }

    private CadLayer ResolveDrawingLayer()
    {
        return CadEditor.Document.GetLayer(ResolveDrawingLayerId());
    }

    private void OnDrawingDefaultChanged(object? sender, CadDrawingDefaultChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.PropertyName))
            OnPropertyChanged($"Drawing{e.PropertyName}");

        RaiseInteractionStateChanged();

        if (e.RequestRender)
            RequestRender();
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

        if (_gripDrag.IsActive)
            return CommitActiveGripDrag(screen);

        if (_paste.IsPreviewActive)
        {
            CommitPaste(screen);
            return CadCanvasInteractionResult.HandledOnly;
        }

        if (CadCanvasToolMode == CadCanvasToolMode.Select)
        {
            if (TryBeginGripDrag(screen))
                return new CadCanvasInteractionResult(true, CaptureMouse: true, Cursor: CadCanvasCursorKind.Hand);

            _selectionDrag.Begin(screen);
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

        if (_pan.Move(CadEditor, screen))
            requiresFullRender = true;

        UpdatePointerWorldStatus(screen);

        if (_gripDrag.IsActive)
        {
            _gripDrag.UpdatePointer(ScreenToSnappedWorld, screen);
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

        if (button == CadCanvasPointerButton.Left && _gripDrag.IsActive)
            return KeepActiveGripDragAfterRelease(screen);

        if (CadCanvasToolMode == CadCanvasToolMode.Select &&
            button == CadCanvasPointerButton.Left &&
            _selectionDrag.IsDragging)
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
            drawGripHandles: true,
            updateHandleScene: true);
        return CadCanvasInteractionResult.HandledOnly;
    }

    public CadCanvasInteractionResult Escape()
    {
        CadCanvasToolMode = CadCanvasToolMode.Select;
        ClearInteractionState(clearClipboard: false);
        EndPan();
        RaiseInteractionStateChanged();
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
        _paste.Copy(CreateClipboardInteractionService());
    }

    public void SelectEntities(IEnumerable<EntityId> entityIds)
    {
        ArgumentNullException.ThrowIfNull(entityIds);

        var resolvedEntityIds = entityIds
            .Where(entityId =>
                CadEditor.Document.TryGetEntity(entityId, out var entity) &&
                entity is { IsErased: false })
            .Distinct()
            .ToArray();

        CadEditor.Selection.Replace(resolvedEntityIds);
        ClearInteractionState(clearClipboard: false, render: false);
        RaiseInteractionStateChanged();
        RequestRender();
    }

    public CadCanvasInteractionResult DeleteSelection()
    {
        var entityIds = CadEditor.Selection.EntityIds
            .Where(entityId =>
                CadEditor.Document.TryGetEntity(entityId, out var entity) &&
                entity is { IsErased: false })
            .ToArray();

        if (entityIds.Length == 0)
            return CadCanvasInteractionResult.NotHandled;

        CadEditor.DeleteEntities(entityIds);
        CadEditor.Selection.Clear();
        ClearInteractionState(clearClipboard: false, render: false);
        RaiseInteractionStateChanged();
        RequestRender();

        return new CadCanvasInteractionResult(
            true,
            ReleaseMouseCapture: true,
            Cursor: CadCanvasCursorKind.Cross);
    }

    public CadCanvasInteractionResult BeginPastePreview()
    {
        if (!_paste.BeginPreview(CreateClipboardInteractionService()))
            return CadCanvasInteractionResult.NotHandled;

        return BeginPastePreviewCore();
    }

    public CadCanvasInteractionResult BeginClipboardPastePreview()
    {
        if (_paste.HasUserCopySnapshot)
            return BeginPastePreview();

        CadOleImportData? oleObject;
        try
        {
            oleObject = _oleImportService.LoadFromClipboard();
        }
        catch
        {
            oleObject = null;
        }

        if (oleObject is not null)
        {
            _paste.SetSnapshot(CreateOleObjectClipboardSnapshot(oleObject));
            return BeginPastePreviewCore();
        }

        CadImageImportData? image;
        try
        {
            image = _imageImportService.LoadFromClipboard();
        }
        catch
        {
            image = null;
        }

        if (image is not null)
        {
            _paste.SetSnapshot(CreateImageClipboardSnapshot(image));
            return BeginPastePreviewCore();
        }

        return BeginPastePreview();
    }

    public CadCanvasInteractionResult OpenOleObjectAt(CadPointD screen)
    {
        var world = ScreenToWorld(screen, snapToGrid: false);
        var queryBounds = CadRectD.FromCenter(world, 1e-6, 1e-6);
        var oleObject = CadEditor.SpatialIndex.Query(queryBounds)
            .Select(entityId => CadEditor.Document.TryGetEntity(entityId, out var entity) ? entity : null)
            .OfType<CadOleObject>()
            .Where(entity => !entity.IsErased && entity.IsVisible && entity.Bounds.Contains(world))
            .OrderByDescending(entity => CadEditor.Document.DocumentSettings.LayerDrawingPriority.GetPriority(entity.LayerId))
            .ThenByDescending(entity => entity.ZIndex)
            .ThenByDescending(entity => entity.Id.Value)
            .FirstOrDefault();

        if (oleObject is null)
            return CadCanvasInteractionResult.NotHandled;

        try
        {
            _oleImportService.BeginEdit(
                _oleEditSessionId,
                oleObject.Id,
                oleObject.CopyOleBytes(),
                string.IsNullOrWhiteSpace(oleObject.Name) ? oleObject.SourceName : oleObject.Name);
            _openOleEditEntityIds.Add(oleObject.Id);
        }
        catch
        {
            return CadCanvasInteractionResult.HandledOnly;
        }

        return CadCanvasInteractionResult.HandledOnly;
    }

    private void OnOleObjectUpdated(CadOleObjectUpdatedMessage message)
    {
        if (_disposed || message.SessionId != _oleEditSessionId ||
            !CadEditor.Document.TryGetEntity(message.EntityId, out var entity) ||
            entity is not CadOleObject oleObject)
        {
            return;
        }

        if (!message.IsPersisted)
        {
            Direct2DImageRenderHost.InvalidateOleBitmap(message.EntityId);
            RequestRender();
            return;
        }

        if (message.Data is null || !HasOleDataChanged(oleObject, message.Data))
            return;

        // Storage changes are a document command; view-only changes are redrawn from the active OLE session.
        _isApplyingOleHostUpdate = true;
        try
        {
            CadEditor.SetOleObjectData(
                message.EntityId,
                message.Data.OleBytes,
                message.Data.ContentType,
                message.Data.SourceName);
        }
        finally
        {
            _isApplyingOleHostUpdate = false;
        }
    }

    private Direct2DOleDrawData? DrawOleObjectForRender(Direct2DOleDrawRequest request)
    {
        var drawData = _oleImportService.DrawOleObject(
            _oleEditSessionId,
            new CadOleDrawRequest(
                request.RenderKey.EntityId,
                request.RenderKey.RenderId,
                request.OleBytes,
                request.FullPixelWidth,
                request.FullPixelHeight,
                request.RegionX,
                request.RegionY,
                request.PixelWidth,
                request.PixelHeight));

        return drawData is null
            ? null
            : new Direct2DOleDrawData(
                drawData.PixelWidth,
                drawData.PixelHeight,
                drawData.Stride,
                drawData.Pixels);
    }

    private static bool HasOleDataChanged(CadOleObject oleObject, CadOleImportData updated)
    {
        return !oleObject.OleBytes.SequenceEqual(updated.OleBytes) ||
               !string.Equals(oleObject.ContentType, updated.ContentType, StringComparison.Ordinal) ||
               !string.Equals(oleObject.SourceName, updated.SourceName, StringComparison.Ordinal);
    }

    public CadCanvasInteractionResult CompleteCurrentDrawing()
    {
        if (CreateDrawingClickHandler().CompleteCurrentDrawing())
        {
            RequestRender();
            return CadCanvasInteractionResult.HandledOnly;
        }

        return CadCanvasInteractionResult.NotHandled;
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
            _overlayScenes.UpdateOverlayScenes(
                CadEditor,
                CreateTransientItems(),
                updateHandleScene,
                _gripDrag.CreateActiveHandleItems(CadEditor, CreateHandleSceneBuildOptions()),
                CreateHandleSceneBuildOptions());
            _overlayScenes.RefreshLastOverlayInvalidation(
                CreateRenderInvalidationCalculator(),
                drawGripHandles);
            effectiveInvalidation = CadRenderInvalidation.Full;
        }
        else
        {
            var overlayInvalidation = _overlayScenes.UpdateOverlayScenesAndCreateInvalidation(
                CreateRenderInvalidationCalculator(),
                CadEditor,
                CreateTransientItems(),
                drawGripHandles,
                updateHandleScene,
                _gripDrag.CreateActiveHandleItems(CadEditor, CreateHandleSceneBuildOptions()),
                CreateHandleSceneBuildOptions());
            effectiveInvalidation = requestedInvalidation.Union(overlayInvalidation);
        }

        Direct2DImageRenderHost.SetRenderOptions(CreateRenderOptions(drawGripHandles));
        Direct2DImageRenderHost.Render(effectiveInvalidation);
    }

    private void UpdateTextMeasurements()
    {
        _renderResources.UpdateTextMeasurements(CadEditor, Direct2DImageRenderHost);
    }

    private void BeginPan(CadPointD screen)
    {
        _pan.Begin(screen);
        OnPropertyChanged(nameof(IsPanning));
    }

    private void EndPan()
    {
        _pan.End();
        OnPropertyChanged(nameof(IsPanning));
    }

    private void HandleDrawingClick(CadPointD screen)
    {
        var world = ScreenToWorld(screen, snapToGrid: true);
        if (CreateDrawingClickHandler().HandleClick(world))
            RequestRender();
    }

    private void CompleteSelection(CadPointD endScreen)
    {
        if (_selectionDrag.Complete(CreateSelectionInteractionService(), endScreen))
            RequestRender();
    }

    private void CommitPaste(CadPointD screen)
    {
        var target = ScreenToWorld(screen, snapToGrid: true);
        var createdIds = _paste.Commit(
            CreateClipboardInteractionService(),
            target,
            PasteTargetLayerId);
        if (createdIds.Count > 0)
        {
            CadEditor.Selection.Replace(createdIds);
            RaiseInteractionStateChanged();
        }

        OnPropertyChanged(nameof(IsPastePreviewActive));
        RequestRender();
    }

    private CadCanvasInteractionResult BeginPastePreviewCore()
    {
        _pasteTargetLayerId = ResolveExistingDrawingLayerId(DrawingLayerId);
        _drawingState.Clear();
        _selectionDrag.Clear();
        OnPropertyChanged(nameof(PasteTargetLayerId));
        OnPropertyChanged(nameof(IsPastePreviewActive));
        RaiseInteractionStateChanged();
        RequestRender();
        return new CadCanvasInteractionResult(true, Cursor: CadCanvasCursorKind.Cross);
    }

    private void ClearInteractionState(bool clearClipboard, bool render = true)
    {
        _drawingState.Clear();
        _selectionDrag.Clear();
        _gripDrag.Clear();
        _paste.Clear(clearClipboard);
        OnPropertyChanged(nameof(IsPastePreviewActive));

        _overlayScenes.ClearTransientScene();

        if (render)
        {
            RequestRender();
        }
    }

    private CadRenderOptions CreateRenderOptions(bool drawGripHandles = true)
    {
        return new CadRenderOptions
        {
            DrawGripHandles = drawGripHandles,
            IsAntialiasingEnabled = UserSettings.Rendering.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = UserSettings.Rendering.IsTextAntialiasingEnabled,
            HiddenEntityIds = _gripDrag.ResolveHiddenEntityIds(CadEditor).ToHashSet()
        };
    }

    private IReadOnlyList<CadTransientItem> CreateTransientItems()
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

        return items;
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
        _overlayScenes.UpdateHandleScene(
            CadEditor,
            activeHandleItems: null,
            CreateHandleSceneBuildOptions());

        if (!_gripDrag.TryBegin(CadEditor, _overlayScenes.HandleScene, ScreenToSnappedWorld, screen))
            return false;

        _selectionDrag.Clear();
        _paste.Clear(clearClipboard: false);
        RequestRender();
        return true;
    }

    private void AddGripDragPreview(List<CadTransientItem> items)
    {
        CreateGripDragPreviewBuilder().AddPreview(items, _gripDrag.ActiveDrag);
    }

    private CadGripDragPreviewBuilder CreateGripDragPreviewBuilder()
    {
        return new CadGripDragPreviewBuilder(CadEditor, CreatePreviewStyleService(), CreateTextMeasurementService());
    }

    private void CommitGripDrag(CadPointD screen)
    {
        _gripDrag.Commit(CadEditor, CreateGripDragCommitter(), ScreenToSnappedWorld, screen);
        RequestRender();
    }

    private CadCanvasInteractionResult CommitActiveGripDrag(CadPointD screen)
    {
        CommitGripDrag(screen);
        return new CadCanvasInteractionResult(
            true,
            ReleaseMouseCapture: true,
            Cursor: CadCanvasCursorKind.Cross);
    }

    private CadCanvasInteractionResult KeepActiveGripDragAfterRelease(CadPointD screen)
    {
        _gripDrag.UpdatePointer(ScreenToSnappedWorld, screen);
        RequestOverlayRender();
        return new CadCanvasInteractionResult(
            true,
            ReleaseMouseCapture: true,
            Cursor: CadCanvasCursorKind.Hand);
    }

    private CadGripDragCommitter CreateGripDragCommitter()
    {
        return new CadGripDragCommitter(CadEditor, CreateTextMeasurementService());
    }

    private void AddPastePreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        _paste.AddPreview(CreateClipboardInteractionService(), items, mouseWorld, PasteTargetLayerId);
    }

    private void AddSelectionWindowPreview(List<CadTransientItem> items, CadPointD mousePoint)
    {
        _selectionDrag.AddPreview(CreateSelectionInteractionService(), items, mousePoint);
    }

    private CadClipboardInteractionService CreateClipboardInteractionService()
    {
        return new CadClipboardInteractionService(CadEditor);
    }

    private CadSelectionInteractionService CreateSelectionInteractionService()
    {
        return new CadSelectionInteractionService(CadEditor, CadEditor.Viewport, CreatePreviewStyleService());
    }

    private CadClipboardSnapshot CreateImageClipboardSnapshot(CadImageImportData image)
    {
        var layer = ResolveDrawingLayer();
        var bounds = CreateClipboardImageBounds(image);
        var state = new CadEntityStateClipboardSnapshot(
            image.SourceName,
            null,
            UseLayerColor: true,
            UseLayerLineWeight: true,
            IsVisible: true,
            IsLocked: false,
            CadStrokeStyle.Default,
            ZIndex: 0);
        var item = new CadClipboardEntityItem(
            new CadImageClipboardSnapshot(
                state,
                bounds,
                image.PixelWidth,
                image.PixelHeight,
                image.Stride,
                image.Pixels,
                image.ContentType,
                image.SourceName),
            new CadLayerClipboardSnapshot(
                layer.Name,
                layer.Color,
                layer.LineWeight,
                layer.IsVisible,
                layer.IsLocked,
                layer.IsFrozen),
            GraphicStyle: null,
            FillStyle: null,
            TextStyle: null);

        return new CadClipboardSnapshot([item], bounds.Center, bounds);
    }

    private CadClipboardSnapshot CreateOleObjectClipboardSnapshot(CadOleImportData oleObject)
    {
        var layer = ResolveDrawingLayer();
        var bounds = CreateClipboardOleObjectBounds(oleObject);
        var state = new CadEntityStateClipboardSnapshot(
            oleObject.SourceName,
            null,
            UseLayerColor: true,
            UseLayerLineWeight: true,
            IsVisible: true,
            IsLocked: false,
            CadStrokeStyle.Default,
            ZIndex: 0);
        var item = new CadClipboardEntityItem(
            new CadOleObjectClipboardSnapshot(
                state,
                bounds,
                oleObject.OleBytes,
                oleObject.ContentType,
                oleObject.SourceName,
                Guid.NewGuid()),
            new CadLayerClipboardSnapshot(
                layer.Name,
                layer.Color,
                layer.LineWeight,
                layer.IsVisible,
                layer.IsLocked,
                layer.IsFrozen),
            GraphicStyle: null,
            FillStyle: null,
            TextStyle: null);

        return new CadClipboardSnapshot([item], bounds.Center, bounds);
    }

    private CadRectD CreateClipboardImageBounds(CadImageImportData image)
    {
        var maxPixelSide = Math.Max(Math.Max(image.PixelWidth, image.PixelHeight), 1);
        var visible = CadEditor.Viewport.VisibleWorldBounds;
        var maxWorldSide = visible.IsEmpty
            ? Math.Max(maxPixelSide, 1)
            : Math.Max(Math.Min(visible.Width, visible.Height) * 0.35, 1.0);
        var scale = maxWorldSide / maxPixelSide;
        var width = Math.Max(image.PixelWidth * scale, 1.0);
        var height = Math.Max(image.PixelHeight * scale, 1.0);

        return CadRectD.FromCenter(CadPointD.Origin, width, height);
    }

    private CadRectD CreateClipboardOleObjectBounds(CadOleImportData oleObject)
    {
        var aspectRatio = oleObject.NaturalAspectRatio > 0 &&
                          !double.IsNaN(oleObject.NaturalAspectRatio) &&
                          !double.IsInfinity(oleObject.NaturalAspectRatio)
            ? oleObject.NaturalAspectRatio
            : 4.0 / 3.0;
        var visible = CadEditor.Viewport.VisibleWorldBounds;
        var maxWorldSide = visible.IsEmpty
            ? 100.0
            : Math.Max(Math.Min(visible.Width, visible.Height) * 0.35, 1.0);
        var width = aspectRatio >= 1.0 ? maxWorldSide : maxWorldSide * aspectRatio;
        var height = aspectRatio >= 1.0 ? maxWorldSide / aspectRatio : maxWorldSide;

        return CadRectD.FromCenter(CadPointD.Origin, width, height);
    }

    private void ReleaseOleRenderSession(Direct2DOleRenderKey renderKey)
    {
        if (renderKey.EntityId is { } entityId)
            _oleImportService.ReleaseRenderSession(_oleEditSessionId, entityId);
        else
            _oleImportService.ReleaseTransientRenderSession(_oleEditSessionId, renderKey.RenderId);
    }

    private CadHandleSceneBuildOptions CreateHandleSceneBuildOptions()
    {
        return CreatePreviewStyleService().CreateHandleSceneBuildOptions();
    }

    private CadTransientStyle CreateEntityPreviewStyle(CadEntity entity)
    {
        return CreatePreviewStyleService().CreateEntityPreviewStyle(entity);
    }

    private CadColor ResolveLayerStrokeColor(CadLayer layer)
    {
        return CreatePreviewStyleService().ResolveLayerStrokeColor(layer);
    }

    private CadPreviewStyleService CreatePreviewStyleService()
    {
        return new CadPreviewStyleService(CadEditor.Document, UserSettings);
    }

    private static double ResolveDrawingLineWeightDisplayValue(CadLineWeight lineWeight)
    {
        return CadDrawingStyleResolver.ResolveLineWeightDisplayValue(lineWeight);
    }

    private CadDrawingStyleResolver CreateDrawingStyleResolver()
    {
        return new CadDrawingStyleResolver(
            CadEditor.Document,
            ResolveDrawingLayer(),
            _drawingDefaults,
            CreatePreviewStyleService());
    }

    private CadDrawingEntityCreator CreateDrawingEntityCreator()
    {
        return new CadDrawingEntityCreator(
            CadEditor,
            ResolveDrawingLayerId(),
            _drawingDefaults,
            CreateDrawingStyleResolver(),
            CreateTextMeasurementService());
    }

    private CadDrawingClickHandler CreateDrawingClickHandler()
    {
        return new CadDrawingClickHandler(
            CadCanvasToolMode,
            _drawingState,
            CreateDrawingEntityCreator(),
            CreateMultiPointDrawingPreviewBuilder(),
            ResolveContinueArcBase,
            CreateDrawingTextRequest);
    }

    private CadContinueArcBase ResolveContinueArcBase()
    {
        return new CadContinueArcResolver(CadEditor.Document).Resolve();
    }

    private CadDrawingTextRequest CreateDrawingTextRequest()
    {
        return new CadDrawingTextRequest(
            ResolveDrawingText(),
            ResolveDrawingTextStyleId(),
            ResolveDrawingTextInvertedMarginFactor());
    }

    private void AddDrawingPreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        CreateDrawingPreviewDispatcher().AddPreview(items, mouseWorld);
    }

    private CadDrawingPreviewDispatcher CreateDrawingPreviewDispatcher()
    {
        return new CadDrawingPreviewDispatcher(
            CadCanvasToolMode,
            _drawingState,
            _drawingDefaults,
            CreateDrawingStyleResolver(),
            CreatePreviewStyleService(),
            CreateMeasurementBuilder(),
            CreateMultiPointDrawingPreviewBuilder(),
            CreateTextMeasurementService(),
            CadEditor.Document,
            CadEditor.Viewport,
            ResolveContinueArcBase,
            CreateDrawingTextRequest);
    }

    private CadTransientMeasurementBuilder CreateMeasurementBuilder()
    {
        return new CadTransientMeasurementBuilder(CadEditor.Document, CadEditor.Viewport);
    }

    private CadMultiPointDrawingPreviewBuilder CreateMultiPointDrawingPreviewBuilder()
    {
        return new CadMultiPointDrawingPreviewBuilder(
            CadEditor.Document,
            CadEditor.Viewport,
            CreateDrawingStyleResolver());
    }

    private void AddSnapMarker(List<CadTransientItem> items, CadPointD rawWorld, CadPointD snappedWorld)
    {
        CreateSnapInteractionService().AddSnapMarker(items, rawWorld, snappedWorld);
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

    private CadPointD ScreenToSnappedWorld(CadPointD screen)
    {
        return ScreenToWorld(screen, snapToGrid: true);
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
        return CreateSnapInteractionService().SnapWorld(world);
    }

    private CadSnapInteractionService CreateSnapInteractionService()
    {
        return new CadSnapInteractionService(CadEditor.Document, CadEditor.Viewport);
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

    private static bool IsFinitePositive(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private CadTextMeasurementService CreateTextMeasurementService()
    {
        return new CadTextMeasurementService(
            CadEditor.Document,
            Direct2DImageRenderHost,
            CadEditor.Viewport);
    }

    private void OnDocumentChanged(object? sender, CadDocumentChangeSet e)
    {
        CloseStaleOleEditSessions();
        CloseReplacedOleEditSessions(e);
        ReleaseChangedOleRenderSessions(e);

        if (!_renderResources.IsApplyingTextMeasurementChanges)
            RequestRender(CreateDocumentInvalidation(e));

        if (e.AffectsViewSettings)
            PublishViewSettingsChanged();

        if (e.DocumentChanged)
            RaiseInteractionStateChanged();
    }

    private void CloseReplacedOleEditSessions(CadDocumentChangeSet changes)
    {
        if (_isApplyingOleHostUpdate || _openOleEditEntityIds.Count == 0)
            return;

        foreach (var change in changes.EntityChanges)
        {
            if ((change.Kind & CadEntityChangeKind.EmbeddedData) == 0 ||
                !_openOleEditEntityIds.Remove(change.EntityId))
            {
                continue;
            }

            _oleImportService.EndEditSession(_oleEditSessionId, change.EntityId);
        }
    }

    private void ReleaseChangedOleRenderSessions(CadDocumentChangeSet changes)
    {
        foreach (var change in changes.EntityChanges)
        {
            if (!ShouldReleaseOleRenderSession(change))
                continue;

            _oleImportService.ReleaseRenderSession(_oleEditSessionId, change.EntityId);
        }
    }

    private bool ShouldReleaseOleRenderSession(CadEntityChange change)
    {
        if ((change.Kind & CadEntityChangeKind.Deleted) != 0)
            return true;

        if ((change.Kind & CadEntityChangeKind.Appearance) == 0)
            return false;

        return CadEditor.Document.TryGetEntity(change.EntityId, out var entity) &&
               entity is CadOleObject;
    }

    private void CloseStaleOleEditSessions()
    {
        if (_openOleEditEntityIds.Count == 0)
            return;

        foreach (var entityId in _openOleEditEntityIds.ToArray())
        {
            if (CadEditor.Document.TryGetEntity(entityId, out var entity) &&
                entity is CadOleObject &&
                !entity.IsErased)
            {
                continue;
            }

            _oleImportService.EndEditSession(_oleEditSessionId, entityId);
            _openOleEditEntityIds.Remove(entityId);
        }
    }

    private void OnEditorStateChanged(object? sender, CadEditorCommandResult e)
    {
        if (e.SelectionChanged)
            RaiseInteractionStateChanged();
    }

    private void RaiseInteractionStateChanged()
    {
        _interactionStateChangedPublisher.Publish(new CadDocumentInteractionStateChangedMessage(this));
    }

    private void PublishViewSettingsChanged()
    {
        _viewSettingsChangedPublisher.Publish(new CadDocumentViewSettingsChangedMessage(this));
    }

    private CadRenderInvalidation CreateDocumentInvalidation(CadDocumentChangeSet changes)
    {
        return CreateRenderInvalidationCalculator().CreateDocumentInvalidation(changes);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DetachRenderResources();
        _oleImportService.EndEditSessions(_oleEditSessionId);
        _oleImportService.ReleaseRenderSessions(_oleEditSessionId);
        _openOleEditEntityIds.Clear();
        _oleObjectUpdatedSubscription.Dispose();
        CadEditor.EditorStateChanged -= OnEditorStateChanged;
        _drawingDefaults.SettingChanged -= OnDrawingDefaultChanged;
        Direct2DImageRenderHost.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CadDocumentViewModel));
    }

}
