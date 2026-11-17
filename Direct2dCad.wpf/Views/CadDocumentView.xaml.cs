using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.ComponentModel;
using System.Linq;
using System.Globalization;
using System.Windows.Threading;
using Direct2dCad.Db.Geometry;
using Direct2dCad.ViewModels;
using Direct2dCad.wpf.Controls;
using Direct2dCad.ViewModels.Enums;
using Direct2dCad.wpf.Services.Input;

namespace Direct2dCad.wpf.Views;
/// <summary>
/// CadDocumentView.xaml 的交互逻辑
/// </summary>
public partial class CadDocumentView : IDisposable
{
    private CadDocumentViewModel? _dynamicInputDocument;
    private readonly DependencyPropertyDescriptor _cursorBadgeDescriptor = DependencyPropertyDescriptor.FromProperty(
        CadCanvas.IsCursorBadgeVisibleProperty, typeof(CadCanvas));
    private readonly DependencyPropertyDescriptor _cursorBadgePositionDescriptor = DependencyPropertyDescriptor.FromProperty(
        CadCanvas.CursorBadgePositionProperty, typeof(CadCanvas));
    private Point _dynamicInputPointer;
    private bool _positioningDynamicInput;
    private bool _dynamicInputPositionPending;
    public static readonly DependencyProperty SaveCommandProperty =
        DependencyProperty.Register(
            nameof(SaveCommand),
            typeof(ICommand),
            typeof(CadDocumentView),
            new PropertyMetadata(null));

    public static readonly DependencyProperty RadialMenuActionCommandProperty =
        DependencyProperty.Register(
            nameof(RadialMenuActionCommand),
            typeof(ICommand),
            typeof(CadDocumentView),
            new PropertyMetadata(null));

    public CadDocumentView()
    {
        InitializeComponent();
        KeyDown += DynamicInput_OnKeyDown;
        Loaded += (_, _) => AttachDynamicInput();
        Unloaded += (_, _) => DetachDynamicInput();
        DataContextChanged += (_, _) => { if (IsLoaded) AttachDynamicInput(); };
        dynamicInputSurface.IsKeyboardFocusWithinChanged += (_, _) => UpdateDynamicInputVisibility();
        dynamicInputItems.MouseEnter += (_, _) => UpdateDynamicInputVisibility();
        dynamicInputItems.MouseLeave += (_, _) => UpdateDynamicInputVisibility();
        dynamicInputItems.ItemContainerGenerator.StatusChanged += (_, _) => ScheduleDynamicInputPosition();
    }

    private void AttachDynamicInput()
    {
        DetachDynamicInput();
        _dynamicInputDocument = DataContext as CadDocumentViewModel;
        if (_dynamicInputDocument is not null) _dynamicInputDocument.PropertyChanged += DynamicInputDocument_OnPropertyChanged;
        _cursorBadgeDescriptor.AddValueChanged(cadCanvas, DynamicInputCanvas_OnPresentationChanged);
        _cursorBadgePositionDescriptor.AddValueChanged(cadCanvas, DynamicInputCanvas_OnPresentationChanged);
        UpdateDynamicInputVisibility();
    }
    private void DetachDynamicInput()
    {
        if (_dynamicInputDocument is not null)
        {
            _dynamicInputDocument.PropertyChanged -= DynamicInputDocument_OnPropertyChanged;
            _dynamicInputDocument.SetDynamicInputInteraction(false);
        }
        _dynamicInputDocument = null;
        _cursorBadgeDescriptor.RemoveValueChanged(cadCanvas, DynamicInputCanvas_OnPresentationChanged);
        _cursorBadgePositionDescriptor.RemoveValueChanged(cadCanvas, DynamicInputCanvas_OnPresentationChanged);
        dynamicInputSurface.Visibility = Visibility.Collapsed;
    }
    private void DynamicInputDocument_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CadDocumentViewModel.HasDynamicInput) or nameof(CadDocumentViewModel.IsPanning))
            UpdateDynamicInputVisibility();
        if (e.PropertyName is nameof(CadDocumentViewModel.DynamicInputScreenGeometry) or nameof(CadDocumentViewModel.DynamicInputScreenMeasurements) or nameof(CadDocumentViewModel.DynamicInputError))
            ScheduleDynamicInputPosition();
    }
    private void DynamicInputCanvas_OnPresentationChanged(object? sender, EventArgs e)
    { UpdateDynamicInputVisibility(); ScheduleDynamicInputPosition(); }
    private void UpdateDynamicInputVisibility()
    {
        var visible = _dynamicInputDocument is { HasDynamicInput: true, IsPanning: false } && !cadCanvas.IsRadialMenuActive &&
            (cadCanvas.IsMouseOver || dynamicInputItems.IsMouseOver || dynamicInputSurface.IsKeyboardFocusWithin);
        dynamicInputSurface.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _dynamicInputDocument?.SetDynamicInputInteraction(visible && (dynamicInputItems.IsMouseOver || dynamicInputSurface.IsKeyboardFocusWithin));
    }
    private void DynamicInput_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!dynamicInputItems.IsMouseOver) _dynamicInputPointer = e.GetPosition(cadCanvas);
        ScheduleDynamicInputPosition();
        UpdateDynamicInputVisibility();
    }
    private void DynamicInput_OnMouseLeave(object sender, MouseEventArgs e) => UpdateDynamicInputVisibility();
    private void DynamicInput_OnSizeChanged(object sender, SizeChangedEventArgs e) => ScheduleDynamicInputPosition();
    private void ScheduleDynamicInputPosition()
    {
        if (_dynamicInputPositionPending) return;
        _dynamicInputPositionPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            _dynamicInputPositionPending = false;
            if (IsLoaded) PositionDynamicInput();
        }));
    }
    private void PositionDynamicInput()
    {
        if (_positioningDynamicInput || _dynamicInputDocument is not { } document || cadCanvas.ActualWidth <= 0) return;
        _positioningDynamicInput = true;
        try
        {
            var boxes = DescendantTextBoxes(dynamicInputItems).ToArray();
            var widths = new Dictionary<string, double>();
            foreach (var box in boxes)
            {
                if (box.DataContext is not CadDynamicInputField field) continue;
                var text = new FormattedText(box.Text.Length == 0 ? "0" : box.Text, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch),
                    box.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(box).PixelsPerDip);
                var width = Math.Clamp(Math.Ceiling(text.WidthIncludingTrailingWhitespace) +
                    (field.IsAngle ? 38 : 28), 56, 128);
                box.Width = width; widths[field.Key] = width;
            }
            var geometry = document.DynamicInputScreenGeometry;
            var layout = CadDynamicInputLayout.Compute(new CadDynamicInputLayoutRequest(
                cadCanvas.ActualWidth, cadCanvas.ActualHeight, BoxHeight: 24,
                new CadPointD(_dynamicInputPointer.X, _dynamicInputPointer.Y),
                boxes.Select(box => box.DataContext).OfType<CadDynamicInputField>()
                    .Select(field => new CadDynamicInputBoxSpec(field.Key, widths[field.Key])).ToArray(),
                geometry.Anchor, geometry.Point, geometry.XCorner, geometry.YCorner,
                document.DynamicInputScreenMeasurements,
                DrawAnchorLine: document.CadCanvasToolMode is CadCanvasToolMode.Spline or CadCanvasToolMode.SplineCv,
                CursorBadgeVisible: cadCanvas.IsCursorBadgeVisible,
                CursorBadgePosition: new CadPointD(cadCanvas.CursorBadgePosition.X, cadCanvas.CursorBadgePosition.Y)));
            cursorToolBadge.SetCurrentValue(Canvas.LeftProperty, layout.InlineBadgePosition?.X ?? cadCanvas.CursorBadgePosition.X);
            cursorToolBadge.SetCurrentValue(Canvas.TopProperty, layout.InlineBadgePosition?.Y ?? cadCanvas.CursorBadgePosition.Y);
            foreach (var box in boxes)
            {
                if (box.DataContext is not CadDynamicInputField field ||
                    ItemsControl.ContainerFromElement(dynamicInputItems, box) is not FrameworkElement container ||
                    !layout.Positions.TryGetValue(field.Key, out var position)) continue;
                Canvas.SetLeft(container, position.X); Canvas.SetTop(container, position.Y);
            }
            var guide = new StreamGeometry();
            using (var context = guide.Open())
                foreach (var stroke in layout.Guides)
                {
                    context.BeginFigure(new Point(stroke.Start.X, stroke.Start.Y), false, false);
                    context.LineTo(new Point(stroke.End.X, stroke.End.Y), true, false);
                }
            guide.Freeze(); dynamicInputGuides.Data = guide;
            dynamicInputError.Margin = new Thickness(
                Math.Clamp(_dynamicInputPointer.X + 18, 4, Math.Max(4, cadCanvas.ActualWidth - 184)),
                Math.Clamp(_dynamicInputPointer.Y + 54, 4, Math.Max(4, cadCanvas.ActualHeight - 44)), 0, 0);
        }
        finally { _positioningDynamicInput = false; }
    }
    private TextBox[] DynamicInputBoxes()
    {
        dynamicInputItems.UpdateLayout();
        return DescendantTextBoxes(dynamicInputItems).ToArray();
    }
    private static IEnumerable<TextBox> DescendantTextBoxes(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox box) yield return box;
            else foreach (var nested in DescendantTextBoxes(child)) yield return nested;
        }
    }
    private void DynamicInputField_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    { if (sender is TextBox box) box.SelectAll(); }
    private void DynamicInputField_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox box || box.IsKeyboardFocusWithin && box.DataContext is CadDynamicInputField { IsLocked: true }) return;
        e.Handled = true;
        box.Focus();
        box.SelectAll();
    }
    private void DynamicInput_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) HandleDynamicInputKey(e.Key, Keyboard.Modifiers, e);
    }

    private void DynamicInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (!e.Handled && e.Key == Key.Escape) HandleDynamicInputKey(e.Key, Keyboard.Modifiers, e);
    }

    internal void HandleDynamicInputKey(Key key, ModifierKeys modifiers, KeyEventArgs e)
    {
        if (e.Handled || key is Key.ImeProcessed or Key.DeadCharProcessed) return;
        var shortcut = CadShortcutCatalog.Find(CadShortcutScope.DynamicInput, key, modifiers);
        if (shortcut is null) return;
        if (_dynamicInputDocument is not { } document) return;
        if (HandleSnapCandidateKey(document, key, modifiers))
        {
            e.Handled = true;
            return;
        }
        if (!document.HasDynamicInput || dynamicInputSurface.Visibility != Visibility.Visible) return;
        if (shortcut.Action is CadShortcutAction.NextField or CadShortcutAction.PreviousField)
        {
            var boxes = DynamicInputBoxes();
            if (boxes.Length == 0) return;
            var current = Array.FindIndex(boxes, b => b.IsKeyboardFocusWithin);
            var backwards = shortcut.Action == CadShortcutAction.PreviousField;
            var next = (current + (backwards ? -1 : 1) + boxes.Length) % boxes.Length;
            if (current < 0) next = backwards ? boxes.Length - 1 : 0;
            boxes[next].Focus(); boxes[next].SelectAll();
            e.Handled = true;
        }
        else if (shortcut.Action == CadShortcutAction.Confirm && dynamicInputSurface.IsKeyboardFocusWithin)
        {
            if (!CadEnterKeyGuard.ShouldIgnoreRepeat(e, modifiers) && document.SubmitDynamicInput()) cadCanvas.Focus();
            e.Handled = true;
        }
        else if (shortcut.Action == CadShortcutAction.Cancel && dynamicInputSurface.IsKeyboardFocusWithin)
        {
            if (!e.IsRepeat)
            {
                cadCanvas.CancelInteraction();
                cadCanvas.Focus();
            }
            e.Handled = true;
        }
    }

    internal static bool HandleSnapCandidateKey(CadDocumentViewModel document, Key key, ModifierKeys modifiers)
    {
        if (CadShortcutCatalog.Find(CadShortcutScope.DynamicInput, key, modifiers)?.Action !=
            CadShortcutAction.NextSnapCandidate || !document.HasSnapCandidates) return false;
        document.CycleSnapCandidateCommand.Execute(null);
        return true;
    }

    private void DynamicInput_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_dynamicInputDocument is not { HasDynamicInput: true } || dynamicInputSurface.Visibility != Visibility.Visible) return;
        if (dynamicInputSurface.IsKeyboardFocusWithin)
        {
            // A queued preview refresh can replace Text after focus and clear its selection.
            // The first keystroke must still replace a live measurement, rather than append to it.
            if (Keyboard.FocusedElement is TextBox { DataContext: CadDynamicInputField { IsLocked: false } } liveBox)
                liveBox.SelectAll();
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None ||
            !e.Text.All(c => char.IsDigit(c) || c is '.' or ',' or '-' or '+')) return;
        var box = DynamicInputBoxes().FirstOrDefault();
        if (box is null) return;
        box.Focus(); box.Text = e.Text; box.CaretIndex = box.Text.Length;
        e.Handled = true;
    }

    public ICommand? SaveCommand
    {
        get => (ICommand?)GetValue(SaveCommandProperty);
            set => SetValue(SaveCommandProperty, value);
    }

    private void EditParameters_OnRequestCanvasFocus(object? sender, EventArgs e) => cadCanvas.Focus();

    public ICommand? RadialMenuActionCommand
    {
        get => (ICommand?)GetValue(RadialMenuActionCommandProperty);
        set => SetValue(RadialMenuActionCommandProperty, value);
    }

    public void Dispose()
    {
        DetachDynamicInput();
        cadCanvas.Dispose();
    }
}
