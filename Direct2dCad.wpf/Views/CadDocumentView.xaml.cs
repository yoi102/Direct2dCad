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
using Direct2dCad.ViewModels;
using Direct2dCad.wpf.Controls;
using Direct2dCad.ViewModels.Enums;

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
            var measurements = document.DynamicInputScreenMeasurements;
            var measuredKeys = measurements.Select(m => m.Key).ToHashSet();
            var endpoint = new Point(geometry.Point.X, geometry.Point.Y);
            var guide = new StreamGeometry();
            using (var context = guide.Open())
            {
                var positions = new Dictionary<string, Point>();
                // Point coordinates stay by the cursor; geometric parameters use their construction lines.
                var cursorFields = boxes.Where(box => box.DataContext is CadDynamicInputField field &&
                    !measuredKeys.Contains(field.Key) && field.Key is not ("Width" or "Height")).ToArray();
                var inlineBadge = cursorFields.Length > 0 && cadCanvas.IsCursorBadgeVisible;
                var groupWidth = cursorFields.Sum(box => widths[((CadDynamicInputField)box.DataContext).Key]) +
                    Math.Max(0, cursorFields.Length - 1) * 6 + (inlineBadge ? 34 : 0);
                var desiredX = _dynamicInputPointer.X + 18;
                var desiredY = _dynamicInputPointer.Y + 18;
                if (desiredX + groupWidth > cadCanvas.ActualWidth - 4) desiredX = _dynamicInputPointer.X - groupWidth - 18;
                if (desiredY + 24 > cadCanvas.ActualHeight - 4) desiredY = _dynamicInputPointer.Y - 30;
                var groupX = Math.Clamp(desiredX, 4, Math.Max(4, cadCanvas.ActualWidth - groupWidth - 4));
                var groupY = Math.Clamp(desiredY, 6, Math.Max(6, cadCanvas.ActualHeight - 30));
                cursorToolBadge.SetCurrentValue(Canvas.LeftProperty, inlineBadge ? groupX : cadCanvas.CursorBadgePosition.X);
                cursorToolBadge.SetCurrentValue(Canvas.TopProperty, inlineBadge ? groupY - 2 : cadCanvas.CursorBadgePosition.Y);
                if (inlineBadge) groupX += 34;
                foreach (var box in boxes)
                    if (box.DataContext is CadDynamicInputField field)
                        positions[field.Key] = new(groupX, groupY);
                foreach (var box in cursorFields)
                    if (box.DataContext is CadDynamicInputField field)
                    { positions[field.Key] = new(groupX, groupY); groupX += widths[field.Key] + 6; }
                void Line(Point from, Point to)
                { context.BeginFigure(from, false, false); context.LineTo(to, true, false); }
                Point Dimension(Point start, Point end, Vector normal, double offset)
                {
                    Line(start, start + normal * (offset + 6)); Line(end, end + normal * (offset + 6));
                    Line(start + normal * offset, end + normal * offset);
                    return start + (end - start) * .5 + normal * offset;
                }
                if (geometry.Anchor is { } anchor)
                {
                    var start = new Point(anchor.X, anchor.Y);
                    var direction = endpoint - start;
                    if (positions.ContainsKey("Width") && positions.ContainsKey("Height") && !measuredKeys.Contains("Width"))
                    {
                        var xCorner = new Point(geometry.XCorner.X, geometry.XCorner.Y);
                        var yCorner = new Point(geometry.YCorner.X, geometry.YCorner.Y);
                        var widthStart = start.Y + xCorner.Y > yCorner.Y + endpoint.Y ? start : yCorner;
                        var widthEnd = widthStart == start ? xCorner : endpoint;
                        var heightStart = start.X + yCorner.X > xCorner.X + endpoint.X ? start : xCorner;
                        var heightEnd = heightStart == start ? yCorner : endpoint;
                        Vector Outward(Point from, Point to)
                        {
                            var edge = to - from;
                            var outward = edge.Length > 1e-6 ? new Vector(-edge.Y, edge.X) / edge.Length : new Vector(0, 1);
                            if (Vector.Multiply(outward, from + (to - from) * .5 - (start + direction * .5)) < 0) outward = -outward;
                            return outward;
                        }
                        positions["Width"] = Dimension(widthStart, widthEnd, Outward(widthStart, widthEnd), 28) - new Vector(widths["Width"] / 2, 12);
                        positions["Height"] = Dimension(heightStart, heightEnd, Outward(heightStart, heightEnd), 54) - new Vector(widths["Height"] / 2, 12);
                    }
                    else if (positions.ContainsKey("Length") && document.CadCanvasToolMode == CadCanvasToolMode.Spline)
                        Line(start, endpoint);
                }
                foreach (var measurement in measurements)
                {
                    if (!widths.TryGetValue(measurement.Key, out var width)) continue;
                    var start = new Point(measurement.Start.X, measurement.Start.Y);
                    var end = new Point(measurement.End.X, measurement.End.Y);
                    var direction = end - start;
                    var unit = direction.Length > 1e-6 ? direction / direction.Length : new Vector(1, 0);
                    var normal = new Vector(-unit.Y, unit.X);
                    if (normal.Y > 0) normal = -normal;
                    positions[measurement.Key] = Dimension(start, end, normal, 26 + measurement.StackIndex * 24) - new Vector(width / 2, 12);
                }
                var occupied = new List<Rect>();
                foreach (var box in boxes)
                {
                    if (box.DataContext is not CadDynamicInputField field ||
                        ItemsControl.ContainerFromElement(dynamicInputItems, box) is not FrameworkElement container) continue;
                    var desired = positions[field.Key];
                    var width = widths[field.Key];
                    var x = Math.Clamp(desired.X, 4, Math.Max(4, cadCanvas.ActualWidth - width - 4));
                    var y = Math.Clamp(desired.Y, 4, Math.Max(4, cadCanvas.ActualHeight - 28));
                    var bounds = new Rect(x, y, width, 24);
                    for (var attempt = 0; attempt < boxes.Length * 2 && occupied.Any(other => bounds.IntersectsWith(other)); attempt++)
                    {
                        var other = occupied.First(other => bounds.IntersectsWith(other));
                        y = other.Bottom + 6 <= cadCanvas.ActualHeight - 28 ? other.Bottom + 6 : Math.Max(4, other.Top - 30);
                        bounds = new(x, y, width, 24);
                    }
                    Canvas.SetLeft(container, x); Canvas.SetTop(container, y);
                    occupied.Add(bounds);
                }
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
        if (_dynamicInputDocument is { IsCurveEditTool: true } editDocument)
        {
            if (e.Key==Key.Escape) { editDocument.Escape(); cadCanvas.Focus(); e.Handled=true; return; }
        }
        if (_dynamicInputDocument is not { HasDynamicInput: true } document || dynamicInputSurface.Visibility != Visibility.Visible) return;
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Control && document.HasSnapCandidates)
        {
            document.CycleSnapCandidateCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Tab && (Keyboard.Modifiers & ~ModifierKeys.Shift) == ModifierKeys.None)
        {
            var boxes = DynamicInputBoxes();
            if (boxes.Length == 0) return;
            var current = Array.FindIndex(boxes, b => b.IsKeyboardFocusWithin);
            var next = (current + ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1) + boxes.Length) % boxes.Length;
            if (current < 0) next = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? boxes.Length - 1 : 0;
            boxes[next].Focus(); boxes[next].SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && dynamicInputSurface.IsKeyboardFocusWithin)
        {
            if (document.SubmitDynamicInput()) cadCanvas.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && dynamicInputSurface.IsKeyboardFocusWithin)
        {
            document.Escape(); cadCanvas.Focus(); e.Handled = true;
        }
        else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.Control)
        {
            document.ClearDynamicInputLocks(); document.RequestRender(); e.Handled = true;
        }
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
