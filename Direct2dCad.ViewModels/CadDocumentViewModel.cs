using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.CommandLine;
using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Commands;
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
using Direct2dCad.ViewModels.Drawing;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Drawing;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Rendering;
using Direct2dCad.ViewModels.Services.Snapping;
using Direct2dCad.ViewModels.Services.Styling;
using Direct2dCad.ViewModels.Services.Text;
using Direct2dCad.ViewModels.Services.Platform;
using MessagePipe;

namespace Direct2dCad.ViewModels;

public partial class CadDocumentViewModel : ObservableObject, ICadDocumentViewModelMessageSource, ICadCommandLineContext, IDisposable
{
    private readonly IPublisher<CadDocumentInteractionStateChangedMessage> _interactionStateChangedPublisher;
    private readonly IPublisher<CadDocumentViewSettingsChangedMessage> _viewSettingsChangedPublisher;
    private readonly IPublisher<CadSelectionFilterChangedMessage> _selectionFilterChangedPublisher;
    private readonly IPublisher<CadCommandActivityMessage> _commandActivityPublisher;
    private readonly IPublisher<CadInteractionActivityMessage> _interactionActivityPublisher;
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
    private readonly IClipboardTextService _clipboardTextService;
    private readonly IOleHostService _oleHostService;
    private readonly CadSelectionDragController _selectionDrag = new();
    private readonly CadSelectionCycleController _selectionCycle = new();
    private readonly CadDrawingSessionState _drawingState = new();
    private readonly CadLayoutViewportCreationState _layoutViewportCreation = new();
    private readonly HashSet<Type> _disabledSelectionEntityTypes = [];
    private LayerId _drawingLayerId = LayerId.Default;
    private LayerId _pasteTargetLayerId = LayerId.Default;
    private CadPointD? _currentMousePoint;
    private CadCommandLinePoint? _lastCommandLineInputPoint;
    private CadPointD? _layoutPanLastScreen;
    private CadLayoutViewportSnapshot? _layoutPanInitialSnapshot;
    private bool _layoutPanHasMoved;
    private bool _fitToWindowPending;
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

    [ObservableProperty]
    public partial LayoutId? ActiveLayoutId { get; private set; }

    [ObservableProperty]
    public partial LayoutViewportId? ActiveLayoutViewportId { get; private set; }

    public bool IsModelSpaceActive => ActiveLayoutId is null;
    public bool IsLayoutViewportActive => ActiveLayoutId is not null && ActiveLayoutViewportId is not null;
    public bool IsPaperSpaceActive => ActiveLayoutId is not null && ActiveLayoutViewportId is null;
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

            var viewport = CadEditor.Document.GetLayout(ActiveLayoutId.Value).Viewports
                .FirstOrDefault(item => item.IsVisible);
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

    public CadDrawingDefaultsViewModel DrawingDefaults { get; } = new();

    public bool IsPanning => _pan.IsPanning || _layoutPanLastScreen is not null;
    public CadUserSettings UserSettings { get; private set; } = CadUserSettings.CreateDefault();

    public CadDocumentViewModel(
        IPublisher<CadDocumentInteractionStateChangedMessage> interactionStateChangedPublisher,
        IPublisher<CadDocumentViewSettingsChangedMessage> viewSettingsChangedPublisher,
        IPublisher<CadSelectionFilterChangedMessage> selectionFilterChangedPublisher,
        IPublisher<CadCommandActivityMessage> commandActivityPublisher,
        IPublisher<CadInteractionActivityMessage> interactionActivityPublisher,
        ISubscriber<CadOleObjectUpdatedMessage> oleObjectUpdatedSubscriber,
        ICadClipboardStore clipboardStore,
        IImageImportService imageImportService,
        IClipboardTextService clipboardTextService,
        IOleHostService oleHostService)
    {
        _interactionStateChangedPublisher = interactionStateChangedPublisher;
        _viewSettingsChangedPublisher = viewSettingsChangedPublisher;
        _selectionFilterChangedPublisher = selectionFilterChangedPublisher;
        _commandActivityPublisher = commandActivityPublisher;
        _interactionActivityPublisher = interactionActivityPublisher;
        _imageImportService = imageImportService ?? throw new ArgumentNullException(nameof(imageImportService));
        _clipboardTextService = clipboardTextService ?? throw new ArgumentNullException(nameof(clipboardTextService));
        _oleHostService = oleHostService ?? throw new ArgumentNullException(nameof(oleHostService));
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
        CadColor previousColor,
        CadLineWeight previousLineWeight,
        CadColor newColor,
        CadLineWeight newLineWeight)
    {
        if (!layerId.Equals(ResolveDrawingLayerId()))
            return;

        DrawingDefaults.UpdateLayerDefaults(
            previousColor,
            newColor,
            ResolveDrawingLineWeightDisplayValue(previousLineWeight),
            ResolveDrawingLineWeightDisplayValue(newLineWeight));
    }

    private void UpdateDrawingDefaultsForLayerSelection(CadLayer previousLayer, CadLayer newLayer)
    {
        DrawingDefaults.UpdateLayerDefaults(
            ResolveLayerStrokeColor(previousLayer),
            ResolveLayerStrokeColor(newLayer),
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

    private void OnDrawingDefaultsChanged(object? sender, EventArgs e)
    {
        RaiseInteractionStateChanged();
        RequestRender();
    }

    public CadCanvasInteractionResult SetToolMode(CadCanvasToolMode toolMode)
    {
        var modeChanged = CadCanvasToolMode != toolMode;
        if (toolMode != CadCanvasToolMode.LayoutViewport)
            _layoutViewportCreation.Clear();
        CadCanvasToolMode = toolMode;
        _lastCommandLineInputPoint = null;
        ClearInteractionState(clearClipboard: false);
        RaiseInteractionStateChanged();
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

        if (MovePan(screen))
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

        CadEditor.Execute(new ZoomViewportCommand(screen, factor));
        UpdatePointerWorldStatus(screen);
        RequestRender(
            CadRenderInvalidation.Full,
            drawGripHandles: true,
            updateHandleScene: true);
        return CadCanvasInteractionResult.HandledOnly;
    }

    public CadCanvasInteractionResult CycleSelection(bool backwards)
    {
        if (CadCanvasToolMode != CadCanvasToolMode.Select ||
            !_selectionCycle.Cycle(CadEditor, backwards, CanSelectEntity))
        {
            return CadCanvasInteractionResult.NotHandled;
        }

        RequestRender();
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
        _lastCommandLineInputPoint = null;
        ClearInteractionState(clearClipboard: false);
        EndPan();
        RaiseInteractionStateChanged();
        PublishInteractionActivity("Cancel current interaction");
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

        CadEditor.Execute(new FitViewportCommand(ownerBlockId: BlockId.ModelSpace));
        RequestRender();
    }

    public void ActivateModelSpace()
    {
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
        FitToWindow();
        RaiseInteractionStateChanged();
    }

    public void ActivateLayout(LayoutId layoutId)
    {
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
        FitToWindow();
        RaiseInteractionStateChanged();
    }

    public void ActivateLayoutViewport(LayoutViewportId viewportId)
    {
        if (ActiveLayoutId is not { } layoutId)
            throw new InvalidOperationException("A paper layout must be active before activating its viewport.");

        var viewport = CadEditor.Document.GetLayout(layoutId).GetViewport(viewportId);
        if (!viewport.IsVisible)
            throw new InvalidOperationException("A hidden layout viewport cannot be activated.");

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

    public void CopySelection()
    {
        _paste.Copy(CreateClipboardInteractionService());
        PublishInteractionActivity($"Copy selection ({CadEditor.Selection.EntityIds.Count})");
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
        RequestRender();
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
            RequestRender();
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
            RequestRender();
        }

        _selectionFilterChangedPublisher.Publish(new CadSelectionFilterChangedMessage(this));
    }

    private bool CanSelectEntity(CadEntity entity)
    {
        return entity.OwnerBlockId.Equals(CadEditor.ActiveOwnerBlockId) &&
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
        var oleObject = CadEditor.SpatialIndex.Query(queryBounds)
            .Select(entityId => CadEditor.Document.TryGetEntity(entityId, out var entity) ? entity : null)
            .OfType<CadOleObject>()
            .Where(entity =>
                !entity.IsErased &&
                entity.IsVisible &&
                entity.OwnerBlockId.Equals(CadEditor.ActiveOwnerBlockId) &&
                entity.Bounds.Contains(world))
            .OrderByDescending(entity => CadEditor.Document.DocumentSettings.LayerDrawingPriority.GetPriority(entity.LayerId))
            .ThenByDescending(entity => entity.ZIndex)
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
        if (IsLayoutViewportActive)
        {
            RequestRender(CadRenderInvalidation.Full);
            return;
        }
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
                _gripDrag.CreateActiveHandleItems(CadEditor, CreateHandleSceneBuildOptions(), InteractionZoom),
                CreateHandleSceneBuildOptions(),
                InteractionZoom);
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
                _gripDrag.CreateActiveHandleItems(CadEditor, CreateHandleSceneBuildOptions(), InteractionZoom),
                CreateHandleSceneBuildOptions(),
                InteractionZoom);
            effectiveInvalidation = requestedInvalidation.Union(overlayInvalidation);
        }

        Direct2DImageRenderHost.SetRenderOptions(CreateRenderOptions(drawGripHandles));
        Direct2DImageRenderHost.Render(effectiveInvalidation);
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
        }
        OnPropertyChanged(nameof(IsPanning));
        return true;
    }

    private void EndPan()
    {
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
        OnPropertyChanged(nameof(IsPanning));
        if (hasMoved)
            PublishInteractionActivity("Pan View");
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
        if (!CreateDrawingClickHandler().HandleClick(world))
            return false;

        _lastCommandLineInputPoint = new CadCommandLinePoint(world.X, world.Y);
        RequestRender();
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
            RequestRender();
        }
    }

    private void CommitPaste(CadPointD screen)
    {
        var target = ScreenToWorld(screen, snapToGrid: true);
        var createdIds = _paste.Commit(
            CreateClipboardInteractionService(),
            target,
            PasteTargetLayerId);
        if (!CadEditor.ActiveOwnerBlockId.Equals(BlockId.ModelSpace))
        {
            foreach (var entityId in createdIds)
                CadEditor.Document.MoveEntityToBlock(entityId, CadEditor.ActiveOwnerBlockId);
        }
        if (createdIds.Count > 0)
        {
            CadEditor.Selection.Replace(createdIds.Where(entityId =>
                CadEditor.Document.TryGetEntity(entityId, out var entity) &&
                entity is not null &&
                CanSelectEntity(entity)));
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
        PublishInteractionActivity("Begin paste preview");
        RequestRender();
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
            ActiveOwnerBlockId = CadEditor.ActiveOwnerBlockId,
            ActiveLayoutId = ActiveLayoutId,
            ActiveLayoutViewportId = ActiveLayoutViewportId,
            DrawGrid = ActiveLayoutId is null,
            DrawOrigin = ActiveLayoutId is null,
            DrawGripHandles = drawGripHandles,
            IsAntialiasingEnabled = UserSettings.Rendering.IsAntialiasingEnabled,
            IsTextAntialiasingEnabled = UserSettings.Rendering.IsTextAntialiasingEnabled,
            HiddenEntityIds = _gripDrag.ResolveHiddenEntityIds(CadEditor).ToHashSet()
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
            AddLayoutViewportCreationPreview(items, mousePoint);
        }

        return items;
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
            CreateEntityPreviewStyle);
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
                WorldToScreen,
                ScreenToSnappedWorld,
                screen))
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
        return new CadSelectionInteractionService(
            CadEditor,
            ScreenToWorld,
            InteractionZoom,
            CreatePreviewStyleService(),
            CanSelectEntity);
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
            DrawingDefaults,
            CreateDrawingStyleResolver(),
            CreatePreviewStyleService(),
            CreateMeasurementBuilder(),
            CreateMultiPointDrawingPreviewBuilder(),
            CreateTextMeasurementService(),
            CadEditor.Document,
            InteractionViewport,
            ResolveContinueArcBase,
            CreateDrawingTextRequest);
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

            var viewport = new CadViewport();
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
        EnsureActiveLayoutViewportStillExists();
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
                !entity.IsErased)
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
        var entityIds = CadEditor.Document.Entities.Values
            .Where(entity => !entity.IsErased && CanSelectEntity(entity))
            .Select(entity => entity.Id)
            .ToArray();
        SelectEntities(entityIds);
        return entityIds.Length;
    }

    int ICadCommandLineContext.DeleteSelection()
    {
        var count = CadEditor.Selection.EntityIds.Count;
        return DeleteSelection().Handled ? count : 0;
    }

    bool ICadCommandLineContext.CopySelection()
    {
        if (CadEditor.Selection.EntityIds.Count == 0)
            return false;

        CopySelection();
        return true;
    }

    bool ICadCommandLineContext.BeginPaste() => BeginClipboardPastePreview().Handled;

    bool ICadCommandLineContext.SubmitDrawingPoint(CadCommandLinePoint point) =>
        HandleDrawingWorldPoint(SnapWorld(new CadPointD(point.X, point.Y)));

    bool ICadCommandLineContext.CompleteCurrentDrawing() => CompleteCurrentDrawing().Handled;

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
        if (ActiveLayoutId is not null)
            return CadRenderInvalidation.Full;
        return CreateRenderInvalidationCalculator().CreateDocumentInvalidation(changes);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DetachRenderResources();
        _oleHostService.EndEditSessions(_oleEditSessionId);
        _oleHostService.ReleaseRenderSessions(_oleEditSessionId);
        _openOleEditEntityIds.Clear();
        _oleObjectUpdatedSubscription.Dispose();
        CadEditor.EditorStateChanged -= OnEditorStateChanged;
        CadEditor.CommandActivity -= OnCommandActivity;
        DrawingDefaults.DefaultsChanged -= OnDrawingDefaultsChanged;
        Direct2DImageRenderHost.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CadDocumentViewModel));
    }

}
