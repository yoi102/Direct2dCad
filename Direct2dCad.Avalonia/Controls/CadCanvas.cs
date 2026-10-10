using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Platform;
using global::Avalonia.Threading;
using global::Avalonia.Rendering.Composition;
using Direct2dCad.Db.Geometry;
using Direct2dCad.Rendering;
using Direct2dCad.Rendering.Direct2D.Hosting;
using Direct2dCad.ViewModels;
using Direct2dCad.Client.Common.Settings;
using System.Windows.Input;

namespace Direct2dCad.Avalonia.Controls;

public sealed class CadCanvas : Control, ID3D11ImageSource, IDisposable
{
    private readonly CadDocumentViewModel _document;
    private readonly Direct2DImageRenderHost _host;
    private readonly DispatcherTimer _viewportTimer;
    private readonly DispatcherTimer _cacheTimer;
    private WriteableBitmap? _bitmap;
    private GpuCanvasPresenter? _gpu;
    private int _gpuGeneration;
    internal string? GpuFailure { get; private set; }
    internal long CpuReadbackFrames { get; private set; }
    internal long GpuPresentedFrames => _gpu?.PresentedFrames ?? 0;
    internal bool IsRenderHostAttached => _attached;
    private IPointer? _pointer;
    private bool _attached, _disposed, _release, _scheduled, _pointerScheduled, _cacheScheduled;
    private Action? _flush;
    private Point _pendingPointer, _rightStart;
    private bool _rightPending, _rightPan, _suppressPanMenu;
    private readonly CadRadialMenu _radial;
    private TopLevel? _traceRoot;
    public event Action<Point>? PointerPositionChanged;
    internal event Action<Point,bool>? CursorBadgeChanged;
    private bool _pointerInside;
    private void BadgeChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e) { if(e.PropertyName is nameof(CadDocumentViewModel.CadCanvasToolMode) or nameof(CadDocumentViewModel.CanvasCursor) or nameof(CadDocumentViewModel.IsPastePreviewActive)) UpdateCursorBadge(); }
    private void UpdateCursorBadge() => CursorBadgeChanged?.Invoke(_pendingPointer,_pointerInside && !_rightPending && !_radial.IsVisible && _document.CanvasCursor == CadCanvasCursorKind.Arrow && (_document.HasActiveDrawingTool || _document.IsPastePreviewActive));
    public int SurfaceWidth { get; private set; } = 1;
    public int SurfaceHeight { get; private set; } = 1;
    public bool IsRadialMenuActive => _radial.IsVisible;
    internal bool RadialUsesOwnerWindow => _radial.UsesOwnerWindow;
    internal bool HasCapturedPointer => _pointer?.Captured == this;
    internal CadRadialMenuGesture RadialGesture => _radial.Gesture;
    public CadCanvas(CadDocumentViewModel document, ICommand radialAction)
    {
        _document = document; _host = (Direct2DImageRenderHost)document.RenderSession;
        Focusable = true; ClipToBounds = true;
        _radial = new CadRadialMenu(this, document, radialAction);
        _viewportTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _viewportTimer.Tick += (_, _) => { _viewportTimer.Stop(); _document.CompleteViewportInteractionPreview(); };
        _cacheTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
        _cacheTimer.Tick += (_, _) => { _cacheTimer.Stop(); ScheduleCache(); };
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
        SizeChanged += (_, _) => Resize();
        // Win32's context gesture recognizer can mark the release handled before
        // ordinary subscribers run. The canvas must still finish its own capture.
        AddHandler(PointerPressedEvent,Pressed,global::Avalonia.Interactivity.RoutingStrategies.Bubble,true);
        AddHandler(PointerMovedEvent,Moved,global::Avalonia.Interactivity.RoutingStrategies.Bubble,true);
        AddHandler(PointerReleasedEvent,Released,global::Avalonia.Interactivity.RoutingStrategies.Bubble,true);
        _document.PropertyChanged += BadgeChanged;
        PointerEntered += (_,_) => { _pointerInside=true; UpdateCursorBadge(); };
        PointerExited += (_, _) => { _pointerInside=false; UpdateCursorBadge(); if (_pointer?.Captured != this) { FlushPointer(); _document.PointerLeave(); } };
        PointerCaptureLost += (_, _) => { Trace("capture-lost"); if (!_release) CancelGesture(); };
        PointerWheelChanged += Wheel; KeyDown += Keys;
        AddHandler(ContextRequestedEvent, (_, e) =>
        {
            // Avalonia may synthesize ContextRequested after capture is released.
            // A completed right drag belongs to pan; a fresh click or keyboard menu still opens.
            if ((_rightPending || _suppressPanMenu) && e.TryGetPosition(this, out var contextPoint)) e.Handled = true;
        }, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        KeyUp += (_, e) => { if (_radial.IsVisible) { _radial.SetModifiers(e.KeyModifiers); e.Handled = true; } };
        LostFocus += (_, _) => { Trace("focus-lost"); if (_pointer?.Captured == this) CancelGesture(); };
    }
    private void Trace(string stage)=>Services.CanvasInputTrace.Write($"canvas {stage} pending={_rightPending} pan={_rightPan} releasing={_release} capture={_pointer?.Captured?.GetType().Name} menu={ContextMenu?.IsOpen}");
    internal void TraceMenu(ContextMenu menu)
    {
        if(!Services.CanvasInputTrace.Enabled)return;
        menu.Opened+=async (_,_)=>
        {
            Trace("menu-opened");
            await Task.Delay(100);
            if(!menu.IsOpen || _disposed || TopLevel.GetTopLevel(this) is null)return;
            var root=TopLevel.GetTopLevel(menu);
            Services.CanvasInputTrace.Write($"menu layout open={menu.IsOpen} bounds={menu.Bounds} maxHeight={menu.MaxHeight} clickScreen={this.PointToScreen(_rightStart)} menuScreen={(root is null?default:menu.PointToScreen(default))} template={menu.Template is not null} items={menu.Items.Count} visible={menu.IsVisible} root={root?.GetType().Name} rootBounds={root?.Bounds} client={root?.ClientSize}");
        };
        menu.Closed+=(_,_)=>Trace("menu-closed");
    }
    private void TraceRootPressed(object? sender,PointerPressedEventArgs e)
    {
        if(!IsEffectivelyVisible || !new Rect(Bounds.Size).Contains(e.GetPosition(this)))return;
        Services.CanvasInputTrace.Write($"root press route={e.Route} kind={e.GetCurrentPoint(this).Properties.PointerUpdateKind} handled={e.Handled} source={e.Source?.GetType().Name} point={e.GetPosition(this)}");
    }
    private void TraceRootReleased(object? sender,PointerReleasedEventArgs e)
    {
        if(!IsEffectivelyVisible || !new Rect(Bounds.Size).Contains(e.GetPosition(this)))return;
        Services.CanvasInputTrace.Write($"root release route={e.Route} button={e.InitialPressMouseButton} kind={e.GetCurrentPoint(this).Properties.PointerUpdateKind} handled={e.Handled} source={e.Source?.GetType().Name} point={e.GetPosition(this)}");
    }
    private void Attach()
    {
        if (_disposed || _attached) return; _attached = true;
        if(Services.CanvasInputTrace.Enabled && TopLevel.GetTopLevel(this) is {} root)
        {
            _traceRoot=root;
            root.AddHandler(PointerPressedEvent,TraceRootPressed,global::Avalonia.Interactivity.RoutingStrategies.Tunnel|global::Avalonia.Interactivity.RoutingStrategies.Bubble,true);
            root.AddHandler(PointerReleasedEvent,TraceRootReleased,global::Avalonia.Interactivity.RoutingStrategies.Tunnel|global::Avalonia.Interactivity.RoutingStrategies.Bubble,true);
            Services.CanvasInputTrace.Write($"attached executable={Environment.ProcessPath}");
        }
        _host.AttachImageSource(this); _document.SetRenderScheduler(ScheduleRender);
        _host.RenderCacheBuildRequested += CacheRequested; _document.AttachRenderResources(); Resize();
        _ = InitializeGpuAsync(++_gpuGeneration);
    }
    private void Detach()
    {
        if(_traceRoot is {} root)
        {
            root.RemoveHandler(PointerPressedEvent,TraceRootPressed);root.RemoveHandler(PointerReleasedEvent,TraceRootReleased);_traceRoot=null;
        }
        if (!_attached) return; _attached = false; CancelGesture(); _viewportTimer.Stop(); _cacheTimer.Stop();
        _document.SetRenderScheduler(null); _host.RenderCacheBuildRequested -= CacheRequested;
        ++_gpuGeneration; ReleaseGpu();
        _host.DetachImageSource(); _document.DetachRenderResources(); _bitmap?.Dispose(); _bitmap = null;
    }
    private void Resize()
    {
        if (!_attached || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        _document.CancelViewportInteractionPreview();
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width)); var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height));
        SetSize(width, height); _document.SetViewportSize(Bounds.Width, Bounds.Height); _document.SetRenderSize(width, height); _document.RequestRender();
    }
    public void SetSize(int width, int height) { SurfaceWidth = width; SurfaceHeight = height; }
    public void SetSurface(nint surface9Ptr) { }
    public void Present(Action presentAction, IReadOnlyList<IntRect>? dirtyRects = null)
    {
        presentAction();
        if (_gpu is not null)
        {
            try { _gpu.Present(Bounds.Size); return; }
            catch (Exception ex) { GpuFailure = ex.ToString(); ReleaseGpu(); }
        }
        CopyFrame();
    }
    private async Task InitializeGpuAsync(int generation)
    {
        try
        {
            var compositor = ElementComposition.GetElementVisual(this)?.Compositor;
            if (compositor is null) return;
            var interop = await compositor.TryGetCompositionGpuInterop();
            if (!_attached || _disposed || generation != _gpuGeneration || interop is null || !GpuCanvasPresenter.Supports(interop)) return;
            _gpu = new(_host, compositor, interop, () =>
            { if (_gpu is not null && _attached && !_disposed) { try { _gpu.Present(Bounds.Size); } catch (Exception ex) { GpuFailure = ex.ToString(); ReleaseGpu(); CopyFrame(); } } });
            ElementComposition.SetElementChildVisual(this, _gpu.Visual); _document.RequestRender();
        }
        catch (Exception ex) { if (generation == _gpuGeneration) { GpuFailure = ex.ToString(); ReleaseGpu(); } }
    }
    private void ReleaseGpu()
    {
        if (_gpu is null) return;
        var presenter = _gpu; _gpu = null; ElementComposition.SetElementChildVisual(this, null);
        _ = DisposeGpuAsync(presenter);
    }
    private async Task DisposeGpuAsync(GpuCanvasPresenter presenter)
    { try { await presenter.DisposeAsync(); } catch (Exception ex) { GpuFailure ??= ex.ToString(); } }
    public void Invalidate() => InvalidateVisual();
    public void Invalidate(IntRect dirtyRect) => InvalidateVisual();
    public void Invalidate(IReadOnlyList<IntRect> dirtyRects) => InvalidateVisual();
    private void CopyFrame()
    {
        if (!_attached || _disposed) return;
        var pixels = _host.CaptureBackBufferPixels(); if (pixels.Length != checked(SurfaceWidth * SurfaceHeight * 4)) return;
        CpuReadbackFrames++;
        if (_bitmap is null || _bitmap.PixelSize.Width != SurfaceWidth || _bitmap.PixelSize.Height != SurfaceHeight)
        { _bitmap?.Dispose(); _bitmap = new WriteableBitmap(new PixelSize(SurfaceWidth, SurfaceHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul); }
        using (var frame = _bitmap.Lock()) for (var y = 0; y < SurfaceHeight; y++) Marshal.Copy(pixels, y * SurfaceWidth * 4, frame.Address + y * frame.RowBytes, SurfaceWidth * 4);
        InvalidateVisual();
    }
    public override void Render(DrawingContext context) { base.Render(context); context.FillRectangle(Brushes.Black, new Rect(Bounds.Size)); if (_bitmap is not null) context.DrawImage(_bitmap, new Rect(Bounds.Size)); }
    private void ScheduleRender(Action flush)
    {
        if (_disposed || !_attached) return; _flush = flush; if (_scheduled) return; _scheduled = true;
        Dispatcher.UIThread.Post(() => { _scheduled = false; if (!_attached || _disposed) return; var render = _flush; _flush = null; render?.Invoke(); }, DispatcherPriority.Render);
    }
    private void CacheRequested(object? sender, EventArgs e) => ScheduleCache();
    private void ScheduleCache()
    {
        if (!_attached || _disposed || _cacheScheduled || _viewportTimer.IsEnabled) return; _cacheScheduled = true;
        Dispatcher.UIThread.Post(() => {
            _cacheScheduled = false; if (!_attached || _disposed) return;
            var start = Stopwatch.GetTimestamp(); bool pending;
            do { pending = _host.PrepareRenderCacheStep(); } while (pending && !_host.IsRenderCachePreparationWaiting && Stopwatch.GetElapsedTime(start).TotalMilliseconds < 9);
            _document.RequestRenderCacheRefresh();
            if (pending) { if (_host.IsRenderCachePreparationWaiting) _cacheTimer.Start(); else ScheduleCache(); }
        }, DispatcherPriority.Background);
    }
    private static CadPointD Cad(Point point) => new(point.X, point.Y);
    internal static CadCanvasInputModifiers Modifiers(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Control) ? CadCanvasInputModifiers.Control : 0) |
        (modifiers.HasFlag(KeyModifiers.Shift) ? CadCanvasInputModifiers.Shift : 0) |
        (modifiers.HasFlag(KeyModifiers.Alt) ? CadCanvasInputModifiers.Alt : 0);
    private void Apply(CadCanvasInteractionResult result)
    {
        if (result.CaptureMouse) _pointer?.Capture(this);
        if (result.ReleaseMouseCapture) ReleaseCapture();
        Cursor = new Cursor((result.Cursor ?? _document.CanvasCursor) switch { CadCanvasCursorKind.Hand => StandardCursorType.Hand, CadCanvasCursorKind.Cross => StandardCursorType.Cross, _ => StandardCursorType.Arrow });
        UpdateCursorBadge();
    }
    private void Pressed(object? sender, PointerPressedEventArgs e)
    {
        Services.CanvasInputTrace.Write($"canvas press kind={e.GetCurrentPoint(this).Properties.PointerUpdateKind} point={e.GetPosition(this)}");
        Focus(); _pointer = e.Pointer; _document.CompleteViewportInteractionPreview(); var point = e.GetPosition(this); FlushPointer(point);
        var kind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (kind == PointerUpdateKind.RightButtonPressed) { _suppressPanMenu = false; _rightPending = true; _rightStart = point; _pointer.Capture(this); UpdateCursorBadge(); e.Handled = true; return; }
        if (kind == PointerUpdateKind.MiddleButtonPressed && _document.UserSettings.Interaction.RadialMenu.IsEnabled) { _radial.Show(point, e.KeyModifiers); UpdateCursorBadge(); _pointer.Capture(this); e.Handled = true; return; }
        if (kind == PointerUpdateKind.LeftButtonPressed && e.ClickCount == 2 && _document.CanActivateDoubleClickObjectOrSpace)
        { var result = _document.HandleDoubleClick(Cad(point)); if (!result.Handled) result = _document.OpenOleObjectAt(Cad(point)); if (result.Handled) { Apply(result); e.Handled = true; return; } }
        var button = kind == PointerUpdateKind.MiddleButtonPressed ? CadCanvasPointerButton.Middle : CadCanvasPointerButton.Left;
        var interaction = _document.PointerDown(Cad(point), button, false, Modifiers(e.KeyModifiers)); Apply(interaction); e.Handled = interaction.Handled;
    }
    private void Moved(object? sender, PointerEventArgs e)
    {
        _pointer = e.Pointer; _pendingPointer = e.GetPosition(this);
        if (_radial.IsVisible) { _radial.SetModifiers(e.KeyModifiers); _radial.Move(_pendingPointer); e.Handled = true; return; }
        if (_rightPending && !_rightPan && Math.Sqrt(Math.Pow(_pendingPointer.X - _rightStart.X, 2) + Math.Pow(_pendingPointer.Y - _rightStart.Y, 2)) > 4)
        { _rightPan = true; _suppressPanMenu = true; Apply(_document.PointerDown(Cad(_rightStart), CadCanvasPointerButton.Right, true, Modifiers(e.KeyModifiers))); }
        if (_pointerScheduled) return; _pointerScheduled = true;
        Dispatcher.UIThread.Post(() => { if (_pointerScheduled) FlushPointer(); }, DispatcherPriority.Render);
    }
    private void FlushPointer(Point? point = null)
    {
        if (point is { } value) _pendingPointer = value; if (!_pointerScheduled && point is null) return; _pointerScheduled = false;
        Apply(_document.PointerMove(Cad(_pendingPointer))); PointerPositionChanged?.Invoke(_pendingPointer);
    }
    private void Released(object? sender, PointerReleasedEventArgs e)
    {
        Trace($"release button={e.InitialPressMouseButton} kind={e.GetCurrentPoint(this).Properties.PointerUpdateKind}");
        var point = e.GetPosition(this);
        if (_radial.IsVisible && e.InitialPressMouseButton == MouseButton.Middle) { _radial.Complete(point); ReleaseCapture(); e.Handled = true; return; }
        FlushPointer(point);
        var button = e.InitialPressMouseButton switch { MouseButton.Right => CadCanvasPointerButton.Right, MouseButton.Middle => CadCanvasPointerButton.Middle, _ => CadCanvasPointerButton.Left };
        if (button == CadCanvasPointerButton.Right)
        {
            var wasClick = _rightPending && !_rightPan;
            var wasPan = _rightPan;
            _rightPending = _rightPan = false; _suppressPanMenu = true;
            if (!wasPan)
            {
                ReleaseCapture(); UpdateCursorBadge();
                // Pointer capture can prevent Win32's synthesized ContextRequested
                // from reaching this canvas. Open the menu on a completed click.
                if(wasClick && new Rect(Bounds.Size).Contains(point) && ContextMenu is {} menu)
                {
                    // Pointer placement uses the standard menu anchor/gravity rather
                    // than centering a large popup on our one-pixel anchor rectangle.
                    menu.Placement=PlacementMode.Pointer;
                    menu.PlacementRect=null;
                    menu.MaxHeight=ContextMenuHeight(point);
                    menu.Open(this); e.Handled=true;
                    Trace("menu-open-returned");
                }
                return;
            }
        }
        var result = _document.PointerUp(Cad(point), button); Apply(result); e.Handled = result.Handled;
    }
    private double ContextMenuHeight(Point point)
    {
        const double maximum=560;
        if(TopLevel.GetTopLevel(this) is not {} root)return maximum;
        var screenPoint=this.PointToScreen(point);
        var clientPoint=this.TranslatePoint(point,root)??point;
        var below=root.ClientSize.Height-clientPoint.Y-8;
        var above=clientPoint.Y-8;
        if(root.Screens?.ScreenFromPoint(screenPoint) is {} screen)
        {
            below=Math.Min(below,(screen.WorkingArea.Bottom-screenPoint.Y)/screen.Scaling-8);
            above=Math.Min(above,(screenPoint.Y-screen.WorkingArea.Y)/screen.Scaling-8);
        }
        // Prefer opening downwards. Near the bottom, leave enough space for the
        // platform positioner to flip upwards without sliding far from the click.
        return Math.Clamp(below>=240?below:Math.Max(above,below),64,maximum);
    }
    private void Wheel(object? sender, PointerWheelEventArgs e)
    {
        if (Math.Abs(e.Delta.Y) < double.Epsilon) return;
        if (_radial.IsVisible) { _radial.Wheel(e.Delta.Y); e.Handled = true; return; }
        var point = e.GetPosition(this); FlushPointer(point); var result = _document.MouseWheel(Cad(point), (int)(e.Delta.Y * 120)); Apply(result); e.Handled = result.Handled;
        _viewportTimer.Stop(); _viewportTimer.Start();
    }
    public void Cancel() { _radial.Close(); CancelGesture(); Apply(_document.Escape()); }
    private void CancelGesture() { _rightPending = _rightPan = false; _pointerScheduled = false; _radial.Close(); Apply(_document.CancelCapturedPointerGesture()); _document.CancelViewportInteractionPreview(); ReleaseCapture(); }
    private void ReleaseCapture() { _release = true; try { if (_pointer?.Captured == this) _pointer.Capture(null); } finally { _release = false; } }
    private void Keys(object? sender, KeyEventArgs e)
    {
        if (_radial.IsVisible) { if (e.Key == Key.Escape) _radial.Close(); else _radial.SetModifiers(e.KeyModifiers); e.Handled = true; return; }
        if (e.KeyModifiers == KeyModifiers.None)
        {
            switch (e.Key) { case Key.Escape: Cancel(); break; case Key.Enter: Apply(_document.CompleteCurrentDrawing()); break; case Key.Back: Apply(_document.UndoCurrentDrawingStep()); break; case Key.Delete: Apply(_document.DeleteSelection()); break; case Key.R: _document.ReselectEditObjectsCommand.Execute(null); break; case Key.Tab: Apply(_document.CycleSelection(false)); break; default: return; }
            e.Handled = true; return;
        }
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift) { Apply(_document.CycleSelection(true)); e.Handled = true; return; }
        if (e.Key == Key.Z && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift)) { _document.Redo(); e.Handled = true; return; }
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            switch (e.Key) { case Key.A: Apply(_document.SelectAllEntities()); break; case Key.Z: _document.Undo(); break; case Key.Y: _document.Redo(); break; case Key.C: _document.CopySelection(); break; case Key.X: if (_document.CopySelection() != null) Apply(_document.DeleteSelection()); break; case Key.V: Apply(_document.BeginClipboardPastePreview()); break; default: return; } e.Handled = true;
        }
    }
    public void Dispose() { if (_disposed) return; Detach(); _disposed = true; _document.PropertyChanged -= BadgeChanged; _viewportTimer.Stop(); _cacheTimer.Stop(); _radial.Close(); }
}

