using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.CommandLine;
using Direct2dCad.Commands;
using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.Lang.Strings;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.Rendering.Direct2D.Ole;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Drawing;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Drawing;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Rendering;
using Direct2dCad.ViewModels.Services.Snapping;
using Direct2dCad.ViewModels.Services.Styling;
using Direct2dCad.ViewModels.Services.Text;
using MessagePipe;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel : ObservableObject, ICadDocumentViewModelMessageSource, ICadCommandLineContext, IDisposable
{
    private readonly IPublisher<CadDocumentInteractionStateChangedMessage> _interactionStateChangedPublisher;
    private readonly IPublisher<CadDocumentViewSettingsChangedMessage> _viewSettingsChangedPublisher;
    private readonly IPublisher<CadSelectionFilterChangedMessage> _selectionFilterChangedPublisher;
    private readonly IAsyncPublisher<CadCommandActivityMessage> _commandActivityPublisher;
    private readonly IAsyncPublisher<CadInteractionActivityMessage> _interactionActivityPublisher;
    private readonly Func<CadPointD, CadPointD> _screenToWorld;
    private readonly Func<CadPointD, CadPointD> _worldToScreen;
    private readonly Func<CadPointD, CadPointD> _screenToSnappedWorld;
    private readonly Func<CadEntity, bool> _canSelectEntity;
    private readonly Func<CadEntity, CadTransientStyle> _createEntityPreviewStyle;
    private readonly Func<CadContinueArcBase> _resolveContinueArcBase;
    private readonly Func<CadDrawingTextRequest> _createDrawingTextRequest;
    private readonly Func<BlockId, CadRectD, IReadOnlyList<EntityId>> _entityBoundsQuery;
    private readonly Action<BlockId, CadRectD, List<EntityId>> _entityBoundsQueryInto;
    private readonly IDisposable _oleObjectUpdatedSubscription;
    private readonly Guid _oleEditSessionId = Guid.NewGuid();
    private readonly HashSet<EntityId> _openOleEditEntityIds = [];
    private bool _isApplyingOleHostUpdate;
    private readonly CadOverlaySceneCoordinator _overlayScenes = new();
    private readonly CadDocumentInvalidationTracker _documentInvalidation = new();
    private readonly CadRenderResourceCoordinator _renderResources = new();
    private readonly CadGripDragController _gripDrag = new(new CadHandleHitTester());
    private readonly CadViewportInitializationState _viewportInitialization = new();
    private readonly CadSpaceViewportState _spaceViewportState = new();
    private readonly CadPanInteractionController _pan = new();
    private readonly CadPasteInteractionController _paste;
    private readonly IImageImportService _imageImportService;
    private readonly IClipboardTextService _clipboardTextService;
    private readonly IOleHostService _oleHostService;
    private readonly ISnackbarService _snackbarService;
    private readonly CadSelectionDragController _selectionDrag = new();
    private readonly CadSelectionCycleController _selectionCycle = new();
    private readonly CadDrawingSessionState _drawingState = new();
    private readonly CadLayoutViewportCreationState _layoutViewportCreation = new();
    private readonly List<CadTransientItem> _transientItemBuffer = new(16);
    private readonly CadViewport _layoutInteractionViewport = new();
    private readonly Dictionary<LayoutId, LayoutViewportId> _preferredLayoutViewports = [];
    private readonly HashSet<Type> _disabledSelectionEntityTypes = [];
    private LayerId _drawingLayerId = LayerId.Default;
    private LayerId _pasteTargetLayerId = LayerId.Default;
    private CadPointD? _currentMousePoint;
    private CadCommandLinePoint? _lastCommandLineInputPoint;
    private CadPointD? _layoutPanLastScreen;
    private CadLayoutViewportSnapshot? _layoutPanInitialSnapshot;
    private bool _layoutPanHasMoved;
    private bool _viewportInteractionRequiresHandleSceneUpdate;
    private CadRenderInvalidation _deferredDocumentInvalidation = CadRenderInvalidation.Empty;
    private CadRenderInvalidation _pendingRenderInvalidation = CadRenderInvalidation.Empty;
    private Action<Action>? _renderScheduler;
    private bool _hasPendingRender;
    private bool _renderScheduled;
    private bool _pendingDrawGripHandles = true;
    private bool _pendingUpdateHandleScene;
    private bool _pendingBaseSceneChanged;
    private long _renderSchedulerVersion;
    private bool _fitToWindowPending;
    private BlockId? _insertBlockDefinitionId;
    private double _insertBlockRotationRadians;
    private double _insertBlockScaleX = 1;
    private double _insertBlockScaleY = 1;
    private bool _disposed;

    [ObservableProperty]
    public partial CadEditor CadEditor { get; private set; } = new(CadDocument.Create("Untitled"));

    public Direct2DImageRenderHost Direct2DImageRenderHost { get; } = new();

    [ObservableProperty]
    public partial double CurrentPointerWorldX { get; private set; }

    [ObservableProperty]
    public partial double CurrentPointerWorldY { get; private set; }

    [ObservableProperty]
    public partial double RenderFramesPerSecond { get; private set; }

    [ObservableProperty]
    public partial double RenderFrameTimeMilliseconds { get; private set; }

    [ObservableProperty]
    public partial bool ShowFramesPerSecond { get; private set; } = true;

    [ObservableProperty]
    public partial CadCanvasToolMode CadCanvasToolMode { get; internal set; } = CadCanvasToolMode.Select;

    [ObservableProperty]
    public partial LayoutId? ActiveLayoutId { get; private set; }

    [ObservableProperty]
    public partial LayoutViewportId? ActiveLayoutViewportId { get; private set; }

    public bool IsModelSpaceActive => ActiveLayoutId is null;
    public bool IsLayoutViewportActive => ActiveLayoutId is not null && ActiveLayoutViewportId is not null;
    public bool IsPaperSpaceActive => ActiveLayoutId is not null && ActiveLayoutViewportId is null;
    public BlockId? EditingBlockId { get; private set; }
    public bool IsEditingBlock => EditingBlockId is not null;
    public string EditingBlockName =>
        EditingBlockId is { } blockId &&
        CadEditor.Document.TryGetBlock(blockId, out var block) &&
        block is not null
            ? block.Name
            : string.Empty;
    public CadLayoutSpaceMode ActiveLayoutSpaceMode
    {
        get => IsLayoutViewportActive ? CadLayoutSpaceMode.Model : CadLayoutSpaceMode.Paper;
        set
        {
            if (ActiveLayoutId is null || value == ActiveLayoutSpaceMode)
                return;

            if (value == CadLayoutSpaceMode.Paper)
            {
                ExitLayoutViewport();
                return;
            }

            var layout = CadEditor.Document.GetLayout(ActiveLayoutId.Value);
            var viewport = ResolvePreferredLayoutViewport(layout);
            if (viewport is not null)
                ActivateLayoutViewport(viewport.Id);
        }
    }

    public LayerId DrawingLayerId
    {
        get => ResolveDrawingLayerId();
        set
        {
            var previousLayerId = ResolveDrawingLayerId();
            var resolvedLayerId = ResolveExistingDrawingLayerId(value);
            if (_drawingLayerId.Equals(resolvedLayerId))
                return;

            var newLayer = CadEditor.Document.GetLayer(resolvedLayerId);
            _drawingLayerId = resolvedLayerId;
            OnPropertyChanged();
            UpdateDrawingDefaultsForLayerSelection(newLayer);
            RaiseInteractionStateChanged();
            RequestOverlayRender();
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
            RequestOverlayRender();
        }
    }

    public bool IsPastePreviewActive => _paste.IsPreviewActive;
    internal CadClipboardSnapshot? ActivePasteSnapshot => _paste.IsPreviewActive ? _paste.Snapshot : null;
    public BlockId? BlockInsertionDefinitionId => _insertBlockDefinitionId;
    public double BlockInsertionRotationDegrees => _insertBlockRotationRadians * 180.0 / Math.PI;
    public double BlockInsertionScaleX => _insertBlockScaleX;
    public double BlockInsertionScaleY => _insertBlockScaleY;

    public CadDrawingDefaultsViewModel DrawingDefaults { get; } = new();

    public bool IsPanning => _pan.IsPanning || _layoutPanLastScreen is not null;
    public CadUserSettings UserSettings { get; private set; } = CadUserSettings.CreateDefault();

    public CadDocumentViewModel(
        IPublisher<CadDocumentInteractionStateChangedMessage> interactionStateChangedPublisher,
        IPublisher<CadDocumentViewSettingsChangedMessage> viewSettingsChangedPublisher,
        IPublisher<CadSelectionFilterChangedMessage> selectionFilterChangedPublisher,
        IAsyncPublisher<CadCommandActivityMessage> commandActivityPublisher,
        IAsyncPublisher<CadInteractionActivityMessage> interactionActivityPublisher,
        ISubscriber<CadOleObjectUpdatedMessage> oleObjectUpdatedSubscriber,
        ICadClipboardStore clipboardStore,
        IImageImportService imageImportService,
        IClipboardTextService clipboardTextService,
        IOleHostService oleHostService,
        ISnackbarService snackbarService)
    {
        _interactionStateChangedPublisher = interactionStateChangedPublisher;
        _viewSettingsChangedPublisher = viewSettingsChangedPublisher;
        _selectionFilterChangedPublisher = selectionFilterChangedPublisher;
        _commandActivityPublisher = commandActivityPublisher;
        _interactionActivityPublisher = interactionActivityPublisher;
        _imageImportService = imageImportService ?? throw new ArgumentNullException(nameof(imageImportService));
        _clipboardTextService = clipboardTextService ?? throw new ArgumentNullException(nameof(clipboardTextService));
        _oleHostService = oleHostService ?? throw new ArgumentNullException(nameof(oleHostService));
        _snackbarService = snackbarService ?? throw new ArgumentNullException(nameof(snackbarService));
        _screenToWorld = ScreenToWorld;
        _worldToScreen = WorldToScreen;
        _screenToSnappedWorld = ScreenToSnappedWorld;
        _canSelectEntity = CanSelectEntity;
        _createEntityPreviewStyle = CreateEntityPreviewStyle;
        _resolveContinueArcBase = ResolveContinueArcBase;
        _createDrawingTextRequest = CreateDrawingTextRequest;
        _entityBoundsQuery = QueryEntityBounds;
        _entityBoundsQueryInto = QueryEntityBounds;
        _oleObjectUpdatedSubscription = (oleObjectUpdatedSubscriber ?? throw new ArgumentNullException(nameof(oleObjectUpdatedSubscriber)))
            .Subscribe(OnOleObjectUpdated);
        Direct2DImageRenderHost.SetOleDrawCallback(DrawOleObjectForRender);
        Direct2DImageRenderHost.SetOleReleaseCallback(ReleaseOleRenderSession);
        _paste = new CadPasteInteractionController(clipboardStore);
        DrawingDefaults.DefaultsChanged += OnDrawingDefaultsChanged;
        CadEditor.EditorStateChanged += OnEditorStateChanged;
        CadEditor.CommandActivity += OnCommandActivity;
    }

    internal void ReplaceEditor(CadEditor editor)
    {
        var wasAttached = _renderResources.IsAttached;
        if (wasAttached)
            DetachRenderResources();

        _oleHostService.EndEditSessions(_oleEditSessionId);
        _oleHostService.ReleaseRenderSessions(_oleEditSessionId);
        _openOleEditEntityIds.Clear();
        CadEditor.EditorStateChanged -= OnEditorStateChanged;
        CadEditor.CommandActivity -= OnCommandActivity;
        CadEditor = editor ?? throw new ArgumentNullException(nameof(editor));
        CadEditor.EditorStateChanged += OnEditorStateChanged;
        CadEditor.CommandActivity += OnCommandActivity;
        _viewportInitialization.ResetInitialView();
        _spaceViewportState.Reset();
        _preferredLayoutViewports.Clear();
        ActiveLayoutViewportId = null;
        ActiveLayoutId = null;
        SetEditingBlock(null);
        CadEditor.ActiveOwnerBlockId = BlockId.ModelSpace;
        _viewportInitialization.ApplyCurrentSize(CadEditor);
        RefreshPointerWorldStatus();
        _pasteTargetLayerId = ResolveExistingDrawingLayerId(_pasteTargetLayerId);
        ClearInteractionState(clearClipboard: false, render: false);
        RefreshDrawingEntityName();
        _overlayScenes.ClearHandleScene();

        if (wasAttached)
            AttachRenderResources();

        RaiseInteractionStateChanged();
        RequestRender();
    }

    public void AttachRenderResources()
    {
        ThrowIfDisposed();
        _documentInvalidation.Reset(
            CadEditor.Document,
            CreateRenderInvalidationCalculator());
        _deferredDocumentInvalidation = CadRenderInvalidation.Empty;
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
        if (_fitToWindowPending && width > 1 && height > 1)
            FitToWindow();
    }

    public void SetRenderSize(int width, int height)
    {
        Direct2DImageRenderHost.SetSize(Math.Max(1, width), Math.Max(1, height));
    }

    public void ApplyUserSettings(CadUserSettings? settings)
    {
        UserSettings = settings ?? CadUserSettings.CreateDefault();
        UserSettings.Normalize();
        ShowFramesPerSecond = UserSettings.Rendering.ShowFramesPerSecond;
        RequestRender();
    }

    public void SetBackgroundColor(CadColor color)
    {
        if (CadEditor.Document.ViewSettings.BackgroundColor == color)
            return;

        CadEditor.SetBackgroundColor(color);
    }

    public void UpdateDrawingDefaultsForLayerAppearance(
        LayerId layerId,
        CadColor newColor,
        CadLineWeight newLineWeight)
    {
        if (!layerId.Equals(ResolveDrawingLayerId()))
            return;

        DrawingDefaults.UpdateLayerDefaults(
            newColor,
            ResolveDrawingLineWeightDisplayValue(newLineWeight));
    }

    private void UpdateDrawingDefaultsForLayerSelection(CadLayer newLayer)
    {
        DrawingDefaults.UpdateLayerDefaults(
            ResolveLayerStrokeColor(newLayer),
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

    private bool EnsureLayerAcceptsEntities(LayerId layerId)
    {
        if (CadEntityAccessPolicy.CanAddToLayer(CadEditor.Document, layerId))
            return true;

        var layer = CadEditor.Document.GetLayer(layerId);
        var resourceKey = layer.IsFrozen
            ? "LayerFrozenMessageFormat"
            : "LayerLockedMessageFormat";
        var fallback = layer.IsFrozen
            ? "Layer \"{0}\" is frozen."
            : "Layer \"{0}\" is locked.";
        var format = Strings.ResourceManager.GetString(resourceKey) ?? fallback;
        _snackbarService.Enqueue(string.Format(format, layer.Name));
        return false;
    }

    private void OnDrawingDefaultsChanged(object? sender, EventArgs e)
    {
        RaiseInteractionStateChanged();
        RequestOverlayRender();
    }

    public CadCanvasInteractionResult SetToolMode(CadCanvasToolMode toolMode)
    {
        var modeChanged = CadCanvasToolMode != toolMode;
        if (modeChanged && toolMode != CadCanvasToolMode.Select)
            CadEditor.Selection.Clear();
        if (toolMode != CadCanvasToolMode.InsertBlock)
            _insertBlockDefinitionId = null;
        if (toolMode != CadCanvasToolMode.LayoutViewport)
            _layoutViewportCreation.Clear();
        CadCanvasToolMode = toolMode;
        if (modeChanged)
            RefreshDrawingEntityName();
        _lastCommandLineInputPoint = null;
        ClearInteractionState(clearClipboard: false);
        RaiseInteractionStateChanged(clearBlockDefinitionSelection: modeChanged);
        if (modeChanged)
            PublishInteractionActivity($"Tool mode: {toolMode}");
        return new CadCanvasInteractionResult(
            true,
            ReleaseMouseCapture: true,
            Cursor: CadCanvasCursorKind.Cross);
    }

    public CadCanvasInteractionResult PointerDown(
        CadPointD screen,
        CadCanvasPointerButton button,
        bool forcePan,
        CadCanvasInputModifiers modifiers = CadCanvasInputModifiers.None)
    {
        _currentMousePoint = screen;
        UpdatePointerWorldStatus(screen);
        _selectionCycle.Clear();

        if (forcePan || button is CadCanvasPointerButton.Middle or CadCanvasPointerButton.Right)
        {
            if (!BeginPan(screen))
                return CadCanvasInteractionResult.HandledOnly;
            return new CadCanvasInteractionResult(true, CaptureMouse: true, Cursor: CadCanvasCursorKind.Hand);
        }

        if (button != CadCanvasPointerButton.Left)
            return CadCanvasInteractionResult.NotHandled;

        if (CadCanvasToolMode == CadCanvasToolMode.LayoutViewport)
        {
            if (_layoutViewportCreation.IsAdjustingView)
            {
                CompleteLayoutViewportCreation();
                return CadCanvasInteractionResult.HandledOnly;
            }

            HandleLayoutViewportCreationClick(screen);
            return CadCanvasInteractionResult.HandledOnly;
        }

        if (_gripDrag.IsActive)
            return CommitActiveGripDrag(screen);

        if (_paste.IsPreviewActive)
        {
            CommitPaste(screen);
            return CadCanvasInteractionResult.HandledOnly;
        }

        if (CadCanvasToolMode == CadCanvasToolMode.Select)
        {
            var toggleSelection = modifiers.HasFlag(CadCanvasInputModifiers.Shift);
            if (!toggleSelection && TryBeginGripDrag(screen))
                return new CadCanvasInteractionResult(true, CaptureMouse: true, Cursor: CadCanvasCursorKind.Hand);

            _selectionDrag.Begin(
                screen,
                toggleSelection ? CadSelectionMode.Toggle : CadSelectionMode.Replace);
            RequestOverlayRender();
            return new CadCanvasInteractionResult(true, CaptureMouse: true);
        }

        if (CadCanvasToolMode == CadCanvasToolMode.InsertBlock)
        {
            CommitBlockInsertion(screen);
            return CadCanvasInteractionResult.HandledOnly;
        }

        HandleDrawingClick(screen);
        return CadCanvasInteractionResult.HandledOnly;
    }

    public CadCanvasInteractionResult PointerMove(CadPointD screen)
    {
        _currentMousePoint = screen;
        var requiresFullRender = false;

        if (_pan.IsPanning &&
            !Direct2DImageRenderHost.IsViewportInteractionActive)
        {
            Direct2DImageRenderHost.BeginViewportInteraction();
        }

        if (MovePan(screen))
            requiresFullRender = true;

        UpdatePointerWorldStatus(screen);

        if (_gripDrag.IsActive)
        {
            _gripDrag.UpdatePointer(_screenToSnappedWorld, screen);
            if (requiresFullRender)
            {
                if (!RenderPanInteractionPreview())
                    RequestRender(CadRenderInvalidation.Full, updateHandleScene: true);
            }
            else if (!Direct2DImageRenderHost.IsViewportInteractionActive)
            {
                RequestOverlayRender(updateHandleScene: true);
            }
            return new CadCanvasInteractionResult(true, Cursor: CadCanvasCursorKind.Hand);
        }

        if (requiresFullRender)
        {
            if (!RenderPanInteractionPreview())
                RequestRender(CadRenderInvalidation.Full, updateHandleScene: false);
        }
        else if (!Direct2DImageRenderHost.IsViewportInteractionActive)
        {
            RequestOverlayRender();
        }
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
        _selectionCycle.Clear();
        var factor = delta > 0 ? 1.1 : 1.0 / 1.1;
        if (TryGetActiveLayoutViewport(out var layout, out var layoutViewport))
        {
            if (layoutViewport.IsLocked)
                return CadCanvasInteractionResult.HandledOnly;

            var paperPoint = CadEditor.Viewport.ScreenToWorld(screen);
            var anchoredModelPoint = CadLayoutViewportMapper.PaperToModel(layoutViewport, paperPoint);
            var targetScale = Math.Clamp(layoutViewport.Scale * factor, 1e-6, 1e6);
            var dx = (paperPoint.X - layoutViewport.Bounds.Center.X) / targetScale;
            var dy = (paperPoint.Y - layoutViewport.Bounds.Center.Y) / targetScale;
            var cos = Math.Cos(layoutViewport.RotationRadians);
            var sin = Math.Sin(layoutViewport.RotationRadians);
            var center = new CadPointD(
                anchoredModelPoint.X - dx * cos - dy * sin,
                anchoredModelPoint.Y + dx * sin - dy * cos);
            var target = CadLayoutViewportSnapshot.From(layoutViewport) with
            {
                ModelCenter = center,
                Scale = targetScale
            };
            if (_layoutViewportCreation.IsAdjustingView)
            {
                CadEditor.SetLayoutViewport(
                    layout.Id,
                    layoutViewport.Id,
                    target,
                    _layoutViewportCreation.BatchId);
            }
            else
            {
                CadEditor.SetLayoutViewport(layout.Id, layoutViewport.Id, target);
            }
            UpdatePointerWorldStatus(screen);
            RequestRender();
            return CadCanvasInteractionResult.HandledOnly;
        }

        if (UserSettings.Rendering.IsZoomSnapshotPreviewEnabled)
            Direct2DImageRenderHost.BeginViewportInteraction();
        _viewportInteractionRequiresHandleSceneUpdate = true;
        CadEditor.Execute(new ZoomViewportCommand(screen, factor));
        UpdatePointerWorldStatus(screen);
        if (!RenderZoomInteractionPreview())
        {
            Direct2DImageRenderHost.EndViewportInteraction();
            _viewportInteractionRequiresHandleSceneUpdate = false;
            RequestRender(
                CadRenderInvalidation.Full,
                drawGripHandles: true,
                updateHandleScene:
                    CadEditor.Selection.EntityIds.Count <=
                    CadHandleSceneBuildOptions.DefaultMaximumIndividualGripEntityCount);
        }
        return CadCanvasInteractionResult.HandledOnly;
    }

    public void CompleteViewportInteractionPreview()
    {
        if (!Direct2DImageRenderHost.IsViewportInteractionActive)
        {
            _viewportInteractionRequiresHandleSceneUpdate = false;
            return;
        }

        Direct2DImageRenderHost.EndViewportInteraction();
        var updateHandleScene = _viewportInteractionRequiresHandleSceneUpdate;
        _viewportInteractionRequiresHandleSceneUpdate = false;
        RequestRender(
            CadRenderInvalidation.Full,
            drawGripHandles: true,
            updateHandleScene: updateHandleScene);
    }

    public void CancelViewportInteractionPreview()
    {
        Direct2DImageRenderHost.EndViewportInteraction();
        _viewportInteractionRequiresHandleSceneUpdate = false;
    }

    public CadCanvasInteractionResult CycleSelection(bool backwards)
    {
        if (CadCanvasToolMode != CadCanvasToolMode.Select ||
            !_selectionCycle.Cycle(CadEditor, backwards, CanSelectEntity))
        {
            return CadCanvasInteractionResult.NotHandled;
        }

        RequestOverlayRender(updateHandleScene: true);
        return CadCanvasInteractionResult.HandledOnly;
    }

    public CadCanvasInteractionResult Escape()
    {
        if (CadCanvasToolMode == CadCanvasToolMode.LayoutViewport)
        {
            var cancelledBatchId = _layoutViewportCreation.IsAdjustingView
                ? _layoutViewportCreation.BatchId
                : Guid.Empty;
            _layoutViewportCreation.Clear();
            if (cancelledBatchId != Guid.Empty)
                CadEditor.UndoBatch(cancelledBatchId);
            else if (IsLayoutViewportActive)
                ExitLayoutViewport();
        }
        CadCanvasToolMode = CadCanvasToolMode.Select;
        CadEditor.Selection.Clear();
        _lastCommandLineInputPoint = null;
        ClearInteractionState(clearClipboard: false);
        EndPan();
        RaiseInteractionStateChanged(clearBlockDefinitionSelection: true);
        PublishInteractionActivity("Cancel current interaction");
        return new CadCanvasInteractionResult(
            true,
            ReleaseMouseCapture: true,
            Cursor: CadCanvasCursorKind.Cross);
    }

    public void Undo()
    {
        CadEditor.Undo();
    }

    public void Redo()
    {
        CadEditor.Redo();
    }

    [RelayCommand]
    public void FitToWindow()
    {
        if (CadEditor.Viewport.ViewWidth <= 1 || CadEditor.Viewport.ViewHeight <= 1)
        {
            _fitToWindowPending = true;
            return;
        }

        _fitToWindowPending = false;
        if (TryGetActiveLayoutViewport(out var activeLayout, out var activeViewport))
        {
            if (activeViewport.IsLocked)
                return;

            var modelBounds = CadEditor.Document.Entities.Values
                .Where(entity =>
                    !entity.IsErased &&
                    entity.IsVisible &&
                    entity.OwnerBlockId.Equals(BlockId.ModelSpace))
                .Aggregate(CadRectD.Empty, static (bounds, entity) => bounds.Union(entity.Bounds));
            var target = CadLayoutViewportSnapshot.From(activeViewport);
            if (modelBounds.IsEmpty)
            {
                target = target with { ModelCenter = CadPointD.Origin, Scale = 1 };
            }
            else
            {
                var scale = Math.Min(
                    activeViewport.Bounds.Width * 0.9 / Math.Max(modelBounds.Width, 1e-9),
                    activeViewport.Bounds.Height * 0.9 / Math.Max(modelBounds.Height, 1e-9));
                target = target with
                {
                    ModelCenter = modelBounds.Center,
                    Scale = Math.Clamp(scale, 1e-6, 1e6)
                };
            }
            CadEditor.SetLayoutViewport(activeLayout.Id, activeViewport.Id, target);
            RequestRender();
            return;
        }

        if (ActiveLayoutId is { } layoutId &&
            CadEditor.Document.TryGetLayout(layoutId, out var layout) &&
            layout is not null)
        {
            FitBounds(layout.PaperBounds, 36);
            RequestRender();
            return;
        }

        CadEditor.Execute(new FitViewportCommand(ownerBlockId: CadEditor.ActiveOwnerBlockId));
        RequestRender();
    }

    public void ActivateModelSpace()
    {
        SetEditingBlock(null);
        _fitToWindowPending = false;
        if (ActiveLayoutId is { } previousLayoutId)
            _spaceViewportState.Capture(CadEditor.Viewport, previousLayoutId);

        _layoutViewportCreation.Clear();
        if (CadCanvasToolMode == CadCanvasToolMode.LayoutViewport)
            CadCanvasToolMode = CadCanvasToolMode.Select;
        ActiveLayoutViewportId = null;
        ActiveLayoutId = null;
        CadEditor.ActiveOwnerBlockId = BlockId.ModelSpace;
        CadEditor.Selection.Clear();
        OnPropertyChanged(nameof(IsModelSpaceActive));
        RaiseActiveSpacePropertiesChanged();
        ClearInteractionState(clearClipboard: false, render: false);
        _spaceViewportState.TryRestore(CadEditor.Viewport, layoutId: null);
        RefreshPointerWorldStatus();
        RequestRender();
        RaiseInteractionStateChanged();
    }

    public void ActivateLayout(LayoutId layoutId)
    {
        SetEditingBlock(null);
        _fitToWindowPending = false;
        if (ActiveLayoutId is { } previousLayoutId)
            _spaceViewportState.Capture(CadEditor.Viewport, previousLayoutId);
        else
            _spaceViewportState.Capture(CadEditor.Viewport, layoutId: null);

        _layoutViewportCreation.Clear();
        if (CadCanvasToolMode == CadCanvasToolMode.LayoutViewport)
            CadCanvasToolMode = CadCanvasToolMode.Select;
        var layout = CadEditor.Document.GetLayout(layoutId);
        ActiveLayoutViewportId = null;
        ActiveLayoutId = layout.Id;
        CadEditor.ActiveOwnerBlockId = layout.PaperSpaceBlockId;
        CadEditor.Selection.Clear();
        OnPropertyChanged(nameof(IsModelSpaceActive));
        RaiseActiveSpacePropertiesChanged();
        ClearInteractionState(clearClipboard: false, render: false);
        if (!_spaceViewportState.TryRestore(CadEditor.Viewport, layout.Id))
            FitToWindow();
        else
        {
            RefreshPointerWorldStatus();
            RequestRender();
        }
        RaiseInteractionStateChanged();
    }

    public void ActivateLayoutViewport(LayoutViewportId viewportId)
    {
        if (ActiveLayoutId is not { } layoutId)
            throw new InvalidOperationException("A paper layout must be active before activating its viewport.");

        var viewport = CadEditor.Document.GetLayout(layoutId).GetViewport(viewportId);
        if (!viewport.IsVisible)
            throw new InvalidOperationException("A hidden layout viewport cannot be activated.");

        _preferredLayoutViewports[layoutId] = viewport.Id;
        ActiveLayoutViewportId = viewport.Id;
        CadEditor.ActiveOwnerBlockId = BlockId.ModelSpace;
        CadEditor.Selection.Clear();
        ClearInteractionState(clearClipboard: false, render: false);
        RaiseActiveSpacePropertiesChanged();
        RefreshPointerWorldStatus();
        RequestRender();
        RaiseInteractionStateChanged();
        PublishInteractionActivity($"Activate layout viewport {viewport.Id.Value}");
    }

    public void ExitLayoutViewport()
    {
        if (ActiveLayoutId is not { } layoutId || ActiveLayoutViewportId is null)
            return;

        ActiveLayoutViewportId = null;
        CadEditor.ActiveOwnerBlockId = CadEditor.Document.GetLayout(layoutId).PaperSpaceBlockId;
        CadEditor.Selection.Clear();
        ClearInteractionState(clearClipboard: false, render: false);
        RaiseActiveSpacePropertiesChanged();
        RefreshPointerWorldStatus();
        RequestRender();
        RaiseInteractionStateChanged();
        PublishInteractionActivity("Return to paper space");
    }

    public void SetPreferredLayoutViewport(LayoutViewportId viewportId)
    {
        if (ActiveLayoutId is not { } layoutId)
            return;

        var layout = CadEditor.Document.GetLayout(layoutId);
        if (layout.Viewports.Any(item => item.Id == viewportId))
            _preferredLayoutViewports[layoutId] = viewportId;
    }

    public LayoutViewportId? GetPreferredLayoutViewportId(LayoutId layoutId)
    {
        var layout = CadEditor.Document.GetLayout(layoutId);
        return ResolvePreferredLayoutViewport(layout)?.Id;
    }

    private CadLayoutViewport? ResolvePreferredLayoutViewport(CadLayout layout)
    {
        if (_preferredLayoutViewports.TryGetValue(layout.Id, out var preferredId))
        {
            var preferred = layout.Viewports.FirstOrDefault(item => item.Id == preferredId && item.IsVisible);
            if (preferred is not null)
                return preferred;
        }

        return layout.Viewports.FirstOrDefault(item => item.IsVisible);
    }

    [RelayCommand]
    public void BeginLayoutViewportCreation()
    {
        if (ActiveLayoutId is null)
            return;

        if (IsLayoutViewportActive)
            ExitLayoutViewport();
        SetToolMode(CadCanvasToolMode.LayoutViewport);
        _layoutViewportCreation.Begin();
        PublishInteractionActivity("MVIEW: specify the first corner");
        RequestRender();
    }

    private void HandleLayoutViewportCreationClick(CadPointD screen)
    {
        if (!_layoutViewportCreation.IsActive ||
            ActiveLayoutId is not { } layoutId ||
            IsLayoutViewportActive)
            return;

        var layout = CadEditor.Document.GetLayout(layoutId);
        var raw = SnapWorld(CadEditor.Viewport.ScreenToWorld(screen));
        var point = new CadPointD(
            Math.Clamp(raw.X, layout.PaperBounds.Left, layout.PaperBounds.Right),
            Math.Clamp(raw.Y, layout.PaperBounds.Bottom, layout.PaperBounds.Top));
        if (_layoutViewportCreation.FirstCorner is null)
        {
            _layoutViewportCreation.SetFirstCorner(point);
            PublishInteractionActivity("MVIEW: specify the opposite corner");
            RequestOverlayRender();
            return;
        }

        var bounds = _layoutViewportCreation.CreateBounds(point);
        var minimumPaperSize = 8.0 / Math.Max(CadEditor.Viewport.Zoom, 1e-6);
        if (bounds.Width < minimumPaperSize || bounds.Height < minimumPaperSize)
            return;

        ResolveModelFit(bounds, out var modelCenter, out var scale);
        var viewportId = CadEditor.AddLayoutViewport(
            layoutId,
            bounds,
            modelCenter,
            scale,
            rotationRadians: 0,
            batchId: _layoutViewportCreation.BatchId);
        _layoutViewportCreation.BeginAdjusting(viewportId);
        ActivateLayoutViewport(viewportId);
        PublishInteractionActivity("MVIEW: use the wheel and right-drag to adjust; click to finish");
    }

    private void CompleteLayoutViewportCreation()
    {
        if (!_layoutViewportCreation.IsActive)
            return;

        _layoutViewportCreation.Clear();
        if (IsLayoutViewportActive)
            ExitLayoutViewport();
        SetToolMode(CadCanvasToolMode.Select);
        PublishInteractionActivity("MVIEW complete");
    }

    public CadCanvasInteractionResult HandleDoubleClick(CadPointD screen)
    {
        if (CadCanvasToolMode == CadCanvasToolMode.LayoutViewport)
            return CadCanvasInteractionResult.NotHandled;
        if (ActiveLayoutId is not { } layoutId)
            return CadCanvasInteractionResult.NotHandled;

        var layout = CadEditor.Document.GetLayout(layoutId);
        var paperPoint = CadEditor.Viewport.ScreenToWorld(screen);
        if (ActiveLayoutViewportId is { } activeViewportId)
        {
            if (!layout.GetViewport(activeViewportId).Bounds.Contains(paperPoint))
            {
                ExitLayoutViewport();
                return CadCanvasInteractionResult.HandledOnly;
            }
            return CadCanvasInteractionResult.NotHandled;
        }

        var viewport = layout.Viewports
            .Where(item => item.IsVisible && item.Bounds.Contains(paperPoint))
            .LastOrDefault();
        if (viewport is null)
            return CadCanvasInteractionResult.NotHandled;

        ActivateLayoutViewport(viewport.Id);
        return CadCanvasInteractionResult.HandledOnly;
    }

    public CadClipboardSnapshot? CopySelection()
    {
        var snapshot = _paste.Copy(CreateClipboardInteractionService());
        if (snapshot is null)
            return null;

        var blockReferenceCount = snapshot.Items.Count(item => item.Entity is CadBlockReferenceClipboardSnapshot);
        var blockDetails = blockReferenceCount > 0
            ? $", {blockReferenceCount} block references, {snapshot.BlockDefinitions.Count} block definitions"
            : string.Empty;
        PublishInteractionActivity(
            $"Copy selection ({snapshot.Items.Count} entities{blockDetails})");
        return snapshot;
    }

    public void SelectEntities(IEnumerable<EntityId> entityIds)
    {
        ArgumentNullException.ThrowIfNull(entityIds);

        var resolvedEntityIds = entityIds
            .Where(entityId =>
                CadEditor.Document.TryGetEntity(entityId, out var entity) &&
                entity is { IsErased: false } &&
                CanSelectEntity(entity))
            .Distinct()
            .ToArray();

        CadEditor.Selection.Replace(resolvedEntityIds);
        ClearInteractionState(clearClipboard: false, render: false);
        RaiseInteractionStateChanged();
        RequestOverlayRender(updateHandleScene: true);
    }

    public bool PanToEntity(EntityId entityId)
    {
        if (!CadEditor.Document.TryGetEntity(entityId, out var entity) ||
            entity is null ||
            entity.IsErased ||
            entity.Bounds.IsEmpty ||
            !TryActivateEntityOwnerSpace(entity.OwnerBlockId))
        {
            return false;
        }

        var viewport = CadEditor.Viewport;
        if (viewport.ViewWidth <= 1 || viewport.ViewHeight <= 1)
            return false;

        var entityScreenCenter = viewport.WorldToScreen(entity.Bounds.Center);
        var viewportCenter = new CadPointD(viewport.ViewWidth * 0.5, viewport.ViewHeight * 0.5);
        var screenDelta = viewportCenter - entityScreenCenter;
        if (Math.Abs(screenDelta.X) > 0.5 || Math.Abs(screenDelta.Y) > 0.5)
            CadEditor.Execute(new PanViewportCommand(screenDelta));

        RefreshPointerWorldStatus();
        RequestRender();
        return true;
    }

    private bool TryActivateEntityOwnerSpace(BlockId ownerBlockId)
    {
        if (ownerBlockId.Equals(BlockId.ModelSpace))
        {
            if (ActiveLayoutId is not null)
                ActivateModelSpace();
            return true;
        }

        var layout = CadEditor.Document.Layouts.Values
            .FirstOrDefault(item => item.PaperSpaceBlockId.Equals(ownerBlockId));
        if (layout is null)
            return ownerBlockId.Equals(CadEditor.ActiveOwnerBlockId);

        if (!ActiveLayoutId.Equals(layout.Id) || ActiveLayoutViewportId is not null)
            ActivateLayout(layout.Id);
        return true;
    }

    public CadCanvasInteractionResult SelectAllEntities()
    {
        var entityIds = GetSelectableEntityIds();
        if (!CadEditor.Selection.EntityIds.ToHashSet().SetEquals(entityIds))
            CadEditor.Execute(new SetSelectionCommand(entityIds, "Select All"));

        ClearInteractionState(clearClipboard: false, render: false);
        RaiseInteractionStateChanged();
        RequestOverlayRender(updateHandleScene: true);
        return CadCanvasInteractionResult.HandledOnly;
    }

    private EntityId[] GetSelectableEntityIds()
    {
        return CadEditor.Document.Entities.Values
            .Where(entity => !entity.IsErased && CanSelectEntity(entity))
            .Select(entity => entity.Id)
            .ToArray();
    }

    public bool IsEntityTypeSelectionEnabled(Type entityType)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return !_disabledSelectionEntityTypes.Contains(entityType);
    }

    public void SetEntityTypeSelectionEnabled(Type entityType, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        if (!typeof(CadEntity).IsAssignableFrom(entityType))
            throw new ArgumentException("Type must derive from CadEntity.", nameof(entityType));

        var changed = isEnabled
            ? _disabledSelectionEntityTypes.Remove(entityType)
            : _disabledSelectionEntityTypes.Add(entityType);
        if (!changed)
            return;

        if (PruneSelectionToFilter())
        {
            RaiseInteractionStateChanged();
            RequestOverlayRender(updateHandleScene: true);
        }

        _selectionFilterChangedPublisher.Publish(new CadSelectionFilterChangedMessage(this));
    }

    public IReadOnlyCollection<string> GetDisabledSelectionEntityTypeKeys()
    {
        return _disabledSelectionEntityTypes
            .Select(CadSelectionEntityTypeCatalog.GetKey)
            .Where(key => key is not null)
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public void ApplyDisabledSelectionEntityTypeKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var disabledTypes = keys
            .Select(CadSelectionEntityTypeCatalog.GetEntityType)
            .Where(entityType => entityType is not null)
            .Cast<Type>()
            .ToHashSet();
        if (_disabledSelectionEntityTypes.SetEquals(disabledTypes))
            return;

        _disabledSelectionEntityTypes.Clear();
        _disabledSelectionEntityTypes.UnionWith(disabledTypes);

        if (PruneSelectionToFilter())
        {
            RaiseInteractionStateChanged();
            RequestOverlayRender(updateHandleScene: true);
        }

        _selectionFilterChangedPublisher.Publish(new CadSelectionFilterChangedMessage(this));
    }

    private bool CanSelectEntity(CadEntity entity)
    {
        return entity.OwnerBlockId.Equals(CadEditor.ActiveOwnerBlockId) &&
               CadEntityAccessPolicy.IsSelectable(CadEditor.Document, entity) &&
               !_disabledSelectionEntityTypes.Contains(entity.GetType());
    }

    private bool PruneSelectionToFilter()
    {
        var filteredSelection = CadEditor.Selection.EntityIds
            .Where(entityId =>
                CadEditor.Document.TryGetEntity(entityId, out var entity) &&
                entity is { IsErased: false } &&
                CanSelectEntity(entity))
            .ToArray();
        if (filteredSelection.Length == CadEditor.Selection.EntityIds.Count)
            return false;

        CadEditor.Selection.Replace(filteredSelection);
        return true;
    }

    public CadCanvasInteractionResult DeleteSelection()
    {
        var entityIds = CadEditor.Selection.EntityIds
            .Where(entityId =>
                CadEditor.Document.TryGetEntity(entityId, out var entity) &&
                entity is not null &&
                CadEntityAccessPolicy.IsEditable(CadEditor.Document, entity))
            .ToArray();

        if (entityIds.Length == 0)
            return CadCanvasInteractionResult.NotHandled;

        CadEditor.DeleteEntities(entityIds);
        CadEditor.Selection.Clear();
        ClearInteractionState(clearClipboard: false, render: false);
        RaiseInteractionStateChanged();

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
            oleObject = _oleHostService.LoadFromClipboard();
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

        string? text;
        try
        {
            text = _clipboardTextService.LoadFromClipboard();
        }
        catch
        {
            text = null;
        }

        if (text is not null)
        {
            DrawingDefaults.Text = text;
            return SetToolMode(CadCanvasToolMode.Text);
        }

        return BeginPastePreview();
    }

    public CadCanvasInteractionResult OpenOleObjectAt(CadPointD screen)
    {
        var world = ScreenToWorld(screen, snapToGrid: false);
        var queryBounds = CadRectD.FromCenter(world, 1e-6, 1e-6);
        var oleObject = CadEditor.SpatialIndex.Query(CadEditor.ActiveOwnerBlockId, queryBounds)
            .Select(entityId => CadEditor.Document.TryGetEntity(entityId, out var entity) ? entity : null)
            .OfType<CadOleObject>()
            .Where(entity =>
                CadEntityAccessPolicy.IsEditable(CadEditor.Document, entity) &&
                entity.OwnerBlockId.Equals(CadEditor.ActiveOwnerBlockId) &&
                entity.Bounds.Contains(world))
            .OrderByDescending(entity => CadEditor.Document.DocumentSettings.LayerDrawingPriority.GetPriority(entity.LayerId))
            .ThenByDescending(entity => entity.ZIndex)
            .ThenByDescending(entity => CadEditor.Document.GetEntityInsertionIndex(entity.Id))
            .ThenByDescending(entity => entity.Id.Value)
            .FirstOrDefault();

        if (oleObject is null)
            return CadCanvasInteractionResult.NotHandled;

        try
        {
            _oleHostService.BeginEdit(
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
            entity is not CadOleObject oleObject ||
            !CadEntityAccessPolicy.IsEditable(CadEditor.Document, oleObject))
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
        var drawData = _oleHostService.DrawOleObject(
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
        if (CadCanvasToolMode == CadCanvasToolMode.LayoutViewport)
        {
            CompleteLayoutViewportCreation();
            return CadCanvasInteractionResult.HandledOnly;
        }

        if (CreateDrawingClickHandler().CompleteCurrentDrawing())
        {
            RequestOverlayRender();
            return CadCanvasInteractionResult.HandledOnly;
        }

        return CadCanvasInteractionResult.NotHandled;
    }

    public void RequestRender()
    {
        RequestRender(CadRenderInvalidation.Full);
    }

    public void RequestRenderCacheRefresh()
    {
        RequestRender();
    }

    public void SetRenderScheduler(Action<Action>? scheduler)
    {
        if (_disposed)
            return;
        _renderScheduler = scheduler;
        _renderScheduled = false;
        _renderSchedulerVersion = unchecked(_renderSchedulerVersion + 1);
        if (scheduler is not null)
            SchedulePendingRender();
    }

    private void RequestOverlayRender(bool updateHandleScene = false)
    {
        if (IsLayoutViewportActive)
        {
            RequestRender(
                CadRenderInvalidation.Full,
                updateHandleScene: updateHandleScene,
                baseSceneChanged: false);
            return;
        }

        RequestRender(
            CadRenderInvalidation.FromScreenRect(default),
            updateHandleScene: updateHandleScene,
            baseSceneChanged: false);
    }

    private void RequestRender(
        CadRenderInvalidation? invalidation,
        bool drawGripHandles = true,
        bool updateHandleScene = true,
        bool baseSceneChanged = true)
    {
        var requestedInvalidation = invalidation ?? CadRenderInvalidation.Full;
        if (_hasPendingRender)
            _pendingRenderInvalidation = _pendingRenderInvalidation.Union(requestedInvalidation);
        else
            _pendingRenderInvalidation = requestedInvalidation;

        _hasPendingRender = true;
        _pendingDrawGripHandles = drawGripHandles;
        _pendingUpdateHandleScene |= updateHandleScene;
        _pendingBaseSceneChanged |= baseSceneChanged;
        SchedulePendingRender();
    }

    private void SchedulePendingRender()
    {
        if (!_hasPendingRender || _renderScheduled)
            return;

        if (_renderScheduler is null)
        {
            FlushPendingRender();
            return;
        }

        _renderScheduled = true;
        var schedulerVersion = _renderSchedulerVersion;
        _renderScheduler(() =>
        {
            if (_disposed || schedulerVersion != _renderSchedulerVersion)
                return;

            _renderScheduled = false;
            FlushPendingRender();
        });
    }

    private void FlushPendingRender()
    {
        if (!_hasPendingRender || _disposed)
            return;

        var invalidation = _pendingRenderInvalidation;
        var drawGripHandles = _pendingDrawGripHandles;
        var updateHandleScene = _pendingUpdateHandleScene;
        var baseSceneChanged = _pendingBaseSceneChanged;
        _hasPendingRender = false;
        _pendingRenderInvalidation = CadRenderInvalidation.Empty;
        _pendingUpdateHandleScene = false;
        _pendingBaseSceneChanged = false;

        RenderCore(
            invalidation,
            drawGripHandles,
            updateHandleScene,
            baseSceneChanged);
    }

    private void RenderCore(
        CadRenderInvalidation? invalidation,
        bool drawGripHandles,
        bool updateHandleScene,
        bool baseSceneChanged)
    {
        var interruptedViewportInteraction =
            Direct2DImageRenderHost.IsViewportInteractionActive;
        if (interruptedViewportInteraction)
        {
            Direct2DImageRenderHost.EndViewportInteraction();
            _viewportInteractionRequiresHandleSceneUpdate = false;
        }

        UpdateTextMeasurements();
        var requestedInvalidation = interruptedViewportInteraction
            ? CadRenderInvalidation.Full
            : invalidation ?? CadRenderInvalidation.Full;
        if (!_deferredDocumentInvalidation.IsEmpty)
        {
            requestedInvalidation =
                requestedInvalidation.Union(_deferredDocumentInvalidation);
            _deferredDocumentInvalidation = CadRenderInvalidation.Empty;
        }
        var interactionZoom = InteractionZoom;
        var handleOptions = CreateHandleSceneBuildOptions();
        var activeHandleItems = _gripDrag.CreateActiveHandleItems(
            CadEditor,
            handleOptions,
            interactionZoom);
        var transientItems = CreateTransientItems();
        var invalidationCalculator = CreateRenderInvalidationCalculator();
        CadRenderInvalidation effectiveInvalidation;

        if (requestedInvalidation.IsFull)
        {
            _overlayScenes.UpdateOverlayScenes(
                CadEditor,
                transientItems,
                updateHandleScene,
                activeHandleItems,
                handleOptions,
                interactionZoom);
            _overlayScenes.RefreshLastOverlayInvalidation(
                invalidationCalculator,
                drawGripHandles);
            effectiveInvalidation = CadRenderInvalidation.Full;
        }
        else
        {
            var overlayInvalidation = _overlayScenes.UpdateOverlayScenesAndCreateInvalidation(
                invalidationCalculator,
                CadEditor,
                transientItems,
                drawGripHandles,
                updateHandleScene,
                activeHandleItems,
                handleOptions,
                interactionZoom);
            effectiveInvalidation = requestedInvalidation.Union(overlayInvalidation);
        }

        Direct2DImageRenderHost.SetRenderOptions(CreateRenderOptions(drawGripHandles));
        Direct2DImageRenderHost.Render(effectiveInvalidation, baseSceneChanged);
        RenderFrameTimeMilliseconds = Direct2DImageRenderHost.AverageFrameRenderTimeMilliseconds;
        var framesPerSecond = Direct2DImageRenderHost.FramesPerSecond;
        if (!RenderFramesPerSecond.Equals(framesPerSecond))
            RenderFramesPerSecond = framesPerSecond;
    }

    private void UpdateTextMeasurements()
    {
        _renderResources.UpdateTextMeasurements(CadEditor, Direct2DImageRenderHost);
    }

    private bool BeginPan(CadPointD screen)
    {
        if (TryGetActiveLayoutViewport(out _, out var viewport))
        {
            if (viewport.IsLocked)
                return false;
            _layoutPanLastScreen = screen;
            _layoutPanInitialSnapshot = CadLayoutViewportSnapshot.From(viewport);
            _layoutPanHasMoved = false;
        }
        else
        {
            _pan.Begin(screen);
            Direct2DImageRenderHost.BeginViewportInteraction();
            _viewportInteractionRequiresHandleSceneUpdate = false;
        }
        OnPropertyChanged(nameof(IsPanning));
        return true;
    }

    private void EndPan()
    {
        var hadViewportPreview = Direct2DImageRenderHost.IsViewportInteractionActive;
        var hasMoved = _pan.End();
        if (_layoutPanInitialSnapshot is { } initial &&
            TryGetActiveLayoutViewport(out var layout, out var viewport))
        {
            hasMoved |= _layoutPanHasMoved;
            if (_layoutPanHasMoved)
            {
                var target = CadLayoutViewportSnapshot.From(viewport);
                initial.ApplyTo(viewport);
                if (_layoutViewportCreation.IsAdjustingView)
                {
                    CadEditor.SetLayoutViewport(
                        layout.Id,
                        viewport.Id,
                        target,
                        _layoutViewportCreation.BatchId);
                }
                else
                {
                    CadEditor.SetLayoutViewport(layout.Id, viewport.Id, target);
                }
            }
        }
        _layoutPanLastScreen = null;
        _layoutPanInitialSnapshot = null;
        _layoutPanHasMoved = false;
        Direct2DImageRenderHost.EndViewportInteraction();
        _viewportInteractionRequiresHandleSceneUpdate = false;
        OnPropertyChanged(nameof(IsPanning));
        if (hasMoved)
            PublishInteractionActivity("Pan View");
        if (hadViewportPreview && hasMoved)
            RequestRender(CadRenderInvalidation.Full, updateHandleScene: false);
    }

    private bool RenderPanInteractionPreview()
    {
        return RenderViewportInteractionPreview();
    }

    private bool RenderZoomInteractionPreview()
    {
        return UserSettings.Rendering.IsZoomSnapshotPreviewEnabled &&
               RenderViewportInteractionPreview();
    }

    private bool RenderViewportInteractionPreview()
    {
        if (!Direct2DImageRenderHost.RenderViewportInteractionPreview())
            return false;

        RenderFrameTimeMilliseconds =
            Direct2DImageRenderHost.AverageFrameRenderTimeMilliseconds;
        var framesPerSecond = Direct2DImageRenderHost.FramesPerSecond;
        if (!RenderFramesPerSecond.Equals(framesPerSecond))
            RenderFramesPerSecond = framesPerSecond;
        return true;
    }

    private bool MovePan(CadPointD screen)
    {
        if (_layoutPanLastScreen is not { } previousScreen ||
            !TryGetActiveLayoutViewport(out _, out var viewport))
        {
            return _pan.Move(CadEditor, screen);
        }

        _layoutPanLastScreen = screen;
        var previousPaper = CadEditor.Viewport.ScreenToWorld(previousScreen);
        var currentPaper = CadEditor.Viewport.ScreenToWorld(screen);
        var dx = (previousPaper.X - currentPaper.X) / viewport.Scale;
        var dy = (previousPaper.Y - currentPaper.Y) / viewport.Scale;
        var cos = Math.Cos(viewport.RotationRadians);
        var sin = Math.Sin(viewport.RotationRadians);
        var modelDelta = new CadVectorD(
            dx * cos + dy * sin,
            -dx * sin + dy * cos);
        if (modelDelta.LengthSquared <= double.Epsilon)
            return false;

        viewport.SetView(
            viewport.Bounds,
            viewport.ModelCenter + modelDelta,
            viewport.Scale,
            viewport.RotationRadians);
        _layoutPanHasMoved = true;
        return true;
    }

    private void HandleDrawingClick(CadPointD screen)
    {
        var world = ScreenToWorld(screen, snapToGrid: true);
        HandleDrawingWorldPoint(world);
    }

    private bool HandleDrawingWorldPoint(CadPointD world)
    {
        if (!EnsureLayerAcceptsEntities(DrawingLayerId))
            return false;

        if (!CreateDrawingClickHandler().HandleClick(world))
            return false;

        _lastCommandLineInputPoint = new CadCommandLinePoint(world.X, world.Y);
        RequestOverlayRender();
        return true;
    }

    private void CompleteSelection(CadPointD endScreen)
    {
        if (_selectionDrag.Complete(
                CreateSelectionInteractionService(),
                endScreen,
                out var cycleSeed))
        {
            _selectionCycle.Begin(cycleSeed);
            RequestOverlayRender(updateHandleScene: true);
        }
    }

    private void CommitPaste(CadPointD screen)
    {
        if (!EnsureLayerAcceptsEntities(PasteTargetLayerId))
            return;

        var target = ScreenToWorld(screen, snapToGrid: true);
        var createdIds = _paste.Commit(
            CreateClipboardInteractionService(),
            target,
            PasteTargetLayerId);
        if (createdIds.Count > 0)
        {
            CadEditor.Selection.Replace(createdIds.Where(entityId =>
                CadEditor.Document.TryGetEntity(entityId, out var entity) &&
                entity is not null &&
                CanSelectEntity(entity)));
            RaiseInteractionStateChanged();
        }

        OnPropertyChanged(nameof(IsPastePreviewActive));
        OnPropertyChanged(nameof(ActivePasteSnapshot));
        RequestOverlayRender(updateHandleScene: true);
    }

    private CadCanvasInteractionResult BeginPastePreviewCore()
    {
        _pasteTargetLayerId = ResolveExistingDrawingLayerId(DrawingLayerId);
        _drawingState.Clear();
        _selectionDrag.Clear();
        OnPropertyChanged(nameof(PasteTargetLayerId));
        OnPropertyChanged(nameof(IsPastePreviewActive));
        OnPropertyChanged(nameof(ActivePasteSnapshot));
        RaiseInteractionStateChanged();
        PublishInteractionActivity("Begin paste preview");
        RequestOverlayRender();
        return new CadCanvasInteractionResult(true, Cursor: CadCanvasCursorKind.Cross);
    }

    private void ClearInteractionState(bool clearClipboard, bool render = true)
    {
        _drawingState.Clear();
        _selectionDrag.Clear();
        _selectionCycle.Clear();
        _gripDrag.Clear();
        _paste.Clear(clearClipboard);
        OnPropertyChanged(nameof(IsPastePreviewActive));
        OnPropertyChanged(nameof(ActivePasteSnapshot));

        _overlayScenes.ClearTransientScene();

        if (render)
        {
            RequestOverlayRender(updateHandleScene: true);
        }
    }

    private CadRenderOptions CreateRenderOptions(bool drawGripHandles = true)
    {
        return new CadRenderOptions
        {
            ActiveOwnerBlockId = CadEditor.ActiveOwnerBlockId,
            ActiveLayoutId = ActiveLayoutId,
            ActiveLayoutViewportId = ActiveLayoutViewportId,
            DrawGrid = ActiveLayoutId is null,
            DrawOrigin = ActiveLayoutId is null,
            DrawGripHandles = drawGripHandles,
            IsAntialiasingEnabled = UserSettings.Rendering.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = UserSettings.Rendering.IsTextAntialiasingEnabled,
            IsLevelOfDetailEnabled = UserSettings.Rendering.IsLevelOfDetailEnabled,
            AllowApproximateTileScaleFallback =
                UserSettings.Rendering.AllowApproximateTileScaleFallback,
            IsBackgroundChunkRecordingEnabled =
                UserSettings.Rendering.IsBackgroundChunkRecordingEnabled,
            IsParallelRenderingEnabled =
                UserSettings.Rendering.IsParallelRenderingEnabled,
            ParallelRenderingMode =
                UserSettings.Rendering.ParallelRenderingMode,
            ParallelRenderingWorkerCount =
                UserSettings.Rendering.ParallelRenderingWorkerCount,
            EntityBoundsQuery = _entityBoundsQuery,
            EntityBoundsQueryInto = _entityBoundsQueryInto,
            HiddenEntityIds = _gripDrag.HiddenEntityIds
        };
    }

    private void FitBounds(CadRectD bounds, double padding)
    {
        var viewport = CadEditor.Viewport;
        var availableWidth = Math.Max(1, viewport.ViewWidth - padding * 2);
        var availableHeight = Math.Max(1, viewport.ViewHeight - padding * 2);
        var zoom = Math.Min(
            availableWidth / Math.Max(bounds.Width, 1),
            availableHeight / Math.Max(bounds.Height, 1));
        viewport.SetView(
            zoom,
            new CadPointD(
                viewport.ViewWidth * 0.5 - bounds.Center.X * zoom,
                viewport.ViewHeight * 0.5 + bounds.Center.Y * zoom));
    }

    private void ResolveModelFit(
        CadRectD viewportBounds,
        out CadPointD modelCenter,
        out double scale)
    {
        var modelBounds = CadEditor.Document.Entities.Values
            .Where(entity =>
                !entity.IsErased &&
                entity.IsVisible &&
                entity.OwnerBlockId.Equals(BlockId.ModelSpace))
            .Aggregate(CadRectD.Empty, static (bounds, entity) => bounds.Union(entity.Bounds));
        if (modelBounds.IsEmpty)
        {
            modelCenter = CadPointD.Origin;
            scale = 1;
            return;
        }

        modelCenter = modelBounds.Center;
        scale = Math.Clamp(Math.Min(
            viewportBounds.Width * 0.9 / Math.Max(modelBounds.Width, 1e-9),
            viewportBounds.Height * 0.9 / Math.Max(modelBounds.Height, 1e-9)), 1e-6, 1e6);
    }

    private IReadOnlyList<CadTransientItem> CreateTransientItems()
    {
        _transientItemBuffer.Clear();
        var items = _transientItemBuffer;

        if (_currentMousePoint is { } mousePoint)
        {
            var rawMouseWorld = ScreenToWorld(mousePoint);
            var snappedMouseWorld = SnapWorld(rawMouseWorld);
            AddPastePreview(items, snappedMouseWorld);
            AddSelectionWindowPreview(items, mousePoint);
            AddGripDragPreview(items);
            AddBlockInsertionPreview(items, snappedMouseWorld);
            AddDrawingPreview(items, snappedMouseWorld);
            AddSnapMarker(items, rawMouseWorld, snappedMouseWorld);
            AddLayoutViewportCreationPreview(items, mousePoint);
        }

        return items;
    }

    public CadCanvasInteractionResult BeginBlockInsertion(
        BlockId definitionBlockId,
        double rotationRadians = 0,
        double scaleX = 1,
        double scaleY = 1)
    {
        var definition = CadEditor.Document.GetBlock(definitionBlockId);
        if (definition.IsSystem)
            throw new InvalidOperationException("System space blocks cannot be inserted.");
        if (!IsValidBlockScale(scaleX) || !IsValidBlockScale(scaleY))
            throw new ArgumentOutOfRangeException(nameof(scaleX), "Block scale must be finite and non-zero.");

        SetToolMode(CadCanvasToolMode.InsertBlock);
        _insertBlockDefinitionId = definitionBlockId;
        _insertBlockRotationRadians = rotationRadians;
        _insertBlockScaleX = scaleX;
        _insertBlockScaleY = scaleY;
        RaiseInteractionStateChanged();
        RequestOverlayRender();
        return CadCanvasInteractionResult.HandledOnly;
    }

    public bool UpdateBlockInsertionTransform(
        double rotationDegrees,
        double scaleX,
        double scaleY)
    {
        if (CadCanvasToolMode != CadCanvasToolMode.InsertBlock ||
            _insertBlockDefinitionId is null)
        {
            return false;
        }

        if (!double.IsFinite(rotationDegrees))
            throw new ArgumentOutOfRangeException(nameof(rotationDegrees));
        if (!IsValidBlockScale(scaleX) || !IsValidBlockScale(scaleY))
            throw new ArgumentOutOfRangeException(nameof(scaleX), "Block scale must be finite and non-zero.");

        var rotationRadians = rotationDegrees * Math.PI / 180.0;
        if (_insertBlockRotationRadians.Equals(rotationRadians) &&
            _insertBlockScaleX.Equals(scaleX) &&
            _insertBlockScaleY.Equals(scaleY))
        {
            return true;
        }

        _insertBlockRotationRadians = rotationRadians;
        _insertBlockScaleX = scaleX;
        _insertBlockScaleY = scaleY;
        RaiseInteractionStateChanged();
        RequestOverlayRender();
        return true;
    }

    private static bool IsValidBlockScale(double value) =>
        double.IsFinite(value) && Math.Abs(value) > 1e-9;

    public void EditBlockDefinition(BlockId blockId)
    {
        var block = CadEditor.Document.GetBlock(blockId);
        if (block.IsSystem || block.IsReadOnly)
            throw new InvalidOperationException($"Block is read-only: {block.Name}");

        ActivateModelSpace();
        CadEditor.ActiveOwnerBlockId = blockId;
        SetEditingBlock(blockId);
        CadEditor.Selection.Clear();
        ClearInteractionState(clearClipboard: false, render: false);
        FitToWindow();
        RaiseInteractionStateChanged();
        PublishInteractionActivity($"Edit block: {block.Name}");
    }

    [RelayCommand]
    public void ExitBlockEditing()
    {
        if (!IsEditingBlock)
            return;
        ActivateModelSpace();
        PublishInteractionActivity("Exit block editor");
    }

    private void SetEditingBlock(BlockId? blockId)
    {
        if (EditingBlockId == blockId)
            return;
        EditingBlockId = blockId;
        OnPropertyChanged(nameof(EditingBlockId));
        OnPropertyChanged(nameof(IsEditingBlock));
        OnPropertyChanged(nameof(EditingBlockName));
    }

    private void CommitBlockInsertion(CadPointD screen)
    {
        if (_insertBlockDefinitionId is not { } definitionBlockId ||
            !EnsureLayerAcceptsEntities(DrawingLayerId))
        {
            return;
        }

        var position = ScreenToWorld(screen, snapToGrid: true);
        var definition = CadEditor.Document.GetBlock(definitionBlockId);
        var entityId = CadEditor.InsertBlockReference(
            definitionBlockId,
            position,
            DrawingLayerId,
            _insertBlockRotationRadians,
            _insertBlockScaleX,
            _insertBlockScaleY,
            definition.Name);
        CadEditor.Selection.Replace([entityId]);
        SetToolMode(CadCanvasToolMode.Select);
        RaiseInteractionStateChanged();
    }

    private void AddBlockInsertionPreview(List<CadTransientItem> items, CadPointD position)
    {
        if (CadCanvasToolMode != CadCanvasToolMode.InsertBlock ||
            _insertBlockDefinitionId is not { } definitionBlockId)
        {
            return;
        }

        items.Add(new CadTransientBlockReference(
            definitionBlockId,
            position,
            _insertBlockRotationRadians,
            _insertBlockScaleX,
            _insertBlockScaleY,
            DrawingLayerId,
            CadColorSource.ByLayer,
            GraphicStyleId: null,
            CreatePreviewStyleService().CreateSelectionWindowStyle()));
    }

    private void AddLayoutViewportCreationPreview(
        List<CadTransientItem> items,
        CadPointD mouseScreen)
    {
        if (CadCanvasToolMode != CadCanvasToolMode.LayoutViewport ||
            !_layoutViewportCreation.IsDefiningBounds ||
            ActiveLayoutId is not { } layoutId)
        {
            return;
        }

        var layout = CadEditor.Document.GetLayout(layoutId);
        var raw = SnapWorld(CadEditor.Viewport.ScreenToWorld(mouseScreen));
        var point = new CadPointD(
            Math.Clamp(raw.X, layout.PaperBounds.Left, layout.PaperBounds.Right),
            Math.Clamp(raw.Y, layout.PaperBounds.Bottom, layout.PaperBounds.Top));
        var bounds = _layoutViewportCreation.CreateBounds(point);
        if (!bounds.IsEmpty)
            items.Add(new CadTransientRectangle(
                bounds,
                CreatePreviewStyleService().CreateSelectionWindowStyle()));
    }

    private CadRenderInvalidationCalculator CreateRenderInvalidationCalculator()
    {
        return new CadRenderInvalidationCalculator(
            CadEditor.Document,
            CadEditor.Viewport,
            Direct2DImageRenderHost.TargetWidth,
            Direct2DImageRenderHost.TargetHeight,
            _createEntityPreviewStyle);
    }

    private bool TryBeginGripDrag(CadPointD screen)
    {
        _overlayScenes.UpdateHandleScene(
            CadEditor,
            activeHandleItems: null,
            CreateHandleSceneBuildOptions(),
            InteractionZoom);

        if (!_gripDrag.TryBegin(
                CadEditor,
                _overlayScenes.HandleScene,
                _worldToScreen,
                _screenToSnappedWorld,
                screen))
            return false;

        _selectionDrag.Clear();
        _paste.Clear(clearClipboard: false);
        RequestOverlayRender(updateHandleScene: true);
        return true;
    }

    private void AddGripDragPreview(List<CadTransientItem> items)
    {
        if (!_gripDrag.IsActive)
            return;

        CreateGripDragPreviewBuilder().AddPreview(items, _gripDrag.ActiveDrag);
    }

    private CadGripDragPreviewBuilder CreateGripDragPreviewBuilder()
    {
        return new CadGripDragPreviewBuilder(CadEditor, CreatePreviewStyleService(), CreateTextMeasurementService());
    }

    private void CommitGripDrag(CadPointD screen)
    {
        if (!_gripDrag.Commit(
                CadEditor,
                CreateGripDragCommitter(),
                _screenToSnappedWorld,
                screen))
        {
            RequestOverlayRender(updateHandleScene: true);
        }
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
        _gripDrag.UpdatePointer(_screenToSnappedWorld, screen);
        RequestOverlayRender(updateHandleScene: true);
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
        if (!_paste.IsPreviewActive)
            return;

        _paste.AddPreview(CreateClipboardInteractionService(), items, mouseWorld, PasteTargetLayerId);
    }

    private void AddSelectionWindowPreview(List<CadTransientItem> items, CadPointD mousePoint)
    {
        if (!_selectionDrag.IsDragging)
            return;

        _selectionDrag.AddPreview(CreateSelectionInteractionService(), items, mousePoint);
    }

    private CadClipboardInteractionService CreateClipboardInteractionService()
    {
        return new CadClipboardInteractionService(CadEditor);
    }

    private CadSelectionInteractionService CreateSelectionInteractionService()
    {
        return new CadSelectionInteractionService(
            CadEditor,
            _screenToWorld,
            InteractionZoom,
            CreatePreviewStyleService(),
            _canSelectEntity);
    }

    private CadClipboardSnapshot CreateImageClipboardSnapshot(CadImageImportData image)
    {
        var layer = ResolveDrawingLayer();
        var bounds = CreateClipboardImageBounds(image);
        var state = new CadEntityStateClipboardSnapshot(
            image.SourceName,
            null,
            ColorSource: CadColorSource.ByLayer,
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
            ColorSource: CadColorSource.ByLayer,
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
        var visible = InteractionViewport.VisibleWorldBounds;
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
        var visible = InteractionViewport.VisibleWorldBounds;
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
            _oleHostService.ReleaseRenderSession(_oleEditSessionId, entityId);
        else
            _oleHostService.ReleaseTransientRenderSession(_oleEditSessionId, renderKey.RenderId);
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
            DrawingDefaults,
            CreatePreviewStyleService());
    }

    private CadDrawingEntityCreator CreateDrawingEntityCreator()
    {
        return new CadDrawingEntityCreator(
            CadEditor,
            ResolveDrawingLayerId(),
            DrawingDefaults,
            CreateDrawingStyleResolver(),
            CreateTextMeasurementService(),
            OnDrawingEntityCreated);
    }

    private void OnDrawingEntityCreated()
    {
        RefreshDrawingEntityName();
    }

    private void RefreshDrawingEntityName()
    {
        DrawingDefaults.EntityName = CadDrawingEntityNameGenerator.CreateNext(
            CadEditor.Document,
            CadCanvasToolMode);
    }

    private CadDrawingClickHandler CreateDrawingClickHandler()
    {
        return new CadDrawingClickHandler(
            CadCanvasToolMode,
            _drawingState,
            CreateDrawingEntityCreator(),
            CreateMultiPointDrawingPreviewBuilder(),
            _resolveContinueArcBase,
            _createDrawingTextRequest);
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
            ResolveDrawingTextInvertedMarginFactor(),
            CadArc.DegreesToRadians(DrawingDefaults.TextRotationDegrees));
    }

    private void AddDrawingPreview(List<CadTransientItem> items, CadPointD mouseWorld)
    {
        if (CadCanvasToolMode is
            CadCanvasToolMode.Select or
            CadCanvasToolMode.InsertBlock or
            CadCanvasToolMode.LayoutViewport)
        {
            return;
        }

        CreateDrawingPreviewDispatcher().AddPreview(items, mouseWorld);
    }

    private CadDrawingPreviewDispatcher CreateDrawingPreviewDispatcher()
    {
        return new CadDrawingPreviewDispatcher(
            CadCanvasToolMode,
            _drawingState,
            DrawingDefaults,
            CreateDrawingStyleResolver(),
            CreatePreviewStyleService(),
            CreateMeasurementBuilder(),
            CreateMultiPointDrawingPreviewBuilder(),
            CreateTextMeasurementService(),
            CadEditor.Document,
            InteractionViewport,
            _resolveContinueArcBase,
            _createDrawingTextRequest);
    }

    private CadTransientMeasurementBuilder CreateMeasurementBuilder()
    {
        return new CadTransientMeasurementBuilder(CadEditor.Document, InteractionViewport);
    }

    private CadMultiPointDrawingPreviewBuilder CreateMultiPointDrawingPreviewBuilder()
    {
        return new CadMultiPointDrawingPreviewBuilder(
            CadEditor.Document,
            InteractionViewport,
            CreateDrawingStyleResolver());
    }

    private void AddSnapMarker(List<CadTransientItem> items, CadPointD rawWorld, CadPointD snappedWorld)
    {
        CreateSnapInteractionService().AddSnapMarker(items, rawWorld, snappedWorld);
    }

    private double InteractionZoom => TryGetActiveLayoutViewport(out _, out var viewport)
        ? Math.Max(CadEditor.Viewport.Zoom * viewport.Scale, 1e-6)
        : CadEditor.Viewport.Zoom;

    private CadViewport InteractionViewport
    {
        get
        {
            if (!TryGetActiveLayoutViewport(out _, out var layoutViewport))
                return CadEditor.Viewport;

            var viewport = _layoutInteractionViewport;
            viewport.SetSize(CadEditor.Viewport.ViewWidth, CadEditor.Viewport.ViewHeight);
            var screenCenter = CadEditor.Viewport.WorldToScreen(layoutViewport.Bounds.Center);
            var zoom = InteractionZoom;
            viewport.SetView(
                zoom,
                new CadPointD(
                    screenCenter.X - layoutViewport.ModelCenter.X * zoom,
                    screenCenter.Y + layoutViewport.ModelCenter.Y * zoom));
            return viewport;
        }
    }

    private bool TryGetActiveLayoutViewport(
        out CadLayout layout,
        out CadLayoutViewport viewport)
    {
        layout = default!;
        viewport = default!;
        if (ActiveLayoutId is not { } layoutId || ActiveLayoutViewportId is not { } viewportId ||
            !CadEditor.Document.TryGetLayout(layoutId, out var resolvedLayout) || resolvedLayout is null)
        {
            return false;
        }

        var resolvedViewport = resolvedLayout.Viewports.FirstOrDefault(item => item.Id == viewportId);
        if (resolvedViewport is null)
            return false;

        layout = resolvedLayout;
        viewport = resolvedViewport;
        return true;
    }

    private void RaiseActiveSpacePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsLayoutViewportActive));
        OnPropertyChanged(nameof(IsPaperSpaceActive));
        OnPropertyChanged(nameof(ActiveLayoutSpaceMode));
    }

    private CadPointD ScreenToWorld(CadPointD screen)
    {
        return TryGetActiveLayoutViewport(out _, out var viewport)
            ? CadLayoutViewportMapper.ScreenToModel(CadEditor.Viewport, viewport, screen)
            : CadEditor.Viewport.ScreenToWorld(screen);
    }

    private CadPointD WorldToScreen(CadPointD world)
    {
        return TryGetActiveLayoutViewport(out _, out var viewport)
            ? CadLayoutViewportMapper.ModelToScreen(CadEditor.Viewport, viewport, world)
            : CadEditor.Viewport.WorldToScreen(world);
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

    private IReadOnlyList<EntityId> QueryEntityBounds(
        BlockId ownerBlockId,
        CadRectD bounds)
    {
        return CadEditor.SpatialIndex.Query(ownerBlockId, bounds);
    }

    private void QueryEntityBounds(
        BlockId ownerBlockId,
        CadRectD bounds,
        List<EntityId> destination)
    {
        CadEditor.SpatialIndex.Query(ownerBlockId, bounds, destination);
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
        return new CadSnapInteractionService(CadEditor.Document, InteractionViewport);
    }

    private string ResolveDrawingText()
    {
        return string.IsNullOrWhiteSpace(DrawingDefaults.Text) ? "Text" : DrawingDefaults.Text;
    }

    private StyleId? ResolveDrawingTextStyleId()
    {
        return DrawingDefaults.TextStyleId is { } styleId &&
               CadEditor.Document.TryGetStyle(styleId, out var style) &&
               style is CadTextStyle
            ? styleId
            : null;
    }

    private double ResolveDrawingTextInvertedMarginFactor()
    {
        return DrawingDefaults.TextInvertedMarginFactor >= 0 &&
               !double.IsNaN(DrawingDefaults.TextInvertedMarginFactor) &&
               !double.IsInfinity(DrawingDefaults.TextInvertedMarginFactor)
            ? DrawingDefaults.TextInvertedMarginFactor
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
            InteractionViewport);
    }

    private void OnDocumentChanged(object? sender, CadDocumentChangeSet e)
    {
        _overlayScenes.ApplyDocumentChanges(e, CadEditor.Selection.EntityIds);
        var documentInvalidation = CreateDocumentInvalidation(e);
        _paste.InvalidatePreviewTemplate();
        EnsureActiveLayoutViewportStillExists();
        if (e.AffectsDocumentStructure)
            OnPropertyChanged(nameof(EditingBlockName));
        if (e.AffectsDocumentStructure &&
            !CadEntityAccessPolicy.CanAddToLayer(CadEditor.Document, DrawingLayerId))
        {
            _drawingState.Clear();
        }
        if (_gripDrag.ActiveDrag is { } drag &&
            (!CadEditor.Document.TryGetEntity(drag.Handle.EntityId, out var draggedEntity) ||
             draggedEntity is null ||
             !CadEntityAccessPolicy.IsEditable(CadEditor.Document, draggedEntity)))
        {
            _gripDrag.Clear();
        }
        CloseStaleOleEditSessions();
        CloseReplacedOleEditSessions(e);
        ReleaseChangedOleRenderSessions(e);

        if (e.DocumentChanged)
        {
            if (PruneSelectionToFilter())
                ClearInteractionState(clearClipboard: false, render: false);
            RaiseInteractionStateChanged();
        }

        if (_renderResources.IsApplyingTextMeasurementChanges)
        {
            _deferredDocumentInvalidation =
                _deferredDocumentInvalidation.Union(documentInvalidation);
        }
        else
        {
            RequestRender(documentInvalidation);
        }

        if (e.AffectsViewSettings)
            PublishViewSettingsChanged();
    }

    private void EnsureActiveLayoutViewportStillExists()
    {
        if (ActiveLayoutId is not { } layoutId || ActiveLayoutViewportId is not { } viewportId)
            return;

        if (!CadEditor.Document.TryGetLayout(layoutId, out var layout) || layout is null)
        {
            ActiveLayoutViewportId = null;
            ActiveLayoutId = null;
            CadEditor.ActiveOwnerBlockId = BlockId.ModelSpace;
            CadEditor.Selection.Clear();
            OnPropertyChanged(nameof(IsModelSpaceActive));
            RaiseActiveSpacePropertiesChanged();
            return;
        }

        if (layout.Viewports.Any(item => item.Id == viewportId && item.IsVisible))
            return;

        ActiveLayoutViewportId = null;
        CadEditor.ActiveOwnerBlockId = layout.PaperSpaceBlockId;
        CadEditor.Selection.Clear();
        RaiseActiveSpacePropertiesChanged();
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

            _oleHostService.EndEditSession(_oleEditSessionId, change.EntityId);
        }
    }

    private void ReleaseChangedOleRenderSessions(CadDocumentChangeSet changes)
    {
        foreach (var change in changes.EntityChanges)
        {
            if (!ShouldReleaseOleRenderSession(change))
                continue;

            _oleHostService.ReleaseRenderSession(_oleEditSessionId, change.EntityId);
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
                CadEntityAccessPolicy.IsEditable(CadEditor.Document, entity))
            {
                continue;
            }

            _oleHostService.EndEditSession(_oleEditSessionId, entityId);
            _openOleEditEntityIds.Remove(entityId);
        }
    }

    private void OnEditorStateChanged(object? sender, CadEditorCommandResult e)
    {
        if (e.SelectionChanged)
        {
            PruneSelectionToFilter();
            RaiseInteractionStateChanged();
        }
    }

    private void OnCommandActivity(object? sender, CadCommandActivity activity)
    {
        _commandActivityPublisher.Publish(new CadCommandActivityMessage(
            this,
            CadEditor.Document.Name,
            activity));
    }

    private void PublishInteractionActivity(string name)
    {
        _interactionActivityPublisher.Publish(new CadInteractionActivityMessage(
            this,
            CadEditor.Document.Name,
            name));
    }

    string ICadCommandLineContext.DocumentName => CadEditor.Document.Name;

    int ICadCommandLineContext.EntityCount => CadEditor.Document.Entities.Values.Count(entity => !entity.IsErased);

    int ICadCommandLineContext.SelectionCount => CadEditor.Selection.EntityIds.Count;

    CadCommandLineDrawingMode ICadCommandLineContext.ToolMode =>
        Enum.Parse<CadCommandLineDrawingMode>(CadCanvasToolMode.ToString());

    bool ICadCommandLineContext.CanUndo => CadEditor.DocumentCommands.CanUndo;

    bool ICadCommandLineContext.CanRedo => CadEditor.DocumentCommands.CanRedo;

    CadCommandLinePoint? ICadCommandLineContext.LastInputPoint => _lastCommandLineInputPoint;

    void ICadCommandLineContext.SetToolMode(CadCommandLineDrawingMode mode)
    {
        if (mode == CadCommandLineDrawingMode.LayoutViewport)
        {
            BeginLayoutViewportCreation();
            return;
        }
        SetToolMode(Enum.Parse<CadCanvasToolMode>(mode.ToString()));
    }

    void ICadCommandLineContext.Cancel() => Escape();

    void ICadCommandLineContext.Undo() => Undo();

    void ICadCommandLineContext.Redo() => Redo();

    void ICadCommandLineContext.FitToWindow() => FitToWindow();

    int ICadCommandLineContext.SelectAll()
    {
        var entityIds = GetSelectableEntityIds();
        SelectAllEntities();
        return entityIds.Length;
    }

    int ICadCommandLineContext.DeleteSelection()
    {
        var count = CadEditor.Selection.EntityIds.Count;
        return DeleteSelection().Handled ? count : 0;
    }

    CadCommandLineClipboardSummary? ICadCommandLineContext.CopySelection()
    {
        if (CadEditor.Selection.EntityIds.Count == 0)
            return null;

        return CreateClipboardSummary(CopySelection());
    }

    CadCommandLineClipboardSummary? ICadCommandLineContext.BeginPaste()
    {
        return BeginClipboardPastePreview().Handled
            ? CreateClipboardSummary(_paste.Snapshot)
            : null;
    }

    bool ICadCommandLineContext.SubmitDrawingPoint(CadCommandLinePoint point) =>
        HandleDrawingWorldPoint(SnapWorld(new CadPointD(point.X, point.Y)));

    bool ICadCommandLineContext.CompleteCurrentDrawing() => CompleteCurrentDrawing().Handled;

    CadCommandLineRenderStatistics? ICadCommandLineContext.GetRenderStatistics()
    {
        var statistics = Direct2DImageRenderHost.RenderStatistics;
        return new CadCommandLineRenderStatistics(
            Direct2DImageRenderHost.FramesPerSecond,
            Direct2DImageRenderHost.AverageFrameRenderTimeMilliseconds,
            statistics.RenderDurationMilliseconds,
            statistics.IsFullFrame,
            statistics.DirtyRegionCount,
            statistics.ScenePassCount,
            statistics.VisibleEntityCount,
            statistics.EntitySubmissionCount,
            statistics.BlockReferenceCount,
            statistics.ExpandedBlockEntityCount,
            statistics.BlockDefinitionCommandListReplayCount,
            statistics.BlockDefinitionCommandListBuildCount,
            statistics.SelectionEntityCount,
            statistics.SelectionCommandListReplayCount,
            statistics.SelectionCommandListBuildCount,
            statistics.CommandListReplayCount,
            statistics.CommandListBuildCount,
            statistics.TileReplayCount,
            statistics.TileBuildCount,
            statistics.FallbackEntityCount,
            statistics.GeometryRealizationFillDrawCount,
            statistics.GeometryRealizationStrokeDrawCount,
            statistics.GeometryRealizationBuildCount,
            statistics.GeometryRealizationFallbackCount,
            statistics.GeometryRealizationCacheHitCount,
            statistics.GeometryRealizationCacheMissCount,
            statistics.GeometryRealizationBuildMilliseconds,
            statistics.HatchLineSubmissionCount,
            statistics.HatchSimplifiedLineFamilyCount,
            statistics.OleTileBuildCount,
            statistics.SceneTileCacheBytes,
            statistics.CommandListCacheBytes,
            statistics.SelectionCommandListCacheBytes,
            statistics.BlockDefinitionCacheBytes,
            statistics.GeometryRealizationCacheBytes,
            statistics.HatchTileCacheBytes,
            statistics.ImageBitmapCacheBytes,
            statistics.OleTileCacheBytes,
            statistics.GpuCacheBytes,
            statistics.GpuCachePeakBytes,
            statistics.GpuCacheBudgetBytes,
            statistics.GpuCacheEvictionCount,
            statistics.CachePreparationMilliseconds,
            statistics.BackgroundRenderMilliseconds,
            statistics.EntityRenderMilliseconds,
            statistics.TransientRenderMilliseconds,
            statistics.SelectionRenderMilliseconds,
            statistics.OlePreparationMilliseconds,
            statistics.SurfaceDrawMilliseconds,
            statistics.ParallelFrameCount,
            statistics.ParallelMode?.ToString(),
            statistics.ParallelWorkerCount,
            statistics.ParallelEntityCount,
            statistics.ParallelRenderMilliseconds,
            statistics.ParallelGpuCacheBytes);
    }

    private static CadCommandLineClipboardSummary? CreateClipboardSummary(CadClipboardSnapshot? snapshot)
    {
        return snapshot is null
            ? null
            : new CadCommandLineClipboardSummary(
                snapshot.Items.Count,
                snapshot.Items.Count(item => item.Entity is CadBlockReferenceClipboardSnapshot),
                snapshot.BlockDefinitions.Count);
    }

    private void RaiseInteractionStateChanged(bool clearBlockDefinitionSelection = false)
    {
        _interactionStateChangedPublisher.Publish(
            new CadDocumentInteractionStateChangedMessage(
                this,
                clearBlockDefinitionSelection));
    }

    private void PublishViewSettingsChanged()
    {
        _viewSettingsChangedPublisher.Publish(new CadDocumentViewSettingsChangedMessage(this));
    }

    private CadRenderInvalidation CreateDocumentInvalidation(CadDocumentChangeSet changes)
    {
        var invalidation = _documentInvalidation.CreateInvalidation(
            CadEditor.Document,
            changes,
            CreateRenderInvalidationCalculator());
        if (ActiveLayoutId is not null)
            return CadRenderInvalidation.Full;
        return invalidation;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _renderScheduler = null;
        _hasPendingRender = false;
        _renderScheduled = false;
        _renderSchedulerVersion = unchecked(_renderSchedulerVersion + 1);
        DetachRenderResources();
        _oleHostService.EndEditSessions(_oleEditSessionId);
        _oleHostService.ReleaseRenderSessions(_oleEditSessionId);
        _openOleEditEntityIds.Clear();
        _oleObjectUpdatedSubscription.Dispose();
        CadEditor.EditorStateChanged -= OnEditorStateChanged;
        CadEditor.CommandActivity -= OnCommandActivity;
        DrawingDefaults.DefaultsChanged -= OnDrawingDefaultsChanged;
        Direct2DImageRenderHost.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CadDocumentViewModel));
    }

}
