using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Direct2dCad.Client.Common.Settings;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.ViewModels;
using Direct2dCad.wpf.Services.Input;

namespace Direct2dCad.wpf.Controls;

public partial class CadCanvas : IDisposable
{
    private const double RenderCacheIdleBuildBudgetMilliseconds = 9.0;
    private const double RenderCacheWaitRetryMilliseconds = 8.0;
    private readonly DispatcherTimer _renderCacheRetryTimer;
    private ICadRenderSession? _renderCacheRetrySession;
    private int _renderCacheScheduleGeneration;
    private CadPointD _pendingPointerScreen;
    private bool _pointerMovePending;
    private bool _pointerRenderScheduled;
    private bool _renderFlushScheduled;
    private bool _viewportPresentationScheduled;
    private bool _renderCacheBuildScheduled;
    private bool _renderCacheBuildDeferred;
    private bool _rightPanPending;
    private bool _rightPanActive;
    private CadPointD _rightPanStart;
    private bool _disposed;
    private Action? _pendingRenderFlush;
    private readonly DispatcherTimer _viewportInteractionCompletionTimer;
    private readonly CadRadialMenuPopup _radialMenu = new();
    private bool _isRadialMenuActive;
    private bool _endingPointerGesture;
    private bool _releasingMouseCapture;
    private Window? _ownerWindow;

    public CadCanvas()
    {
        InitializeComponent();

        _viewportInteractionCompletionTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Render,
            OnViewportInteractionCompletionTimer,
            Dispatcher);
        _viewportInteractionCompletionTimer.Stop();
        _renderCacheRetryTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(RenderCacheWaitRetryMilliseconds),
            DispatcherPriority.Background, OnRenderCacheRetry, Dispatcher);
        _renderCacheRetryTimer.Stop();

        Focusable = true;
        Stretch = System.Windows.Media.Stretch.Fill;

        Loaded += CadCanvas_Loaded;
        Unloaded += CadCanvas_Unloaded;
        SizeChanged += CadCanvas_SizeChanged;
        MouseDown += CadCanvas_MouseDown;
        MouseMove += CadCanvas_MouseMove;
        MouseEnter += (_, _) => UpdateCursor(DocumentViewModel?.CanvasCursor ?? CadCanvasCursorKind.Arrow);
        MouseLeave += CadCanvas_MouseLeave;
        MouseUp += CadCanvas_MouseUp;
        LostMouseCapture += CadCanvas_LostMouseCapture;
        LostKeyboardFocus += CadCanvas_LostKeyboardFocus;
        MouseWheel += CadCanvas_MouseWheel;
        KeyDown += CadCanvas_KeyDown;
        KeyUp += CadCanvas_KeyUp;
    }

    public CadDocumentViewModel? DocumentViewModel
    {
        get => (CadDocumentViewModel?)GetValue(DocumentViewModelProperty);
        set => SetValue(DocumentViewModelProperty, value);
    }

    public static readonly DependencyProperty DocumentViewModelProperty =
        DependencyProperty.Register(
            nameof(DocumentViewModel),
            typeof(CadDocumentViewModel),
            typeof(CadCanvas),
            new PropertyMetadata(null, OnDocumentViewModelChanged));

    private static readonly DependencyPropertyKey CursorBadgePositionPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CursorBadgePosition), typeof(Point), typeof(CadCanvas),
            new PropertyMetadata(default(Point)));
    public static readonly DependencyProperty CursorBadgePositionProperty = CursorBadgePositionPropertyKey.DependencyProperty;
    public Point CursorBadgePosition => (Point)GetValue(CursorBadgePositionProperty);

    private static readonly DependencyPropertyKey IsCursorBadgeVisiblePropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsCursorBadgeVisible), typeof(bool), typeof(CadCanvas),
            new PropertyMetadata(false));
    public static readonly DependencyProperty IsCursorBadgeVisibleProperty = IsCursorBadgeVisiblePropertyKey.DependencyProperty;
    public bool IsCursorBadgeVisible => (bool)GetValue(IsCursorBadgeVisibleProperty);
    public bool IsRadialMenuActive => _isRadialMenuActive;

    public ICommand? SaveCommand
    {
        get => (ICommand?)GetValue(SaveCommandProperty);
        set => SetValue(SaveCommandProperty, value);
    }

    public static readonly DependencyProperty SaveCommandProperty =
        DependencyProperty.Register(
            nameof(SaveCommand),
            typeof(ICommand),
            typeof(CadCanvas),
            new PropertyMetadata(null));

    public ICommand? RadialMenuActionCommand
    {
        get => (ICommand?)GetValue(RadialMenuActionCommandProperty);
        set => SetValue(RadialMenuActionCommandProperty, value);
    }

    public static readonly DependencyProperty RadialMenuActionCommandProperty =
        DependencyProperty.Register(
            nameof(RadialMenuActionCommand),
            typeof(ICommand),
            typeof(CadCanvas),
            new PropertyMetadata(null));

    public void RefreshView()
    {
        DocumentViewModel?.RequestRender();
    }

    private static void OnDocumentViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CadCanvas canvas || canvas._disposed)
            return;

        canvas.CancelRenderCachePreparation();

        if (e.OldValue is CadDocumentViewModel oldViewModel)
        {
            canvas.EndCapturedPointerGesture(oldViewModel);
            canvas._viewportInteractionCompletionTimer.Stop();
            oldViewModel.CancelViewportInteractionPreview();
            oldViewModel.SetRenderScheduler(null);
            oldViewModel.PropertyChanged -= canvas.OnDocumentViewModelPropertyChanged;
            oldViewModel.RenderSession.RenderCacheBuildRequested -=
                canvas.OnRenderCacheBuildRequested;
            ((Direct2dCad.Rendering.Direct2D.Hosting.Direct2DImageRenderHost)oldViewModel.RenderSession).DetachImageSource();
            oldViewModel.DetachRenderResources();
        }

        if (e.NewValue is CadDocumentViewModel newViewModel)
        {
            newViewModel.SetRenderScheduler(canvas.ScheduleRenderFlush);
            newViewModel.PropertyChanged += canvas.OnDocumentViewModelPropertyChanged;
            newViewModel.RenderSession.RenderCacheBuildRequested +=
                canvas.OnRenderCacheBuildRequested;
            ((Direct2dCad.Rendering.Direct2D.Hosting.Direct2DImageRenderHost)newViewModel.RenderSession).AttachImageSource(canvas.d3d11ImageSource);
            newViewModel.AttachRenderResources();
            canvas.UpdateViewportSize();
            canvas.UpdateRenderSize();
            canvas.UpdateCursor(newViewModel.CanvasCursor);
            newViewModel.RequestRender();
        }
        else
        {
            canvas.UpdateCursor(CadCanvasCursorKind.Arrow);
        }
    }

    private void OnDocumentViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_disposed) return;
        if (DocumentViewModel is null)
            return;

        if (e.PropertyName is nameof(CadDocumentViewModel.CadCanvasToolMode)
            or nameof(CadDocumentViewModel.IsPanning)
            or nameof(CadDocumentViewModel.IsPastePreviewActive))
        {
            UpdateCursor(DocumentViewModel.CanvasCursor);
        }
    }

    private void CadCanvas_Loaded(object sender, RoutedEventArgs e)
    {
        if (_disposed) return;
        SetOwnerWindow(Window.GetWindow(this));
        UpdateCursor(DocumentViewModel?.CanvasCursor ?? CadCanvasCursorKind.Arrow);
        UpdateViewportSize();
        UpdateRenderSize();
        DocumentViewModel?.RequestRender();
    }

    private void CadCanvas_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_disposed) return;
        EndCapturedPointerGesture();
        SetOwnerWindow(null);
        SetValue(IsCursorBadgeVisiblePropertyKey, false);
    }

    private void SetOwnerWindow(Window? window)
    {
        if (ReferenceEquals(_ownerWindow, window))
            return;
        if (_ownerWindow is not null)
            _ownerWindow.Deactivated -= OwnerWindow_Deactivated;
        _ownerWindow = window;
        if (_ownerWindow is not null)
            _ownerWindow.Deactivated += OwnerWindow_Deactivated;
    }

    private void OwnerWindow_Deactivated(object? sender, EventArgs e) => EndCapturedPointerGesture();

    private void CadCanvas_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!IsKeyboardFocusWithin &&
            (IsMouseCaptured || _rightPanPending || _rightPanActive || _isRadialMenuActive))
            EndCapturedPointerGesture();
    }

    private void CadCanvas_LostMouseCapture(object sender, MouseEventArgs e)
    {
        // PointerUp may intentionally release capture while leaving a click-started
        // grip edit active. Only an unexpected loss cancels the gesture.
        if (!_releasingMouseCapture && !IsMouseCaptured)
            EndCapturedPointerGesture();
    }

    private void EndCapturedPointerGesture(CadDocumentViewModel? viewModel = null)
    {
        if (_endingPointerGesture)
            return;
        _endingPointerGesture = true;
        try
        {
            UnschedulePointerMove();
            _pointerMovePending = false;
            ResetRightPanState();
            CloseRadialMenu();
            var document = viewModel ?? DocumentViewModel;
            if (document is not null)
                ApplyInteractionResult(document.CancelCapturedPointerGesture());
            CancelPendingViewportInteraction();
        }
        finally
        {
            _endingPointerGesture = false;
        }
    }

    private void ReleasePointerCapture()
    {
        if (!IsMouseCaptured)
            return;
        _releasingMouseCapture = true;
        try { ReleaseMouseCapture(); }
        finally { _releasingMouseCapture = false; }
    }

    private void CadCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_disposed) return;
        CancelPendingViewportInteraction();
        UpdateViewportSize();
        UpdateRenderSize();
        UpdateCursorBadge();
        DocumentViewModel?.RequestRender();
    }

    private void CadCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();

        if (DocumentViewModel is null)
            return;

        CompletePendingViewportInteraction();
        var screen = ToCadPoint(e.GetPosition(this));
        FlushPendingPointerMove(screen);

        if (e.ChangedButton == MouseButton.Right)
        {
            // Do not start panning on mouse-down. WPF opens the ContextMenu
            // after a normal right click; only promote it to a pan after the
            // pointer crosses the system drag threshold.
            _rightPanPending = true;
            _rightPanActive = false;
            _rightPanStart = screen;
            UpdateCursorBadge();
            e.Handled = false;
            return;
        }

        if (e.ChangedButton == MouseButton.Middle &&
            TryOpenRadialMenu(e.GetPosition(this), Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 &&
            DocumentViewModel.CanActivateDoubleClickObjectOrSpace)
        {
            ApplyInteractionResult(
                DocumentViewModel.HandleDoubleClick(screen),
                e);
            if (e.Handled)
                return;

            ApplyInteractionResult(
                DocumentViewModel.OpenOleObjectAt(screen),
                e);
            if (e.Handled)
                return;
        }

        var result = DocumentViewModel.PointerDown(
            screen,
            ToPointerButton(e.ChangedButton),
            forcePan: false,
            modifiers: ToInputModifiers(Keyboard.Modifiers));

        ApplyInteractionResult(result, e);
    }

    private void CadCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (DocumentViewModel is null)
            return;

        if (_isRadialMenuActive)
        {
            _radialMenu.UpdatePointer(e.GetPosition(this));
            e.Handled = true;
            return;
        }

        _pendingPointerScreen = ToCadPoint(e.GetPosition(this));
        _pointerMovePending = true;
        SchedulePointerMove();
        e.Handled = true;
    }

    private void CadCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        SetValue(IsCursorBadgeVisiblePropertyKey, false);
        if (DocumentViewModel is null || IsMouseCaptured)
            return;

        ResetRightPanState();
        FlushPendingPointerMove();
        DocumentViewModel.PointerLeave();
    }

    private void CadCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DocumentViewModel is null)
            return;

        if (_isRadialMenuActive && e.ChangedButton == MouseButton.Middle)
        {
            var action = _radialMenu.Complete(e.GetPosition(this));
            _isRadialMenuActive = false;
            ReleasePointerCapture();
            if (action is { } selectedAction &&
                selectedAction != CadRadialMenuAction.None &&
                RadialMenuActionCommand?.CanExecute(selectedAction) == true)
            {
                RadialMenuActionCommand.Execute(selectedAction);
            }
            UpdateCursor(DocumentViewModel.CanvasCursor);

            e.Handled = true;
            return;
        }

        var screen = ToCadPoint(e.GetPosition(this));
        FlushPendingPointerMove(screen);

        if (e.ChangedButton == MouseButton.Right)
        {
            if (_rightPanPending && !_rightPanActive)
            {
                // A right click without a drag belongs to ContextMenuService.
                ResetRightPanState();
                return;
            }

            ResetRightPanState();
        }

        var result = DocumentViewModel.PointerUp(
            screen,
            ToPointerButton(e.ChangedButton));

        ApplyInteractionResult(result, e);
        if (result.ReleaseMouseCapture && !IsMouseOver)
            DocumentViewModel.PointerLeave();
    }

    private void CadCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DocumentViewModel is null)
            return;

        var screen = ToCadPoint(e.GetPosition(this));
        FlushPendingPointerMove(screen);
        var result = DocumentViewModel.MouseWheel(screen, e.Delta);
        ApplyInteractionResult(result, e);
        if (result.Handled)
        {
            ScheduleViewportInteractionCompletion();
            ScheduleViewportPresentation();
        }
    }

    private void OnRenderCacheBuildRequested(object? sender, EventArgs e)
    {
        if (_disposed || DocumentViewModel is not { } current ||
            !ReferenceEquals(sender, current.RenderSession)) return;
        _renderCacheRetryTimer.Stop();
        _renderCacheRetrySession = null;
        if (_viewportInteractionCompletionTimer.IsEnabled)
        {
            _renderCacheBuildDeferred = true;
            return;
        }

        if (_renderCacheBuildScheduled || _disposed)
            return;

        _renderCacheBuildScheduled = true;
        var generation = _renderCacheScheduleGeneration;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (generation != _renderCacheScheduleGeneration) return;
            _renderCacheBuildScheduled = false;
            if (_disposed ||
                DocumentViewModel is not { } viewModel ||
                !ReferenceEquals(sender, viewModel.RenderSession))
            {
                return;
            }

            if (_viewportInteractionCompletionTimer.IsEnabled)
            {
                _renderCacheBuildDeferred = true;
                return;
            }

            var wasVisibleViewReady = viewModel.RenderSession.IsInitialViewReady;
            var buildPending = PrepareRenderCacheBatch(
                viewModel.RenderSession.PrepareRenderCacheStep,
                () => viewModel.RenderSession.IsRenderCachePreparationWaiting);

            if (buildPending)
            {
                if ((!wasVisibleViewReady || !viewModel.RenderSession.HasPresentedScene) &&
                    viewModel.RenderSession.IsInitialViewReady)
                    viewModel.RequestRenderCacheRefresh();
                if (viewModel.RenderSession.IsRenderCachePreparationWaiting)
                {
                    _renderCacheRetrySession = viewModel.RenderSession;
                    _renderCacheRetryTimer.Start();
                }
                else
                    OnRenderCacheBuildRequested(sender, EventArgs.Empty);
            }
            else
                viewModel.RequestRenderCacheRefresh();
        });
    }

    internal static bool PrepareRenderCacheBatch(Func<bool> prepareStep,
        Func<bool> isWaitingForWorker, double budgetMilliseconds = RenderCacheIdleBuildBudgetMilliseconds)
    {
        var started = Stopwatch.GetTimestamp();
        bool pending;
        do { pending = prepareStep(); }
        while (pending && !isWaitingForWorker() &&
               Stopwatch.GetElapsedTime(started).TotalMilliseconds < budgetMilliseconds);
        return pending;
    }

    private void OnRenderCacheRetry(object? sender, EventArgs e)
    {
        _renderCacheRetryTimer.Stop();
        var session = _renderCacheRetrySession;
        _renderCacheRetrySession = null;
        if (session is not null) OnRenderCacheBuildRequested(session, EventArgs.Empty);
    }

    private void CancelRenderCachePreparation()
    {
        _renderCacheRetryTimer.Stop();
        _renderCacheRetrySession = null;
        _renderCacheBuildScheduled = false;
        _renderCacheBuildDeferred = false;
        _renderCacheScheduleGeneration++;
    }

    internal void CancelInteraction()
    {
        if (_isRadialMenuActive)
        {
            CloseRadialMenu();
            return;
        }
        if (DocumentViewModel is null) return;
        EndCapturedPointerGesture();
        ApplyInteractionResult(DocumentViewModel.Escape());
    }

    private void CadCanvas_KeyDown(object sender, KeyEventArgs e)
        => HandleCanvasKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers, e);

    internal bool ConfirmInteraction()
    {
        if (_isRadialMenuActive || DocumentViewModel is not { CanEditDocument: true } document) return false;
        if (!document.HasActiveDrawingTool && !document.IsGripEditing && !document.IsPastePreviewActive) return false;
        ApplyInteractionResult(document.CompleteCurrentDrawing());
        // Invalid/incomplete input still owns the confirmation and displays its prompt.
        return true;
    }

    internal void HandleCanvasKey(Key key, ModifierKeys modifiers, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (DocumentViewModel is null)
            return;
        if (key == Key.Escape && modifiers == ModifierKeys.None && e.IsRepeat)
        {
            e.Handled = true;
            return;
        }

        if (_isRadialMenuActive)
        {
            if (key == Key.Escape && modifiers == ModifierKeys.None)
            {
                CloseRadialMenu();
                e.Handled = true;
                return;
            }

            UpdateRadialMenuProfile(modifiers);
            e.Handled = true;
            return;
        }

        var shortcut = CadShortcutCatalog.Find(CadShortcutScope.Canvas, key, modifiers);
        if (shortcut is null) return;

        if (shortcut.Action == CadShortcutAction.Cancel)
        {
            CancelInteraction();
            e.Handled = true;
            return;
        }

        if (shortcut.Action == CadShortcutAction.Confirm)
        {
            if (!CadEnterKeyGuard.ShouldIgnoreRepeat(e, modifiers)) ConfirmInteraction();
            e.Handled = true;
            return;
        }

        if (shortcut.Action == CadShortcutAction.PreviousPoint)
        {
            ApplyInteractionResult(DocumentViewModel.UndoCurrentDrawingStep(), e);
            return;
        }

        if (shortcut.Action == CadShortcutAction.Reselect && DocumentViewModel.IsCurveEditTool)
        {
            DocumentViewModel.ReselectEditObjectsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (shortcut.Action == CadShortcutAction.Delete)
        {
            ApplyInteractionResult(DocumentViewModel.DeleteSelection(), e);
            return;
        }

        if (shortcut.Action is CadShortcutAction.NextSelection or CadShortcutAction.PreviousSelection)
        {
            ApplyInteractionResult(
                DocumentViewModel.CycleSelection(
                    shortcut.Action == CadShortcutAction.PreviousSelection),
                e);
            return;
        }

        if (shortcut.Action == CadShortcutAction.SelectAll)
        {
            ApplyInteractionResult(DocumentViewModel.SelectAllEntities(), e);
            return;
        }

        switch (shortcut.Action)
        {
            case CadShortcutAction.Undo:
                DocumentViewModel.Undo();
                e.Handled = true;
                break;

            case CadShortcutAction.Redo:
                DocumentViewModel.Redo();
                e.Handled = true;
                break;

            case CadShortcutAction.Copy:
                DocumentViewModel.CopySelection();
                e.Handled = true;
                break;

            case CadShortcutAction.Cut:
                if (DocumentViewModel.CopySelection() is not null)
                    DocumentViewModel.DeleteSelection();

                e.Handled = true;
                break;

            case CadShortcutAction.Paste:
                ApplyInteractionResult(DocumentViewModel.BeginClipboardPastePreview(), e);
                break;
        }
    }

    private void CadCanvas_KeyUp(object sender, KeyEventArgs e)
    {
        if (!_isRadialMenuActive)
            return;

        UpdateRadialMenuProfile(Keyboard.Modifiers);
        e.Handled = true;
    }

    private void UpdateViewportSize()
    {
        if (_disposed) return;
        DocumentViewModel?.SetViewportSize(ActualWidth, ActualHeight);
    }

    private bool TryOpenRadialMenu(Point position, ModifierKeys modifiers)
    {
        if (DocumentViewModel?.UserSettings.Interaction.RadialMenu is not { IsEnabled: true } settings)
            return false;

        var gesture = ToRadialMenuGesture(modifiers);
        _radialMenu.Show(this, position, settings.GetActions(gesture));
        _isRadialMenuActive = CaptureMouse();
        UpdateCursor(DocumentViewModel.CanvasCursor);
        if (!_isRadialMenuActive)
            _radialMenu.Close();
        return _isRadialMenuActive;
    }

    private void CloseRadialMenu()
    {
        _radialMenu.Close();
        _isRadialMenuActive = false;
        UpdateCursor(DocumentViewModel?.CanvasCursor ?? CadCanvasCursorKind.Arrow);
        ReleasePointerCapture();
    }

    private void UpdateRadialMenuProfile(ModifierKeys modifiers)
    {
        var settings = DocumentViewModel?.UserSettings.Interaction.RadialMenu;
        if (settings is not { IsEnabled: true })
            return;

        _radialMenu.SetActions(settings.GetActions(ToRadialMenuGesture(modifiers)));
        _radialMenu.UpdatePointer(Mouse.GetPosition(this));
    }

    private void UpdateRenderSize()
    {
        if (_disposed) return;
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        d3d11ImageSource.SetSize(width, height);
        DocumentViewModel?.SetRenderSize(width, height);
    }

    private void ApplyInteractionResult(CadCanvasInteractionResult result, RoutedEventArgs e)
    {
        ApplyInteractionResult(result);

        if (result.Handled)
            e.Handled = true;
    }

    private void ApplyInteractionResult(CadCanvasInteractionResult result)
    {
        if (result.CaptureMouse && !CaptureMouse())
        {
            EndCapturedPointerGesture();
            return;
        }

        if (result.ReleaseMouseCapture)
            ReleasePointerCapture();

        UpdateCursor(result.Cursor ?? DocumentViewModel?.CanvasCursor ?? CadCanvasCursorKind.Arrow);
    }

    private void SchedulePointerMove()
    {
        if (_pointerRenderScheduled)
            return;

        _pointerRenderScheduled = true;
        CompositionTarget.Rendering += OnCompositionTargetRendering;
    }

    private void ScheduleRenderFlush(Action flush)
    {
        if (_disposed)
            return;

        _pendingRenderFlush = flush ?? throw new ArgumentNullException(nameof(flush));
        if (_renderFlushScheduled)
            return;

        _renderFlushScheduled = true;
        CompositionTarget.Rendering += OnRenderFlush;
    }

    private void OnRenderFlush(object? sender, EventArgs e)
    {
        FlushScheduledRender();
    }

    private void FlushScheduledRender()
    {
        UnscheduleRenderFlush();
        var flush = _pendingRenderFlush;
        _pendingRenderFlush = null;
        flush?.Invoke();
    }

    private void UnscheduleRenderFlush()
    {
        if (!_renderFlushScheduled)
            return;

        CompositionTarget.Rendering -= OnRenderFlush;
        _renderFlushScheduled = false;
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        UnschedulePointerMove();
        FlushPendingPointerMove();
        // PointerMove schedules its overlay render while this composition tick is
        // already running. Flush it now so the marker does not trail by one frame.
        FlushScheduledRender();
    }

    private void ScheduleViewportInteractionCompletion()
    {
        _viewportInteractionCompletionTimer.Stop();
        _viewportInteractionCompletionTimer.Start();
    }

    private void OnViewportInteractionCompletionTimer(object? sender, EventArgs e)
    {
        CompletePendingViewportInteraction();
    }

    private void CompletePendingViewportInteraction()
    {
        _viewportInteractionCompletionTimer.Stop();
        var viewModel = DocumentViewModel;
        viewModel?.CompleteViewportInteractionPreview();
        ScheduleViewportPresentation();
        if (_renderCacheBuildDeferred && viewModel is not null)
        {
            _renderCacheBuildDeferred = false;
            OnRenderCacheBuildRequested(
                viewModel.RenderSession,
                EventArgs.Empty);
        }
    }

    private void CancelPendingViewportInteraction()
    {
        _viewportInteractionCompletionTimer.Stop();
        _renderCacheBuildDeferred = false;
        DocumentViewModel?.CancelViewportInteractionPreview();
    }

    private void ScheduleViewportPresentation()
    {
        if (_disposed || _viewportPresentationScheduled)
            return;

        _viewportPresentationScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _viewportPresentationScheduled = false;
            if (_disposed || !IsLoaded)
                return;

            d3d11ImageSource.Invalidate();
            InvalidateVisual();
        });
    }

    private void FlushPendingPointerMove(CadPointD? latestScreen = null)
    {
        UnschedulePointerMove();
        if (!_pointerMovePending)
            return;

        if (latestScreen is { } screen)
            _pendingPointerScreen = screen;

        _pointerMovePending = false;
        if (DocumentViewModel is not { } viewModel)
            return;

        if (_rightPanPending && !_rightPanActive)
        {
            if (HasExceededRightPanThreshold(_pendingPointerScreen))
            {
                var startResult = viewModel.PointerDown(
                    _rightPanStart,
                    CadCanvasPointerButton.Right,
                    forcePan: false,
                    modifiers: ToInputModifiers(Keyboard.Modifiers));
                _rightPanActive = startResult.CaptureMouse;
                ApplyInteractionResult(startResult);
                ApplyInteractionResult(viewModel.PointerMove(_pendingPointerScreen));
            }
            else
            {
                // Keep hover coordinates and overlays current while waiting
                // to decide whether this is a click or a drag.
                ApplyInteractionResult(viewModel.PointerMove(_pendingPointerScreen));
            }

            return;
        }

        ApplyInteractionResult(viewModel.PointerMove(_pendingPointerScreen));
    }

    private bool HasExceededRightPanThreshold(CadPointD current)
    {
        return Math.Abs(current.X - _rightPanStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(current.Y - _rightPanStart.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private void ResetRightPanState()
    {
        _rightPanPending = false;
        _rightPanActive = false;
    }

    private void UnschedulePointerMove()
    {
        if (!_pointerRenderScheduled)
            return;

        CompositionTarget.Rendering -= OnCompositionTargetRendering;
        _pointerRenderScheduled = false;
    }

    private void UpdateCursor(CadCanvasCursorKind cursor)
    {
        Cursor = (_isRadialMenuActive ? CadCanvasCursorKind.Arrow : cursor) switch
        {
            CadCanvasCursorKind.Hand => Cursors.Hand,
            _ => Cursors.Arrow
        };
        UpdateCursorBadge();
    }

    private void UpdateCursorBadge()
    {
        var visible = !_disposed && IsLoaded && IsMouseOver && !_isRadialMenuActive &&
                      Mouse.RightButton == MouseButtonState.Released && Mouse.MiddleButton == MouseButtonState.Released &&
                      DocumentViewModel is { CanvasCursor: CadCanvasCursorKind.Arrow } document &&
                      (document.HasActiveDrawingTool || document.IsPastePreviewActive);
        if (visible)
        {
            const double badgeSize = 28;
            const double offset = 16;
            var pointer = Mouse.GetPosition(this);
            var x = pointer.X + offset;
            var y = pointer.Y + offset;
            if (x + badgeSize > ActualWidth) x = pointer.X - offset - badgeSize;
            if (y + badgeSize > ActualHeight) y = pointer.Y - offset - badgeSize;
            SetValue(CursorBadgePositionPropertyKey, new Point(Math.Max(0, x), Math.Max(0, y)));
        }
        SetValue(IsCursorBadgeVisiblePropertyKey, visible);
    }

    private static CadCanvasPointerButton ToPointerButton(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => CadCanvasPointerButton.Left,
            MouseButton.Middle => CadCanvasPointerButton.Middle,
            MouseButton.Right => CadCanvasPointerButton.Right,
            _ => CadCanvasPointerButton.None
        };
    }

    private static CadCanvasInputModifiers ToInputModifiers(ModifierKeys modifiers)
    {
        var result = CadCanvasInputModifiers.None;
        if ((modifiers & ModifierKeys.Shift) != 0)
            result |= CadCanvasInputModifiers.Shift;
        if ((modifiers & ModifierKeys.Control) != 0)
            result |= CadCanvasInputModifiers.Control;
        if ((modifiers & ModifierKeys.Alt) != 0)
            result |= CadCanvasInputModifiers.Alt;
        return result;
    }

    private static CadRadialMenuGesture ToRadialMenuGesture(ModifierKeys modifiers)
    {
        if ((modifiers & ModifierKeys.Shift) != 0)
            return CadRadialMenuGesture.ShiftMiddle;
        if ((modifiers & ModifierKeys.Control) != 0)
            return CadRadialMenuGesture.ControlMiddle;
        if ((modifiers & ModifierKeys.Alt) != 0)
            return CadRadialMenuGesture.AltMiddle;
        return CadRadialMenuGesture.Middle;
    }

    private static CadPointD ToCadPoint(Point point)
    {
        return new CadPointD(point.X, point.Y);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Loaded -= CadCanvas_Loaded;
        Unloaded -= CadCanvas_Unloaded;
        SizeChanged -= CadCanvas_SizeChanged;
        CancelRenderCachePreparation();
        EndCapturedPointerGesture();
        SetOwnerWindow(null);
        if (DocumentViewModel is { } viewModel)
        {
            viewModel.SetRenderScheduler(null);
            viewModel.PropertyChanged -= OnDocumentViewModelPropertyChanged;
            viewModel.RenderSession.RenderCacheBuildRequested -=
                OnRenderCacheBuildRequested;
            ((Direct2dCad.Rendering.Direct2D.Hosting.Direct2DImageRenderHost)viewModel.RenderSession).DetachImageSource();
            viewModel.DetachRenderResources();
        }
        CancelPendingViewportInteraction();
        CloseRadialMenu();
        UnschedulePointerMove();
        UnscheduleRenderFlush();
        _pendingRenderFlush = null;
        d3d11ImageSource.Dispose();
        _radialMenu.Dispose();
    }
}
