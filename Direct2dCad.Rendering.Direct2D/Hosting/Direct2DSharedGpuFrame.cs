using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Direct2dCad.Rendering.Direct2D.Hosting;

/// <summary>A GPU-only BGRA frame. Producer uses key 0 and hands key 1 to the consumer.</summary>
public sealed unsafe class Direct2DSharedGpuFrame : IDisposable
{
    private readonly ID3D11Texture2D _texture;
    private readonly IDXGIKeyedMutex _mutex;
    private bool _disposed;
    public int Width { get; }
    public int Height { get; }
    public nint SharedHandle { get; }
    internal nint DevicePointer { get; }
    internal Direct2DSharedGpuFrame(ID3D11Device device, int width, int height)
    {
        Width = width; Height = height; DevicePointer = device.NativePointer;
        _texture = device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None, MiscFlags = ResourceOptionFlags.SharedKeyedMutex
        });
        try
        {
            _mutex = _texture.QueryInterface<IDXGIKeyedMutex>();
            using var resource = _texture.QueryInterface<IDXGIResource>(); SharedHandle = resource.SharedHandle;
        }
        catch { _mutex?.Dispose(); _texture.Dispose(); throw; }
    }
    internal bool TryPublish(ID3D11DeviceContext context, ID3D11Texture2D source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // WAIT_TIMEOUT is a positive HRESULT, so check S_OK explicitly rather than Success.
        // The wrapper discards positive HRESULTs. Preserve WAIT_TIMEOUT / WAIT_ABANDONED.
        var pointer = _mutex.NativePointer;
        var acquired = ((delegate* unmanaged[Stdcall]<nint, ulong, uint, int>)(*(nint**)pointer)[8])(pointer, 0, 0);
        if (acquired == 258) return false;
        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(acquired);
        if (acquired != 0) throw new InvalidOperationException("The shared GPU mutex was abandoned.");
        try { context.CopyResource(_texture, source); context.Flush(); }
        catch { _mutex.ReleaseSync(0); throw; }
        _mutex.ReleaseSync(1); return true;
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _mutex.Dispose(); _texture.Dispose(); }
}
