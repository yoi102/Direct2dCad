using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels;

namespace Direct2dCad.wpf.Controls;

public partial class CadCanvas : IDisposable
{
    private CadPointD _pendingPointerScreen;
    private bool _pointerMovePending;
    private bool _pointerRenderScheduled;
    private bool _viewportPresentationScheduled;
    private bool _disposed;
    private readonly DispatcherTimer _viewportInteractionCompletionTimer;

    public CadCanvas()
    {
        InitializeComponent();

        _viewportInteractionCompletionTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Render,
            OnViewportInteractionCompletionTimer,
            Dispatcher);
        _viewportInteractionCompletionTimer.Stop();

        Focusable = true;
        Stretch = System.Windows.Media.Stretch.Fill;

        Loaded += CadCanvas_Loaded;
        SizeChanged += CadCanvas_SizeChanged;
        MouseDown += CadCanvas_MouseDown;
        MouseMove += CadCanvas_MouseMove;
        MouseUp += CadCanvas_MouseUp;
        MouseWheel += CadCanvas_MouseWheel;
        KeyDown += CadCanvas_KeyDown;
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

    public void RefreshView()
    {
        DocumentViewModel?.RequestRender();
    }

    private static void OnDocumentViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CadCanvas canvas)
            return;

        if (e.OldValue is CadDocumentViewModel oldViewModel)
        {
            canvas._viewportInteractionCompletionTimer.Stop();
            oldViewModel.CancelViewportInteractionPreview();
            oldViewModel.PropertyChanged -= canvas.OnDocumentViewModelPropertyChanged;
            oldViewModel.DetachRenderResources();
        }

        if (e.NewValue is CadDocumentViewModel newViewModel)
        {
            newViewModel.PropertyChanged += canvas.OnDocumentViewModelPropertyChanged;
            newViewModel.Direct2DImageRenderHost.AttachImageSource(canvas.d3d11ImageSource);
            newViewModel.AttachRenderResources();
            canvas.UpdateViewportSize();
            canvas.UpdateRenderSize();
            canvas.UpdateCursor(CadCanvasCursorKind.Cross);
            newViewModel.RequestRender();
        }
    }

    private void OnDocumentViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (DocumentViewModel is null)
            return;

        if (e.PropertyName == nameof(CadDocumentViewModel.CadCanvasToolMode))
        {
            UpdateCursor(CadCanvasCursorKind.Cross);
        }
    }

    private void CadCanvas_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateViewportSize();
        UpdateRenderSize();
        DocumentViewModel?.RequestRender();
    }

    private void CadCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        CancelPendingViewportInteraction();
        UpdateViewportSize();
        UpdateRenderSize();
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

        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
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

        _pendingPointerScreen = ToCadPoint(e.GetPosition(this));
        _pointerMovePending = true;
        SchedulePointerMove();
        e.Handled = true;
    }

    private void CadCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DocumentViewModel is null)
            return;

        var screen = ToCadPoint(e.GetPosition(this));
        FlushPendingPointerMove(screen);
        var result = DocumentViewModel.PointerUp(
            screen,
            ToPointerButton(e.ChangedButton));

        ApplyInteractionResult(result, e);
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

    private void CadCanvas_KeyDown(object sender, KeyEventArgs e)
    {
        if (DocumentViewModel is null)
            return;

        if (e.Key == Key.Escape)
        {
            ApplyInteractionResult(DocumentViewModel.Escape(), e);
            return;
        }

        if (e.Key == Key.Enter)
        {
            ApplyInteractionResult(DocumentViewModel.CompleteCurrentDrawing(), e);
            return;
        }

        if (e.Key == Key.Delete)
        {
            ApplyInteractionResult(DocumentViewModel.DeleteSelection(), e);
            return;
        }

        if (e.Key == Key.Tab &&
            (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None)
        {
            ApplyInteractionResult(
                DocumentViewModel.CycleSelection(
                    (Keyboard.Modifiers & ModifierKeys.Shift) != 0),
                e);
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            return;

        switch (e.Key)
        {
            case Key.A:
                if (Keyboard.Modifiers != ModifierKeys.Control)
                    break;

                ApplyInteractionResult(DocumentViewModel.SelectAllEntities(), e);
                break;

            case Key.Z:
                DocumentViewModel.Undo();
                e.Handled = true;
                break;

            case Key.Y:
                DocumentViewModel.Redo();
                e.Handled = true;
                break;

            case Key.S:
                if (Keyboard.Modifiers != ModifierKeys.Control)
                    break;

                if (SaveCommand?.CanExecute(null) == true)
                    SaveCommand.Execute(null);

                e.Handled = true;
                break;
            case Key.C:
                DocumentViewModel.CopySelection();
                e.Handled = true;
                break;

            case Key.V:
                ApplyInteractionResult(DocumentViewModel.BeginClipboardPastePreview(), e);
                break;
        }
    }

    private void UpdateViewportSize()
    {
        DocumentViewModel?.SetViewportSize(ActualWidth, ActualHeight);
    }

    private void UpdateRenderSize()
    {
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
        if (result.CaptureMouse)
            CaptureMouse();

        if (result.ReleaseMouseCapture && IsMouseCaptured)
            ReleaseMouseCapture();

        if (result.Cursor is { } cursor)
            UpdateCursor(cursor);
    }

    private void SchedulePointerMove()
    {
        if (_pointerRenderScheduled)
            return;

        _pointerRenderScheduled = true;
        CompositionTarget.Rendering += OnCompositionTargetRendering;
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        UnschedulePointerMove();
        FlushPendingPointerMove();
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
        DocumentViewModel?.CompleteViewportInteractionPreview();
        ScheduleViewportPresentation();
    }

    private void CancelPendingViewportInteraction()
    {
        _viewportInteractionCompletionTimer.Stop();
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
        if (DocumentViewModel is { } viewModel)
            ApplyInteractionResult(viewModel.PointerMove(_pendingPointerScreen));
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
        Cursor = cursor == CadCanvasCursorKind.Hand ? Cursors.Hand : Cursors.Cross;
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

    private static CadPointD ToCadPoint(Point point)
    {
        return new CadPointD(point.X, point.Y);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CancelPendingViewportInteraction();
        UnschedulePointerMove();
        d3d11ImageSource.Dispose();
    }
}
