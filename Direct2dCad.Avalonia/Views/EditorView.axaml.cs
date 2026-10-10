using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using Direct2dCad.Avalonia.Controls;
using Direct2dCad.ViewModels;
using System.ComponentModel;

namespace Direct2dCad.Avalonia.Views;

public partial class EditorView : UserControl, IDisposable
{
    public EditorTabViewModel Model { get; } = null!;
    public CadCanvas Canvas { get; } = null!;
    private bool _enterHeld;
    private bool _disposed;
    private readonly Border _cursorBadge = new() { Width=28,Height=28,Padding=new Thickness(4),CornerRadius=new CornerRadius(4),BorderThickness=new Thickness(1),BorderBrush=global::Avalonia.Media.Brushes.Gray,Background=new global::Avalonia.Media.SolidColorBrush(global::Avalonia.Media.Color.Parse("#D0202429")),IsVisible=false,IsHitTestVisible=false };
    private readonly Generated.CursorIconResources _cursorIcons = new();
    private bool _badgeShown;
    private string? _badgeKey;
    public EditorView() => InitializeComponent();
    public EditorView(EditorTabViewModel model)
    {
        InitializeComponent(); DataContext = Model = model;
        Canvas = new CadCanvas(model.CadDocumentViewModel, model.ExecuteRadialMenuActionCommand);
        CanvasSurface.Children.Insert(0, Canvas);
        var badgeLayer=new global::Avalonia.Controls.Canvas {IsHitTestVisible=false};badgeLayer.Children.Add(_cursorBadge);CanvasSurface.Children.Add(badgeLayer);
        global::Avalonia.Automation.AutomationProperties.SetAutomationId(_cursorBadge,"CanvasToolBadge");
        Canvas.CursorBadgeChanged += UpdateCursorBadge;
        Canvas.PointerPositionChanged += PositionFields;
        model.CadDocumentViewModel.DynamicInputFields.CollectionChanged += DynamicFieldsChanged;
        CanvasSurface.AddHandler(PointerPressedEvent, (_, e) => { if (e.Source == Overlay) { Canvas.Focus(); e.Handled = true; } }, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, DynamicKey, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => { if (e.Key == Key.Enter) _enterHeld = false; }, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, TextEntry, RoutingStrategies.Tunnel);
        model.CadDocumentViewModel.PropertyChanged += DocumentChanged;
        Canvas.ContextMenu = new Generated.EditorContextMenu { DataContext = model };
        Canvas.TraceMenu(Canvas.ContextMenu);
    }
    private Point _dynamicPointer;
    private bool _positioningFields;
    private void UpdateCursorBadge(Point point,bool shown)
    {
        _badgeShown=shown;_cursorBadge.IsVisible=shown;if(!shown)return;
        var key=Model.CadDocumentViewModel.IsPastePreviewActive?"Paste":Model.CadDocumentViewModel.CadCanvasToolMode.ToString();
        if(key != _badgeKey) { _badgeKey=key;
        if(_cursorIcons.Resources.TryGetValue(key,out var resource) && resource is global::Avalonia.Controls.Templates.IDataTemplate template && template.Build(null) is Control icon) {icon.Width=icon.Height=20;_cursorBadge.Child=icon;} else _cursorBadge.Child=new CadIcon {Kind="CursorDefaultOutline",Foreground=global::Avalonia.Media.Brushes.White,Width=20,Height=20}; }
        var x=point.X+16;var y=point.Y+16;if(x+28>CanvasSurface.Bounds.Width)x=point.X-44;if(y+28>CanvasSurface.Bounds.Height)y=point.Y-44;
        global::Avalonia.Controls.Canvas.SetLeft(_cursorBadge,Math.Max(0,x));global::Avalonia.Controls.Canvas.SetTop(_cursorBadge,Math.Max(0,y));
        PositionFields(point);
    }
    private void DynamicFieldsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Dispatcher.UIThread.Post(() => PositionFields(_dynamicPointer), DispatcherPriority.Background);
    private void DocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CadDocumentViewModel.DynamicInputScreenGeometry) or nameof(CadDocumentViewModel.DynamicInputScreenMeasurements) or nameof(CadDocumentViewModel.HasDynamicInput))
            Dispatcher.UIThread.Post(() => PositionFields(_dynamicPointer), DispatcherPriority.Background);
    }
    private void PositionFields(Point point)
    {
        _dynamicPointer = point;
        if (_positioningFields || !Model.CadDocumentViewModel.HasDynamicInput || CanvasSurface.Bounds.Width <= 0) return;
        _positioningFields = true;
        try
        {
            var document = Model.CadDocumentViewModel;
            var boxes = DynamicFields.GetVisualDescendants().OfType<TextBox>().Where(box => box.DataContext is CadDynamicInputField).ToArray();
            if (boxes.Length == 0) return;
            var surfaceWidth = CanvasSurface.Bounds.Width; var surfaceHeight = CanvasSurface.Bounds.Height;
            DynamicPanel.Width = DynamicFields.Width = surfaceWidth; DynamicPanel.Height = DynamicFields.Height = surfaceHeight;
            var widths = boxes.ToDictionary(box => ((CadDynamicInputField)box.DataContext!).Key, box => Math.Clamp((box.Text?.Length ?? 1) * 7 + 48, 90, 170d));
            var geometry = document.DynamicInputScreenGeometry; var measurements = document.DynamicInputScreenMeasurements;
            var measured = measurements.Select(m => m.Key).ToHashSet();
            var endpoint = new Point(geometry.Point.X, geometry.Point.Y);
            var cursorFields = boxes.Where(box => box.DataContext is CadDynamicInputField field && !measured.Contains(field.Key) && field.Key is not ("Width" or "Height")).ToArray();
            var groupWidth = cursorFields.Sum(box => widths[((CadDynamicInputField)box.DataContext!).Key]) + Math.Max(0, cursorFields.Length - 1) * 6 + (_badgeShown && cursorFields.Length>0?34:0);
            var desiredX = point.X + 18; var desiredY = point.Y + 18;
            if (desiredX + groupWidth > surfaceWidth - 4) desiredX = point.X - groupWidth - 18;
            if (desiredY + 26 > surfaceHeight - 4) desiredY = point.Y - 30;
            var x = Math.Clamp(desiredX, 4, Math.Max(4, surfaceWidth - groupWidth - 4));
            var y = Math.Clamp(desiredY, 4, Math.Max(4, surfaceHeight - 30));
            if(_badgeShown && cursorFields.Length>0) {global::Avalonia.Controls.Canvas.SetLeft(_cursorBadge,x);global::Avalonia.Controls.Canvas.SetTop(_cursorBadge,y-2);x+=34;}
            var positions = widths.Keys.ToDictionary(key => key, _ => new Point(x,y));
            foreach (var box in cursorFields) { var field = (CadDynamicInputField)box.DataContext!; positions[field.Key] = new Point(x,y); x += widths[field.Key] + 6; }
            var guides = new global::Avalonia.Media.StreamGeometry();
            using (var context = guides.Open())
            {
                void Line(Point from, Point to) { context.BeginFigure(from, false); context.LineTo(to); context.EndFigure(false); }
                Point Dimension(Point start, Point end, Vector normal, double offset)
                {
                    Line(start, start + normal * (offset + 6)); Line(end, end + normal * (offset + 6)); Line(start + normal * offset, end + normal * offset);
                    return start + (end - start) * .5 + normal * offset;
                }
                if (geometry.Anchor is { } anchor && positions.ContainsKey("Width") && positions.ContainsKey("Height") && !measured.Contains("Width"))
                {
                    var start = new Point(anchor.X, anchor.Y); var xCorner = new Point(geometry.XCorner.X, geometry.XCorner.Y); var yCorner = new Point(geometry.YCorner.X, geometry.YCorner.Y);
                    var widthStart = start.Y + xCorner.Y > yCorner.Y + endpoint.Y ? start : yCorner; var widthEnd = widthStart == start ? xCorner : endpoint;
                    var heightStart = start.X + yCorner.X > xCorner.X + endpoint.X ? start : xCorner; var heightEnd = heightStart == start ? yCorner : endpoint;
                    Vector Outward(Point from, Point to)
                    {
                        var edge = new Vector(to.X - from.X, to.Y - from.Y); var normal = edge.Length > 1e-6 ? new Vector(-edge.Y,edge.X) / edge.Length : new Vector(0,1);
                        var delta = from + (to - from) * .5 - (start + (endpoint - start) * .5);
                        return normal.X * delta.X + normal.Y * delta.Y < 0 ? -normal : normal;
                    }
                    positions["Width"] = Dimension(widthStart,widthEnd,Outward(widthStart,widthEnd),28) - new Vector(widths["Width"] / 2,13);
                    positions["Height"] = Dimension(heightStart,heightEnd,Outward(heightStart,heightEnd),54) - new Vector(widths["Height"] / 2,13);
                }
                foreach (var measurement in measurements)
                {
                    if (!widths.TryGetValue(measurement.Key,out var width)) continue;
                    var start = new Point(measurement.Start.X,measurement.Start.Y); var end = new Point(measurement.End.X,measurement.End.Y);
                    var direction = new Vector(end.X - start.X, end.Y - start.Y); var unit = direction.Length > 1e-6 ? direction / direction.Length : new Vector(1,0); var normal = new Vector(-unit.Y,unit.X); if (normal.Y > 0) normal = -normal;
                    positions[measurement.Key] = Dimension(start,end,normal,26 + measurement.StackIndex * 24) - new Vector(width/2,13);
                }
            }
            DynamicGuides.Data = guides;
            var occupied = new List<Rect>();
            foreach (var box in boxes)
            {
                var field = (CadDynamicInputField)box.DataContext!; var index = document.DynamicInputFields.IndexOf(field);
                if (DynamicFields.ContainerFromIndex(index) is not Control container) continue;
                var width = widths[field.Key]; box.Width = width - 46; var desired = positions[field.Key];
                var bx = Math.Clamp(desired.X,4,Math.Max(4,surfaceWidth-width-4)); var by = Math.Clamp(desired.Y,4,Math.Max(4,surfaceHeight-30));
                var bounds = new Rect(bx,by,width,26);
                for (var attempt=0; attempt<boxes.Length*2 && occupied.Any(other=>bounds.Intersects(other)); attempt++)
                {
                    var other=occupied.First(other=>bounds.Intersects(other)); by=other.Bottom+6<=surfaceHeight-30?other.Bottom+6:Math.Max(4,other.Top-32); bounds=new Rect(bx,by,width,26);
                }
                global::Avalonia.Controls.Canvas.SetLeft(container,bx); global::Avalonia.Controls.Canvas.SetTop(container,by); occupied.Add(bounds);
            }
            global::Avalonia.Controls.Canvas.SetLeft(DynamicError,Math.Clamp(point.X+18,4,Math.Max(4,surfaceWidth-184)));
            global::Avalonia.Controls.Canvas.SetTop(DynamicError,Math.Clamp(point.Y+54,4,Math.Max(4,surfaceHeight-44)));
        }
        finally { _positioningFields = false; }
    }
    private void FieldFocused(object? sender, RoutedEventArgs e) { Model.CadDocumentViewModel.SetDynamicInputInteraction(true); if (sender is TextBox box) box.SelectAll(); }
    private void FieldLostFocus(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() => Model.CadDocumentViewModel.SetDynamicInputInteraction(DynamicPanel.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsFocused)));
    private void DynamicKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Source != Canvas && (e.Source is not TextBox text || !DynamicPanel.IsVisualAncestorOf(text))) return;
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            if (_enterHeld) { e.Handled = true; return; } _enterHeld = true;
            if (e.Source is TextBox && DynamicPanel.IsVisualAncestorOf((global::Avalonia.Visual)e.Source)) { if (Model.CadDocumentViewModel.SubmitDynamicInput()) Canvas.Focus(); e.Handled = true; }
        }
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) { if (e.Source is TextBox) { Model.CadDocumentViewModel.ClearDynamicInputLocks(); Canvas.Focus(); } else Cancel(); e.Handled = true; return; }
        if (e.Key == Key.F4 && e.KeyModifiers == KeyModifiers.None && Model.CadDocumentViewModel.HasDynamicInput) { Model.CadDocumentViewModel.CycleSnapCandidateCommand.Execute(null); e.Handled = true; return; }
        if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift && Model.CadDocumentViewModel.HasDynamicInput)
        {
            var fields = DynamicPanel.GetVisualDescendants().OfType<TextBox>().ToArray(); if (fields.Length == 0) return;
            var index = Array.FindIndex(fields, t => t.IsFocused); index = (index + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? fields.Length - 1 : 1)) % fields.Length; fields[index].Focus(); fields[index].SelectAll(); e.Handled = true;
        }
        if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.Control && Model.CadDocumentViewModel.HasDynamicInput) { Model.CadDocumentViewModel.ClearDynamicInputLocks(); Canvas.Focus(); e.Handled = true; }
    }
    private void TextEntry(object? sender, TextInputEventArgs e)
    {
        if (e.Source != Canvas || !Model.CadDocumentViewModel.HasDynamicInput || string.IsNullOrWhiteSpace(e.Text) || !e.Text.All(c => char.IsDigit(c) || c is '+' or '-' or '.' or ',')) return;
        var field = DynamicPanel.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(); if (field is null) return;
        field.Focus(); field.Text = e.Text; field.CaretIndex = e.Text.Length; e.Handled = true;
    }
    private async void LayoutSettingsClicked(object? sender, RoutedEventArgs e)
    {
        var view = KnownViews.Create(Model.LayoutWorkspace); if (view is null) return;
        var window=new Window {Title=Direct2dCad.Lang.CadUiText.Get("Layout"),Width=480,Height=700,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        window.Content=CadWindowChrome.Wrap(window,view); await window.ShowDialog(App.Window!);
    }
    private void DocumentSettingsClicked(object? sender, RoutedEventArgs e)
    {
        var dialogs = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Direct2dCad.ViewModels.Services.Platform.IDialogService>();
        dialogs.ShowDocumentSettingsDialog(new Direct2dCad.ViewModels.Settings.DocumentSettingsViewModel(Model, dialogs));
    }
    public void Cancel() { Canvas.Cancel(); Canvas.Focus(); }
    public void Dispose() { if (_disposed) return; _disposed = true; Model.CadDocumentViewModel.PropertyChanged -= DocumentChanged; Model.CadDocumentViewModel.DynamicInputFields.CollectionChanged -= DynamicFieldsChanged; Canvas.Dispose(); }
}


