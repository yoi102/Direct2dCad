using Avalonia;
using global::Avalonia.Platform;
using global::Avalonia.Rendering.Composition;
using global::Avalonia.Threading;
using Direct2dCad.Rendering.Direct2D.Hosting;

namespace Direct2dCad.Avalonia.Controls;

// Shared keyed-mutex textures are consumed by Avalonia on its render thread.
// Keep every texture alive until both its last update and asynchronous import disposal finish.
internal sealed class GpuCanvasPresenter : IAsyncDisposable
{
    private sealed class Slot(Direct2DSharedGpuFrame frame, ICompositionImportedGpuImage image)
    {
        public Direct2DSharedGpuFrame Frame { get; } = frame;
        public ICompositionImportedGpuImage Image { get; } = image;
        public Task LastUpdate { get; set; } = Task.CompletedTask;
        public async Task RetireAsync()
        {
            try { await LastUpdate; } catch { /* Still release the imported object. */ }
            try { await Image.DisposeAsync(); } finally { Frame.Dispose(); }
        }
    }
    private readonly Direct2DImageRenderHost _host;
    private readonly ICompositionGpuInterop _interop;
    private readonly CompositionDrawingSurface _surface;
    private readonly List<Slot> _slots = [];
    private readonly List<Task> _retired = [];
    private readonly Action _requestLatest;
    private bool _disposed, _retryQueued;
    public CompositionSurfaceVisual Visual { get; }
    public long PresentedFrames { get; private set; }
    private const string HandleType = KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle;
    public static bool Supports(ICompositionGpuInterop interop) => interop.SupportedImageHandleTypes.Contains(HandleType) &&
        interop.GetSynchronizationCapabilities(HandleType).HasFlag(CompositionGpuImportedImageSynchronizationCapabilities.KeyedMutex);
    public GpuCanvasPresenter(Direct2DImageRenderHost host, Compositor compositor, ICompositionGpuInterop interop, Action requestLatest)
    {
        _host = host; _interop = interop; _requestLatest = requestLatest;
        _surface = compositor.CreateDrawingSurface(); Visual = compositor.CreateSurfaceVisual(); Visual.Surface = _surface;
    }
    public void Present(Size logicalSize)
    {
        if (_disposed) return;
        if (_interop.IsLost) throw new InvalidOperationException("Avalonia's GPU context was lost.");
        Visual.Size = new(logicalSize.Width, logicalSize.Height);
        if (_slots.Any(slot => slot.Frame.Width != _host.TargetWidth || slot.Frame.Height != _host.TargetHeight))
        { foreach (var slot in _slots) _retired.Add(slot.RetireAsync()); _slots.Clear(); }
        _retired.RemoveAll(task => task.IsCompletedSuccessfully);
        var available = _slots.FirstOrDefault(slot => slot.LastUpdate.IsCompleted);
        if (available is null && _slots.Count < 3)
        {
            var frame = _host.CreateSharedGpuFrame();
            try
            {
                var image = _interop.ImportImage(new PlatformHandle(frame.SharedHandle, HandleType), new PlatformGraphicsExternalImageProperties
                { Width = frame.Width, Height = frame.Height, TopLeftOrigin = true, Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm });
                available = new(frame, image); _slots.Add(available);
            }
            catch { frame.Dispose(); throw; }
        }
        if (available is null)
        {
            if (!_retryQueued)
            {
                _retryQueued = true;
                _ = Task.WhenAny(_slots.Select(slot => slot.LastUpdate)).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
                { _retryQueued = false; if (!_disposed) _requestLatest(); }), TaskScheduler.Default);
            }
            return;
        }
        available.LastUpdate.GetAwaiter().GetResult(); // Observe completed failures; never block the UI thread.
        if (available.Image.IsLost) throw new InvalidOperationException("The imported CAD texture was lost.");
        if (!_host.TryPublishSharedGpuFrame(available.Frame))
        {
            // The CAD device was recreated. Retire its old texture and retry with the current device.
            _slots.Remove(available); _retired.Add(available.RetireAsync());
            Dispatcher.UIThread.Post(() => { if (!_disposed) _requestLatest(); }); return;
        }
        available.LastUpdate = _surface.UpdateWithKeyedMutexAsync(available.Image, 1, 0); PresentedFrames++;
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true;
        Visual.Surface = null;
        foreach (var slot in _slots) _retired.Add(slot.RetireAsync()); _slots.Clear();
        try { await Task.WhenAll(_retired); } finally { _surface.Dispose(); }
    }
}
