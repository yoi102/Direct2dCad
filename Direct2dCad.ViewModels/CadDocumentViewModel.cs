using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.CommandLine;
using Direct2dCad.Lang;
using Direct2dCad.Commands;
using Direct2dCad.Commands.Clipboard;
using Direct2dCad.Db;
using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Db.Data.Styles;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Editor;
using Direct2dCad.Editor.Commands;
using Direct2dCad.HitTesting;
using Direct2dCad.Lang.Strings;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Handles;
using Direct2dCad.Rendering.Transient;
using Direct2dCad.ViewModels.Drawing;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.ViewModels.Services.Drawing;
using Direct2dCad.ViewModels.Services.Events;
using Direct2dCad.ViewModels.Services.Interactions;
using Direct2dCad.ViewModels.Services.Platform;
using Direct2dCad.ViewModels.Services.Platform.Notifications;
using Direct2dCad.ViewModels.Services.Platform.Printing;
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
    private readonly Func<BlockId, CadRectD, int> _entityBoundsCount;
    private readonly CadOleSessionController _oleSessions;
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
    private readonly ISnackbarService _snackbarService;
    private readonly CadSelectionDragController _selectionDrag = new();
    // The button that started the current pan, so releasing another button (for example the left
    // button of an open selection box) does not end it.
    private CadCanvasPointerButton _panButton;
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
    private CadPointD? _previewMouseWorld;
    private CadCommandLinePoint? _lastCommandLineInputPoint;
    private readonly CadLayoutViewportPanController _layoutPan = new();
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
    public bool IsDisposed => _disposed;
    public DateTimeOffset LastInteractionAtUtc { get; private set; }=DateTimeOffset.UtcNow;

    [ObservableProperty]
    public partial CadEditor CadEditor { get; private set; } = new(CadDocument.Create("Untitled"));

    public ICadRenderSession RenderSession { get; }

    [ObservableProperty]
    public partial double CurrentPointerWorldX { get; private set; }

    [ObservableProperty]
    public partial double CurrentPointerWorldY { get; private set; }

    public string CurrentPointerWorldXDisplay => FormatPointerCoordinate(CurrentPointerWorldX);
    public string CurrentPointerWorldYDisplay => FormatPointerCoordinate(CurrentPointerWorldY);
    public CadUnit DocumentUnit => CadEditor.Document.DocumentSettings.Unit;
    public int DocumentLengthPrecision => CadEditor.Document.DocumentSettings.LengthPrecision;
    public int DocumentAnglePrecision => CadEditor.Document.DocumentSettings.AnglePrecision;

    partial void OnCurrentPointerWorldXChanged(double value) =>
        OnPropertyChanged(nameof(CurrentPointerWorldXDisplay));

    partial void OnCurrentPointerWorldYChanged(double value) =>
        OnPropertyChanged(nameof(CurrentPointerWorldYDisplay));

    public void NotifyDocumentUnitChanged()
    {
        OnPropertyChanged(nameof(DocumentUnit));
        OnPropertyChanged(nameof(DocumentLengthPrecision));
        OnPropertyChanged(nameof(DocumentAnglePrecision));
        OnPropertyChanged(nameof(CurrentPointerWorldXDisplay));
        OnPropertyChanged(nameof(CurrentPointerWorldYDisplay));
        RaiseInteractionStateChanged();
        PublishViewSettingsChanged();
    }

    public bool SetDocumentUnit(CadUnit unit)
    {
        return SetDocumentSettings(unit: unit);
    }

    public bool SetDocumentPrecision(int? length = null, int? angle = null)
    {
        var settings = CadEditor.Document.DocumentSettings;
        return SetDocumentSettings(
            lengthPrecision: length ?? settings.LengthPrecision,
            anglePrecision: angle ?? settings.AnglePrecision);
    }

    public bool SetDocumentSettings(
        CadUnit? unit = null,
        int? lengthPrecision = null,
        int? anglePrecision = null)
    {
        var settings = CadEditor.Document.DocumentSettings;
        var targetUnit = unit ?? settings.Unit;
        var targetLengthPrecision = lengthPrecision ?? settings.LengthPrecision;
        var targetAnglePrecision = anglePrecision ?? settings.AnglePrecision;
        if (settings.Unit == targetUnit &&
            settings.LengthPrecision == targetLengthPrecision &&
            settings.AnglePrecision == targetAnglePrecision)
        {
            return false;
        }

        CadEditor.SetDocumentSettings(
            targetUnit,
            targetLengthPrecision,
            targetAnglePrecision);
        NotifyDocumentUnitChanged();
        RequestRender();
        return true;
    }

    private string FormatPointerCoordinate(double value)
    {
        var displayValue = CadUnitConversion.FromMillimeters(
            value,
            CadEditor.Document.DocumentSettings.Unit);
        var precision = Math.Clamp(CadEditor.Document.DocumentSettings.LengthPrecision, 0, 12);
        return displayValue.ToString($"F{precision}", CultureInfo.CurrentCulture);
    }

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

    public bool IsPanning => _pan.IsPanning || _layoutPan.IsPanning;
    public CadCanvasCursorKind CanvasCursor => IsPanning || _gripDrag.IsActive
        ? CadCanvasCursorKind.Hand
        : CadCanvasCursorKind.Arrow;
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
        ISnackbarService snackbarService,
        ICadRenderSessionFactory renderSessionFactory)
    {
        ArgumentNullException.ThrowIfNull(imageImportService);
        ArgumentNullException.ThrowIfNull(clipboardTextService);
        ArgumentNullException.ThrowIfNull(snackbarService);
        ArgumentNullException.ThrowIfNull(oleHostService);
        ArgumentNullException.ThrowIfNull(oleObjectUpdatedSubscriber);
        ArgumentNullException.ThrowIfNull(clipboardStore);
        RenderSession = (renderSessionFactory ?? throw new ArgumentNullException(nameof(renderSessionFactory))).Create();
        CadOleSessionController? initializingOleSessions = null;
        try
        {
            _interactionStateChangedPublisher = interactionStateChangedPublisher;
            _viewSettingsChangedPublisher = viewSettingsChangedPublisher;
            _selectionFilterChangedPublisher = selectionFilterChangedPublisher;
            _commandActivityPublisher = commandActivityPublisher;
            _interactionActivityPublisher = interactionActivityPublisher;
            _imageImportService = imageImportService ?? throw new ArgumentNullException(nameof(imageImportService));
            _clipboardTextService = clipboardTextService ?? throw new ArgumentNullException(nameof(clipboardTextService));
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
            _entityBoundsCount = (owner, bounds) => CadEditor.SpatialIndex.CountIntersecting(owner, bounds);
            _oleSessions = initializingOleSessions = new CadOleSessionController(CadEditor, oleHostService, oleObjectUpdatedSubscriber,
                entityId =>
                {
                    RenderSession.InvalidateOleBitmap(entityId);
                    RequestRender();
                });
            RenderSession.SetOleDrawCallback(_oleSessions.Draw);
            RenderSession.SetOleReleaseCallback(_oleSessions.Release);
            _paste = new CadPasteInteractionController(clipboardStore);
            DrawingDefaults.DefaultsChanged += OnDrawingDefaultsChanged;
            CadEditor.EditorStateChanged += OnEditorStateChanged;
            CadEditor.CommandActivity += OnCommandActivity;
        }
        catch (Exception initializationError)
        {
            List<Exception>? cleanupErrors = null;
            try { initializingOleSessions?.Dispose(); }
            catch (Exception exception) { (cleanupErrors ??= []).Add(exception); }
            try { RenderSession.Dispose(); }
            catch (Exception exception) { (cleanupErrors ??= []).Add(exception); }
            if (cleanupErrors is not null)
                throw new AggregateException("Render session initialization and cleanup failed.",
                    new[] { initializationError }.Concat(cleanupErrors));
            throw;
        }
    }

    internal void ReplaceEditor(CadEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _layoutPan.Cancel();
        _pan.End();
        OnPropertyChanged(nameof(IsPanning));
        var wasAttached = _renderResources.IsAttached;
        if (wasAttached)
            DetachRenderResources();

        _oleSessions.ReplaceEditor(editor);
        CadEditor.EditorStateChanged -= OnEditorStateChanged;
        CadEditor.CommandActivity -= OnCommandActivity;
        CadEditor = editor ?? throw new ArgumentNullException(nameof(editor));
        CadEditor.DocumentCommands.Settings.MaximumUndoBytes=UserSettings.General.HistoryBudgetMegabytes*1024L*1024;
        CadEditor.EditorCommands.Settings.MaximumUndoBytes=UserSettings.General.HistoryBudgetMegabytes*1024L*1024;
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
            CreateDocumentInvalidationCalculator());
        _deferredDocumentInvalidation = CadRenderInvalidation.Empty;
        _renderResources.Attach(
            CadEditor,
            RenderSession,
            _overlayScenes.TransientScene,
            _overlayScenes.HandleScene,
            OnDocumentChanged);
    }

    public void DetachRenderResources()
    {
        _renderResources.Detach(CadEditor, RenderSession, OnDocumentChanged);
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
        RenderSession.SetSize(Math.Max(1, width), Math.Max(1, height));
    }

    public void ApplyUserSettings(CadUserSettings? settings)
    {
        CadEditor.DocumentCommands.Settings.MaximumUndoBytes=(settings?.General.HistoryBudgetMegabytes??256)*1024L*1024;
        CadEditor.EditorCommands.Settings.MaximumUndoBytes=(settings?.General.HistoryBudgetMegabytes??256)*1024L*1024;
        UserSettings = settings ?? CadUserSettings.CreateDefault();
        UserSettings.Normalize();
        RenderSession.SetGraphicsDeviceMode(
            UserSettings.Rendering.GraphicsDeviceMode);
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
        _snackbarService.Enqueue(string.Format(format, layer.Name), level: CadMessageLevel.Warning);
        return false;
    }

    private void OnDrawingDefaultsChanged(object? sender, EventArgs e)
    {
        RaiseInteractionStateChanged();
        RequestOverlayRender();
    }

    public CadCanvasInteractionResult SetToolMode(CadCanvasToolMode toolMode)
    {
        if (CadEditor.Document.IsReadOnly && toolMode != CadCanvasToolMode.Select)
        {
            _snackbarService.Enqueue(Direct2dCad.Lang.CadUiText.Get(Direct2dCad.Lang.LangKeys.CompatibilityReadOnly));
            return CadCanvasInteractionResult.NotHandled;
        }
        ClearBooleanInteraction();
        var modeChanged = CadCanvasToolMode != toolMode;
        var suppressPrompt = _suppressStepPrompt;
        _suppressStepPrompt = true;
        try
        {
            _dynamicInputStep = null;
            BeginDimension(toolMode);
            BeginEditInteraction(toolMode);
            if (modeChanged && toolMode != CadCanvasToolMode.Select)
                CadEditor.Selection.Clear();
            if (toolMode != CadCanvasToolMode.InsertBlock)
                _insertBlockDefinitionId = null;
            if (toolMode != CadCanvasToolMode.LayoutViewport)
                _layoutViewportCreation.Clear();
            CadCanvasToolMode = toolMode;
            _objectSnap.Clear();
            StepInputError = "";
            if (modeChanged)
                RefreshDrawingEntityName();
            _lastCommandLineInputPoint = null;
            ClearInteractionState(clearClipboard: false);
            if (IsCurveEditTool) CadEditor.Selection.Replace(_editTargets);
            RaiseInteractionStateChanged(clearBlockDefinitionSelection: modeChanged);
            if (modeChanged)
                PublishInteractionActivity($"Tool mode: {toolMode}");
            return new CadCanvasInteractionResult(
                true,
                ReleaseMouseCapture: true,
                Cursor: CanvasCursor);
        }
        finally
        {
            _suppressStepPrompt = suppressPrompt;
            PublishCurrentStepPrompt();
        }
    }

    public CadCanvasInteractionResult PointerDown(
        CadPointD screen,
        CadCanvasPointerButton button,
        bool forcePan,
        CadCanvasInputModifiers modifiers = CadCanvasInputModifiers.None)
    {
        LastInteractionAtUtc=DateTimeOffset.UtcNow;
        _currentMousePoint = screen;
        UpdatePointerWorldStatus(screen);
        _selectionCycle.Clear();

        if (forcePan || button is CadCanvasPointerButton.Middle or CadCanvasPointerButton.Right)
        {
            if (!BeginPan(screen))
                return CadCanvasInteractionResult.HandledOnly;
            _panButton = button;
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
                _screenToWorld(screen),
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
        LastInteractionAtUtc=DateTimeOffset.UtcNow;
        _currentMousePoint = screen;
        var requiresFullRender = false;

        if (_pan.IsPanning &&
            !RenderSession.IsViewportInteractionActive)
        {
            RenderSession.BeginViewportInteraction();
        }

        if (MovePan(screen))
            requiresFullRender = true;

        UpdatePointerWorldStatus(screen);

        if (_gripDrag.IsActive)
        {
            UpdateGripDynamicInputPointer(_screenToSnappedWorld(screen));
            if (requiresFullRender)
            {
                if (!RenderPanInteractionPreview())
                    RequestRender(CadRenderInvalidation.Full, updateHandleScene: true);
            }
            else if (!RenderSession.IsViewportInteractionActive)
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
        else if (!RenderSession.IsViewportInteractionActive)
        {
            RequestOverlayRender();
        }
        return CadCanvasInteractionResult.HandledOnly;
    }

    public void PointerLeave()
    {
        if (_currentMousePoint is null)
            return;

        _previewMouseWorld = IsCurveEditTool ? ScreenToWorld(_currentMousePoint.Value) : _screenToSnappedWorld(_currentMousePoint.Value);
        _currentMousePoint = null;
        RequestOverlayRender();
    }

    public CadCanvasInteractionResult PointerUp(CadPointD screen, CadCanvasPointerButton button)
    {
        _currentMousePoint = screen;
        UpdatePointerWorldStatus(screen);

        // Left button released while a right/middle-button pan is still held: finish the box, keep panning.
        if (IsPanning && button != _panButton &&
            CadCanvasToolMode == CadCanvasToolMode.Select &&
            button == CadCanvasPointerButton.Left &&
            _selectionDrag.IsDragging)
        {
            CompleteSelection(screen);
            return CadCanvasInteractionResult.HandledOnly;
        }

        if (IsPanning)
        {
            EndPan();
            // A selection box opened before the pan still needs the left-button release.
            return new CadCanvasInteractionResult(
                true,
                ReleaseMouseCapture: !_selectionDrag.IsDragging,
                Cursor: CanvasCursor);
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
            RenderSession.BeginViewportInteraction();
        _viewportInteractionRequiresHandleSceneUpdate = true;
        CadEditor.Execute(new ZoomViewportCommand(screen, factor));
        UpdatePointerWorldStatus(screen);
        if (!RenderZoomInteractionPreview())
        {
            RenderSession.EndViewportInteraction();
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
        if (!RenderSession.IsViewportInteractionActive)
        {
            _viewportInteractionRequiresHandleSceneUpdate = false;
            return;
        }

        RenderSession.EndViewportInteraction();
        var updateHandleScene = _viewportInteractionRequiresHandleSceneUpdate;
        _viewportInteractionRequiresHandleSceneUpdate = false;
        RequestRender(
            CadRenderInvalidation.Full,
            drawGripHandles: true,
            updateHandleScene: updateHandleScene);
    }

    public void CancelViewportInteractionPreview()
    {
        RenderSession.EndViewportInteraction();
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
        ClearEditInteraction();
        ClearBooleanInteraction();
        _dimensionAnchors.Clear();
        _reassociateDimension = null;
        _objectSnap.Clear();
        CadEditor.Selection.Clear();
        _lastCommandLineInputPoint = null;
        ClearInteractionState(clearClipboard: false);
        EndPan();
        RaiseInteractionStateChanged(clearBlockDefinitionSelection: true);
        PublishInteractionActivity("Cancel current interaction");
        return new CadCanvasInteractionResult(
            true,
            ReleaseMouseCapture: true,
            Cursor: CanvasCursor);
    }

    /// <summary>Cancels only the pointer gesture when capture is interrupted; confirmed drawing input survives.</summary>
    public CadCanvasInteractionResult CancelCapturedPointerGesture()
    {
        if (_disposed)
            return new CadCanvasInteractionResult(true, ReleaseMouseCapture: true, Cursor: CadCanvasCursorKind.Arrow);

        // Layout panning previews a document property. Capture loss must not commit it.
        var cancelledLayoutPan = _layoutPan.IsPanning;
        if (cancelledLayoutPan)
            _layoutPan.Cancel();
        EndPan();
        _selectionDrag.Clear();
        if (_gripDrag.IsActive)
        {
            _gripDrag.Clear();
            _dynamicInputStep = null;
            OnPropertyChanged(nameof(IsGripEditing));
        }
        RaiseInteractionStateChanged();
        if (cancelledLayoutPan)
            RequestRender();
        else
            RequestOverlayRender(updateHandleScene: true);
        return new CadCanvasInteractionResult(true, ReleaseMouseCapture: true, Cursor: CanvasCursor);
    }

    public bool CanUndoCurrentDrawingStep => !IsGripEditing && !IsPastePreviewActive &&
        _drawingState.GetPendingPointCount(CadCanvasToolMode) > 0;

    public bool CanCompletePointSequence => _drawingState.CanComplete(CadCanvasToolMode);

    public CadCanvasInteractionResult UndoCurrentDrawingStep()
    {
        if (!CanUndoCurrentDrawingStep || !_drawingState.UndoLastPoint(CadCanvasToolMode))
        {
            StepInputError = CadUiText.Get("DrawingNoStepToUndo");
            return CadCanvasInteractionResult.NotHandled;
        }

        _lastCommandLineInputPoint = DrawingAnchor is { } anchor
            ? new CadCommandLinePoint(anchor.X, anchor.Y) : null;
        _dynamicInputStep = null;
        _objectSnap.Clear();
        StepInputError = "";
        RaiseInteractionStateChanged();
        RequestOverlayRender();
        PublishInteractionActivity("Undo current drawing point");
        return CadCanvasInteractionResult.HandledOnly;
    }

    public void Undo()
    {
        if (IsBooleanTool) SetToolMode(CadCanvasToolMode.Select);
        EndDimensionPropertyEdit();
        CadEditor.Undo();
        RefreshDimensionContext(force: true);
    }

    public void Redo()
    {
        if (IsBooleanTool) SetToolMode(CadCanvasToolMode.Select);
        EndDimensionPropertyEdit();
        CadEditor.Redo();
        RefreshDimensionContext(force: true);
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

    public CadCanvasInteractionResult HandleDoubleClick(CadPointD screen)
    {
        if (!CanActivateDoubleClickObjectOrSpace)
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
            Cursor: CanvasCursor);
    }

    public CadCanvasInteractionResult ClearSelection()
    {
        if (CadEditor.Selection.EntityIds.Count == 0)
            return CadCanvasInteractionResult.NotHandled;

        CadEditor.Selection.Clear();
        ClearInteractionState(clearClipboard: false, render: false);
        RaiseInteractionStateChanged();
        RequestOverlayRender(updateHandleScene: true);
        return CadCanvasInteractionResult.HandledOnly;
    }

    public CadCanvasInteractionResult OpenOleObjectAt(CadPointD screen)
    {
        if (!CanActivateDoubleClickObjectOrSpace)
            return CadCanvasInteractionResult.NotHandled;

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
            _oleSessions.BeginEdit(oleObject);
        }
        catch
        {
            return CadCanvasInteractionResult.HandledOnly;
        }

        return CadCanvasInteractionResult.HandledOnly;
    }

    public CadCanvasInteractionResult CompleteCurrentDrawing()
    {
        if (IsPastePreviewActive)
        {
            StepInputError = CadUiText.Get("PastePlacementPrompt");
            NotifyDrawingUx();
            return CadCanvasInteractionResult.HandledOnly;
        }
        if (_gripDrag.IsActive)
        {
            if(!SubmitDynamicInput()) { StepInputError=DynamicInputError; return CadCanvasInteractionResult.NotHandled; }
            StepInputError="";
            return new CadCanvasInteractionResult(true, ReleaseMouseCapture: !_gripDrag.IsActive, Cursor: CanvasCursor);
        }
        if (IsDimensionTool)
        {
            if (_dimensionAnchors.Count > 0 || _reassociateDimension is not null)
            {
                StepInputError = DimensionPrompt;
                NotifyDrawingUx();
                return CadCanvasInteractionResult.HandledOnly;
            }
            SetToolMode(CadCanvasToolMode.Select);
            return CadCanvasInteractionResult.HandledOnly;
        }
        if (CompleteBooleanInteraction()) return CadCanvasInteractionResult.HandledOnly;
        if (CompleteEditInteraction()) return CadCanvasInteractionResult.HandledOnly;
        if (CadCanvasToolMode == CadCanvasToolMode.LayoutViewport)
        {
            CompleteLayoutViewportCreation();
            return CadCanvasInteractionResult.HandledOnly;
        }

        StepInputError = "";
        if (CreateDrawingClickHandler().CompleteCurrentDrawing())
        {
            StepInputError = "";
            NotifyDrawingUx();
            RequestOverlayRender();
            return CadCanvasInteractionResult.HandledOnly;
        }

        var minimum = CadDrawingSessionState.GetMinimumCompletionPointCount(CadCanvasToolMode);
        var missing = minimum - _drawingState.GetPendingPointCount(CadCanvasToolMode);
        if (missing > 0)
            StepInputError = string.Format(System.Globalization.CultureInfo.CurrentCulture,
                CadUiText.Get("DrawingMorePointsRequired"), missing);
        else if (string.IsNullOrEmpty(StepInputError))
            StepInputError = CadUiText.Get("DrawingCannotComplete");
        NotifyDrawingUx();
        return CadCanvasInteractionResult.NotHandled;
    }

    private bool BeginPan(CadPointD screen)
    {
        if (TryGetActiveLayoutViewport(out var layout, out var viewport))
        {
            if (!_layoutPan.Begin(CadEditor, layout.Id, viewport, screen,
                    _layoutViewportCreation.IsAdjustingView ? _layoutViewportCreation.BatchId : null))
                return false;
        }
        else
        {
            _pan.Begin(screen);
            RenderSession.BeginViewportInteraction();
            _viewportInteractionRequiresHandleSceneUpdate = false;
        }
        OnPropertyChanged(nameof(IsPanning));
        return true;
    }

    private void EndPan()
    {
        var hadViewportPreview = RenderSession.IsViewportInteractionActive;
        var hasMoved = _pan.End();
        hasMoved |= _layoutPan.End();
        RenderSession.EndViewportInteraction();
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
        if (!RenderSession.RenderViewportInteractionPreview())
            return false;

        RenderFrameTimeMilliseconds =
            RenderSession.AverageFrameRenderTimeMilliseconds;
        var framesPerSecond = RenderSession.FramesPerSecond;
        if (!RenderFramesPerSecond.Equals(framesPerSecond))
            RenderFramesPerSecond = framesPerSecond;
        return true;
    }

    private bool MovePan(CadPointD screen)
    {
        if (!_layoutPan.IsPanning)
            return _pan.Move(CadEditor, screen);

        var moved = _layoutPan.Move(screen);
        if (!_layoutPan.IsPanning)
        {
            OnPropertyChanged(nameof(IsPanning));
            return true; // Cancellation restored the initial view; repaint that rollback.
        }
        return moved;
    }

    private void HandleDrawingClick(CadPointD screen)
    {
        // Entity picking uses the raw pointer, as does the edit preview. Only
        // geometric point steps opt into snapping inside the edit workflow.
        var world = ScreenToWorld(screen, snapToGrid: !IsCurveEditTool && !IsBooleanTool);
        if (HasDynamicInput && HasLockedDynamicInput && !IsCurveEditTool)
        {
            UpdateDynamicInputPointer(world);
            SubmitDynamicInput();
            return;
        }
        HandleDrawingWorldPoint(world,explicitInput:false);
    }

    private bool HandleDrawingWorldPoint(CadPointD world,bool explicitInput=true)
    {
        LastInteractionAtUtc=DateTimeOffset.UtcNow;
        StepInputError = "";
        if (IsPastePreviewActive)
        {
            if (!CommitPasteWorldPoint(world)) return false;
            _lastCommandLineInputPoint = new(world.X, world.Y);
            return true;
        }
        if (_layoutViewportCreation.IsActive)
        {
            if (!HandleLayoutViewportCreationPoint(world, explicitInput)) return false;
            _lastCommandLineInputPoint = new(world.X, world.Y);
            return true;
        }
        if (CadCanvasToolMode == CadCanvasToolMode.Select)
        {
            StepInputError = "Start a drawing command or paste preview before entering a point.";
            return false;
        }
        if (IsBooleanTool) return HandleBooleanClick(world);
        if (IsDimensionTool)
        {
            if (!EnsureLayerAcceptsEntities(DrawingLayerId) || !HandleDimensionPoint(world, explicitInput)) return false;
            _lastCommandLineInputPoint = new CadCommandLinePoint(world.X, world.Y);
            ClearDynamicInputLocks();
            return true;
        }
        if (IsCurveEditTool)
        {
            if (!HandleEditClick(world,explicitInput)) return false;
            _lastCommandLineInputPoint=new CadCommandLinePoint(world.X,world.Y);
            return true;
        }
        if (!EnsureLayerAcceptsEntities(DrawingLayerId))
            return false;

        if (!CreateDrawingClickHandler().HandleClick(world))
        {
            StepInputError = CadUiText.Get("InvalidDrawingGeometry");
            NotifyDrawingUx();
            return false;
        }

        _lastCommandLineInputPoint = new CadCommandLinePoint(world.X, world.Y);
        NotifyDrawingUx();
        ClearDynamicInputLocks();
        RequestOverlayRender();
        return true;
    }

    private void CompleteSelection(CadPointD endScreen)
    {
        if (_selectionDrag.Complete(
                CreateSelectionInteractionService(),
                WorldToScreen,
                endScreen,
                out var cycleSeed))
        {
            _selectionCycle.Begin(cycleSeed);
            RequestOverlayRender(updateHandleScene: true);
        }
    }

    private void ClearInteractionState(bool clearClipboard, bool render = true)
    {
        if (IsPanning)
            EndPan();
        _drawingState.Clear();
        _selectionDrag.Clear();
        _selectionCycle.Clear();
        _gripDrag.Clear();
        _previewMouseWorld = null;
        OnPropertyChanged(nameof(IsGripEditing));
        _paste.Clear(clearClipboard);
        OnPropertyChanged(nameof(IsPastePreviewActive));
        OnPropertyChanged(nameof(ActivePasteSnapshot));

        _overlayScenes.ClearTransientScene();

        if (render)
        {
            RequestOverlayRender(updateHandleScene: true);
        }
    }

    public CadPrintRequest CreatePrintRequest(string documentName)
    {
        var document = CadEditor.Document;
        if (ActiveLayoutId is not { } layoutId ||
            !document.TryGetLayout(layoutId, out var layout) ||
            layout is null)
        {
            var bounds=document.GetEntitiesInBlock(BlockId.ModelSpace).Where(e=>!e.IsErased && e.IsVisible)
                .Aggregate(CadRectD.Empty,(b,e)=>b.Union(e.Bounds));
            if(bounds.IsEmpty) bounds=CadRectD.FromXYWH(0,0,297,210);
            if(bounds.Width<1 || bounds.Height<1) bounds=bounds.Inflate(1,1);
            var a=CadEditor.Viewport.ScreenToWorld(new(0,0));
            var b=CadEditor.Viewport.ScreenToWorld(new(CadEditor.Viewport.ViewWidth,CadEditor.Viewport.ViewHeight));
            return new(documentName,document,bounds,document.Layouts.Keys.First(),_oleSessions.Draw)
            {IsModelSpace=true,CurrentViewBounds=CadRectD.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y))};
        }

        return new CadPrintRequest(
            documentName,
            document,
            layout.PaperBounds,
            layout.Id,
            _oleSessions.Draw);
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

    internal IReadOnlyList<CadTransientItem> CreateTransientItems()
    {
        _transientItemBuffer.Clear();
        var items = _transientItemBuffer;

        if (_currentMousePoint is { } || _previewMouseWorld is not null &&
            (HasActiveDrawingTool || _gripDrag.IsActive || _paste.IsPreviewActive) ||
            HasDynamicInput && (HasLockedDynamicInput || _isDynamicInputInteracting))
        {
            var fallbackWorld = _previewMouseWorld ?? _dynamicInputPointer;
            var mousePoint = _currentMousePoint ?? WorldToScreen(fallbackWorld);
            var rawMouseWorld = _currentMousePoint is not null ? ScreenToWorld(mousePoint) : fallbackWorld;
            var snappedMouseWorld = _currentMousePoint is not null ? SnapWorld(rawMouseWorld) : rawMouseWorld;
            if (_gripDrag.IsActive) UpdateGripDynamicInputPointer(snappedMouseWorld);
            else UpdateDynamicInputPointer(IsCurveEditTool ? rawMouseWorld : snappedMouseWorld);
            var inputWorld = ResolveDynamicInputPreview(IsCurveEditTool ? rawMouseWorld : snappedMouseWorld);
            AddPastePreview(items, snappedMouseWorld);
            AddSelectionWindowPreview(items, mousePoint);
            AddGripDragPreview(items);
            AddBlockInsertionPreview(items, snappedMouseWorld);
            AddDrawingPreview(items, inputWorld);
            AddDimensionPreview(items, inputWorld);
            if (_currentMousePoint is not null) AddSnapMarker(items, snappedMouseWorld);
            AddLayoutViewportCreationPreview(items, mousePoint);
        }
        AddEditPreview(items, _dynamicInputPointer);
        AddBooleanPreview(items);
        AddDimensionAnchorMarkers(items);
        return items;
    }

    private CadRenderInvalidationCalculator CreateRenderInvalidationCalculator()
    {
        // Transient markers and handles are rendered through the active model
        // viewport while editing inside a layout viewport. Using the paper
        // viewport here produces a dirty rectangle at the wrong screen position
        // (and sometimes the wrong size), which can leave the previous marker
        // behind during fast pointer movement.
        var viewport = IsLayoutViewportActive
            ? InteractionViewport
            : CadEditor.Viewport;
        return new CadRenderInvalidationCalculator(
            CadEditor.Document,
            viewport,
            RenderSession.TargetWidth,
            RenderSession.TargetHeight,
            _createEntityPreviewStyle);
    }

    private void AddSelectionWindowPreview(List<CadTransientItem> items, CadPointD mousePoint)
    {
        if (!_selectionDrag.IsDragging)
            return;

        _selectionDrag.AddPreview(CreateSelectionInteractionService(), WorldToScreen, items, mousePoint);
    }

    private CadSelectionInteractionService CreateSelectionInteractionService()
    {
        return new CadSelectionInteractionService(
            CadEditor,
            _screenToWorld,
            CreateHitTestOptions(),
            CreatePreviewStyleService(),
            _canSelectEntity);
    }

    private CadHitTestOptions CreateHitTestOptions()
    {
        var worldScale = TryGetActiveLayoutViewport(out _, out var layoutViewport)
            ? 1.0 / Math.Max(layoutViewport.Scale, double.Epsilon)
            : 1.0;
        return new CadHitTestOptions(InteractionZoom)
        {
            KeepStrokeWidthScreenConstant = ActiveLayoutId is null,
            EntityLineWeightWorldScale = worldScale
        };
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
        var worldScale = TryGetActiveLayoutViewport(out _, out var layoutViewport)
            ? 1.0 / Math.Max(layoutViewport.Scale, double.Epsilon)
            : 1.0;
        return new CadPreviewStyleService(
            CadEditor.Document,
            UserSettings,
            keepEntityStrokeWidthScreenConstant: ActiveLayoutId is null,
            entityLineWeightWorldScale: worldScale);
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
            _dynamicInputResolvedState ?? _drawingState,
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
        return new CadTransientMeasurementBuilder(CadEditor.Document, InteractionViewport,
            showLabels: !HasDynamicInput);
    }

    private CadMultiPointDrawingPreviewBuilder CreateMultiPointDrawingPreviewBuilder()
    {
        return new CadMultiPointDrawingPreviewBuilder(
            CadEditor.Document,
            InteractionViewport,
            CreateDrawingStyleResolver(),
            CreateMeasurementBuilder());
    }

    private void AddSnapMarker(List<CadTransientItem> items, CadPointD snappedWorld)
    {
        if(_objectSnap.Current is { } candidate && CadCanvasToolMode!=CadCanvasToolMode.Select)
        {
            var half=6/Math.Max(InteractionZoom,1e-9);var point=candidate.Point;
            var style=CadTransientStyle.Construction with {StrokeColor=CadEditor.Document.ViewSettings.Grid.SnapMarkerColor,LinePattern=CadTransientLinePattern.Solid};
            if(candidate.Kind==CadObjectSnapModes.Center) items.Add(new CadTransientCircle(point,half,style));
            else if(candidate.Kind==CadObjectSnapModes.Midpoint) items.Add(new CadTransientPolyline([new(point.X-half,point.Y-half),new(point.X+half,point.Y-half),new(point.X,point.Y+half)],true,style));
            else if(candidate.Kind==CadObjectSnapModes.Endpoint) items.Add(new CadTransientRectangle(CadRectD.FromCenter(point,half*2,half*2),style));
            else {items.Add(new CadTransientLine(new(point.X-half,point.Y-half),new(point.X+half,point.Y+half),style));items.Add(new CadTransientLine(new(point.X-half,point.Y+half),new(point.X+half,point.Y-half),style));}
            return;
        }
        CreateSnapInteractionService().AddSnapMarker(items, snappedWorld);
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
        UpdateDynamicInputPointer(IsCurveEditTool ? ScreenToWorld(screen, snapToGrid: false) : world);
        CurrentPointerWorldX = world.X;
        CurrentPointerWorldY = world.Y;
    }

    private CadPointD SnapWorld(CadPointD world)
    {
        if (CadCanvasToolMode == CadCanvasToolMode.Select && !_paste.IsPreviewActive)
            return CreateSnapInteractionService().SnapWorld(world);
        var point = _objectSnap.Resolve(CadEditor.Document, CadEditor.ActiveOwnerBlockId,
            InteractionViewport, _entityBoundsQuery, world, DrawingAnchor);
        OnPropertyChanged(nameof(SnapCandidateDisplay));
        OnPropertyChanged(nameof(HasSnapCandidates));
        return point;
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
            RenderSession,
            InteractionViewport);
    }

    private void OnDocumentChanged(object? sender, CadDocumentChangeSet e)
    {
        if (e.IsDerivedGeometry)
        {
            _overlayScenes.ApplyDocumentChanges(e, CadEditor.Selection.EntityIds);
            var derivedInvalidation = CreateDocumentInvalidation(e);
            if (_renderResources.IsApplyingTextMeasurementChanges)
                _deferredDocumentInvalidation = _deferredDocumentInvalidation.Union(derivedInvalidation);
            else
                RequestRender(derivedInvalidation);
            return;
        }
        if(e.DocumentChanged) LastInteractionAtUtc=DateTimeOffset.UtcNow;
        if (e.DocumentChanged && IsBooleanTool && !_committingBoolean) SetToolMode(CadCanvasToolMode.Select);
        _overlayScenes.ApplyDocumentChanges(e, CadEditor.Selection.EntityIds);
        var documentInvalidation = CreateDocumentInvalidation(e);
        _paste.InvalidatePreviewTemplate();
        EnsureActiveLayoutViewportStillExists();
        if (e.AffectsDocumentStructure || (e.TableChanges & CadDocumentTableChangeKind.BlockMetadata) != 0)
            OnPropertyChanged(nameof(EditingBlockName));
        if ((e.AffectsDocumentStructure || e.AffectsLayerAccess) &&
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

        if (e.DocumentChanged)
        {
            if (PruneSelectionToFilter(e))
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
        {
            // Document unit/precision are persisted with the document and are
            // restored by document undo/redo. Keep status-bar bindings in sync
            // when the command is undone outside SetDocumentSettings().
            OnPropertyChanged(nameof(DocumentUnit));
            OnPropertyChanged(nameof(DocumentLengthPrecision));
            OnPropertyChanged(nameof(DocumentAnglePrecision));
            OnPropertyChanged(nameof(CurrentPointerWorldXDisplay));
            OnPropertyChanged(nameof(CurrentPointerWorldYDisplay));
            RaiseInteractionStateChanged();
            PublishViewSettingsChanged();
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

    private void RaiseInteractionStateChanged(bool clearBlockDefinitionSelection = false)
    {
        NotifyDrawingUx();
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
        return _documentInvalidation.CreateInvalidation(
            CadEditor.Document,
            changes,
            CreateDocumentInvalidationCalculator());
    }

    private CadRenderInvalidationCalculator CreateDocumentInvalidationCalculator()
    {
        // Document damage is in paper units, not the active model viewport's units.
        var style = new CadPreviewStyleService(CadEditor.Document, UserSettings,
            keepEntityStrokeWidthScreenConstant: ActiveLayoutId is null);
        return new(CadEditor.Document, CadEditor.Viewport,
            RenderSession.TargetWidth, RenderSession.TargetHeight,
            style.CreateEntityPreviewStyle, ActiveLayoutId, CadEditor.ActiveOwnerBlockId);
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

        List<Exception>? cleanupErrors = null;
        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception exception) { (cleanupErrors ??= []).Add(exception); }
        }

        Cleanup(ClearBooleanInteraction);
        Cleanup(_layoutPan.Cancel);
        Cleanup(() => _pan.End());
        Cleanup(DetachRenderResources);
        Cleanup(_oleSessions.Dispose);
        CadEditor.EditorStateChanged -= OnEditorStateChanged;
        CadEditor.CommandActivity -= OnCommandActivity;
        DrawingDefaults.DefaultsChanged -= OnDrawingDefaultsChanged;
        // The session owns a process-budget lease, so an earlier platform/OLE
        // cleanup failure must never leave it registered for the app lifetime.
        Cleanup(RenderSession.Dispose);

        if (cleanupErrors is { Count: 1 })
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupErrors[0]).Throw();
        if (cleanupErrors is not null)
            throw new AggregateException("Document cleanup failed.", cleanupErrors);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CadDocumentViewModel));
    }

}
