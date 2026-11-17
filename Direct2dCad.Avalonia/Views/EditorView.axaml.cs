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
            var geometry = document.DynamicInputScreenGeometry;
            var badgeLeft = global::Avalonia.Controls.Canvas.GetLeft(_cursorBadge); var badgeTop = global::Avalonia.Controls.Canvas.GetTop(_cursorBadge);
            var layout = CadDynamicInputLayout.Compute(new CadDynamicInputLayoutRequest(
                surfaceWidth, surfaceHeight, BoxHeight: 26, new global::Direct2dCad.Db.Geometry.CadPointD(point.X, point.Y),
                boxes.Select(box => (CadDynamicInputField)box.DataContext!).Select(field => new CadDynamicInputBoxSpec(field.Key, widths[field.Key])).ToArray(),
                geometry.Anchor, geometry.Point, geometry.XCorner, geometry.YCorner, document.DynamicInputScreenMeasurements,
                DrawAnchorLine: document.CadCanvasToolMode is global::Direct2dCad.ViewModels.Enums.CadCanvasToolMode.Spline or global::Direct2dCad.ViewModels.Enums.CadCanvasToolMode.SplineCv,
                CursorBadgeVisible: _badgeShown,
                CursorBadgePosition: new global::Direct2dCad.Db.Geometry.CadPointD(double.IsNaN(badgeLeft) ? 0 : badgeLeft, double.IsNaN(badgeTop) ? 0 : badgeTop)));
            if (layout.InlineBadgePosition is { } inlineBadge)
            { global::Avalonia.Controls.Canvas.SetLeft(_cursorBadge, inlineBadge.X); global::Avalonia.Controls.Canvas.SetTop(_cursorBadge, inlineBadge.Y); }
            var guides = new global::Avalonia.Media.StreamGeometry();
            using (var context = guides.Open())
                foreach (var stroke in layout.Guides)
                { context.BeginFigure(new Point(stroke.Start.X, stroke.Start.Y), false); context.LineTo(new Point(stroke.End.X, stroke.End.Y)); context.EndFigure(false); }
            DynamicGuides.Data = guides;
            foreach (var box in boxes)
            {
                var field = (CadDynamicInputField)box.DataContext!; var index = document.DynamicInputFields.IndexOf(field);
                if (DynamicFields.ContainerFromIndex(index) is not Control container || !layout.Positions.TryGetValue(field.Key, out var position)) continue;
                box.Width = widths[field.Key] - 46;
                global::Avalonia.Controls.Canvas.SetLeft(container, position.X); global::Avalonia.Controls.Canvas.SetTop(container, position.Y);
            }
            global::Avalonia.Controls.Canvas.SetLeft(DynamicError,Math.Clamp(point.X+18,4,Math.Max(4,surfaceWidth-184)));
            global::Avalonia.Controls.Canvas.SetTop(DynamicError,Math.Clamp(point.Y+54,4,Math.Max(4,surfaceHeight-44)));
        }
        finally { _positioningFields = false; }
    }
    private void FieldFocused(object? sender, RoutedEventArgs e)
    {
        Model.CadDocumentViewModel.SetDynamicInputInteraction(true);
        if (sender is not TextBox box) return;
        box.SelectAll();
        // While typing, the field's full name would only cover the geometry being edited.
        ToolTip.SetIsOpen(box, false); ToolTip.SetServiceEnabled(box, false);
    }
    private void FieldLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) ToolTip.SetServiceEnabled(box, true);
        Dispatcher.UIThread.Post(() => Model.CadDocumentViewModel.SetDynamicInputInteraction(DynamicPanel.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsFocused)));
    }
    private void DynamicKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Source != Canvas && (e.Source is not TextBox text || !DynamicPanel.IsVisualAncestorOf(text))) return;
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            if (e.Source is TextBox && DynamicPanel.IsVisualAncestorOf((global::Avalonia.Visual)e.Source)) { if (Model.CadDocumentViewModel.SubmitDynamicInput()) Canvas.Focus(); e.Handled = true; }
        }
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) { Cancel(); e.Handled = true; return; }
        if (e.Key == Key.F4 && e.KeyModifiers == KeyModifiers.None && Model.CadDocumentViewModel.HasDynamicInput) { Model.CadDocumentViewModel.CycleSnapCandidateCommand.Execute(null); e.Handled = true; return; }
        if (e.Key == Key.Tab && e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift && Model.CadDocumentViewModel.HasDynamicInput)
        {
            var fields = DynamicPanel.GetVisualDescendants().OfType<TextBox>().ToArray(); if (fields.Length == 0) return;
            var index = Array.FindIndex(fields, t => t.IsFocused); index = (index + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? fields.Length - 1 : 1)) % fields.Length; fields[index].Focus(); fields[index].SelectAll(); e.Handled = true;
        }
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


