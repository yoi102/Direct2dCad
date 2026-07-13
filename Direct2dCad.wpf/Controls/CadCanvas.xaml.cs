using System.Windows;
using System.Windows.Input;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels;

namespace Direct2dCad.wpf.Controls;

public partial class CadCanvas : IDisposable
{
    public CadCanvas()
    {
        InitializeComponent();

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
        UpdateViewportSize();
        UpdateRenderSize();
        DocumentViewModel?.RequestRender();
    }

    private void CadCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();

        if (DocumentViewModel is null)
            return;

        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
        {
            ApplyInteractionResult(
                DocumentViewModel.HandleDoubleClick(ToCadPoint(e.GetPosition(this))),
                e);
            if (e.Handled)
                return;

            ApplyInteractionResult(
                DocumentViewModel.OpenOleObjectAt(ToCadPoint(e.GetPosition(this))),
                e);
            if (e.Handled)
                return;
        }

        var result = DocumentViewModel.PointerDown(
            ToCadPoint(e.GetPosition(this)),
            ToPointerButton(e.ChangedButton),
            forcePan: false,
            modifiers: ToInputModifiers(Keyboard.Modifiers));

        ApplyInteractionResult(result, e);
    }

    private void CadCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (DocumentViewModel is null)
            return;

        var result = DocumentViewModel.PointerMove(ToCadPoint(e.GetPosition(this)));
        ApplyInteractionResult(result, e);
    }

    private void CadCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (DocumentViewModel is null)
            return;

        var result = DocumentViewModel.PointerUp(
            ToCadPoint(e.GetPosition(this)),
            ToPointerButton(e.ChangedButton));

        ApplyInteractionResult(result, e);
    }

    private void CadCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DocumentViewModel is null)
            return;

        var result = DocumentViewModel.MouseWheel(ToCadPoint(e.GetPosition(this)), e.Delta);
        ApplyInteractionResult(result, e);
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
        if (result.CaptureMouse)
            CaptureMouse();

        if (result.ReleaseMouseCapture && IsMouseCaptured)
            ReleaseMouseCapture();

        if (result.Cursor is { } cursor)
            UpdateCursor(cursor);

        if (result.Handled)
            e.Handled = true;
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
        d3d11ImageSource.Dispose();
    }
}
