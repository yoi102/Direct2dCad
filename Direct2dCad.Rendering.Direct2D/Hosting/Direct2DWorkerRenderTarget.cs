using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Direct2dCad.Rendering.Direct2D.Hosting;

// Target lifetime is independent of the worker device and entity resource cache.
internal sealed class Direct2DWorkerRenderTarget : IDisposable
{
    private ID3D11Texture2D? _texture;
    private ID3D11Texture2D? _mainTexture;
    private IDXGIKeyedMutex? _workerMutex;
    private IDXGIKeyedMutex? _mainMutex;
    public ID2D1Bitmap1 Target { get; private set; } = null!;
    public ID2D1Bitmap1 MainReadableBitmap { get; private set; } = null!;

    public static Direct2DWorkerRenderTarget Create(
        ID3D11Device workerDevice, ID2D1DeviceContext workerContext,
        ID3D11Device mainDevice, ID2D1DeviceContext mainContext,
        int width, int height, bool crossDevice)
    {
        var result = new Direct2DWorkerRenderTarget();
        try
        {
            result._texture = workerDevice.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width, Height = (uint)height,
                MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                MiscFlags = crossDevice ? ResourceOptionFlags.SharedKeyedMutex : ResourceOptionFlags.None
            });
            result.Target = CreateBitmap(workerContext, result._texture, BitmapOptions.Target);
            if (crossDevice)
            {
                result._workerMutex = result._texture.QueryInterface<IDXGIKeyedMutex>();
                using var resource = result._texture.QueryInterface<IDXGIResource>();
                result._mainTexture = mainDevice.OpenSharedResource<ID3D11Texture2D>(resource.SharedHandle);
                result._mainMutex = result._mainTexture.QueryInterface<IDXGIKeyedMutex>();
            }
            result.MainReadableBitmap = CreateBitmap(mainContext,
                result._mainTexture ?? result._texture, BitmapOptions.None);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public void AcquireForDraw() => _workerMutex?.AcquireSync(0, int.MaxValue);
    public void FinishDraw(bool ready) => _workerMutex?.ReleaseSync(ready ? 1u : 0u);
    public void AcquireForRead() => _mainMutex?.AcquireSync(1, int.MaxValue);
    public void ReleaseRead() => _mainMutex?.ReleaseSync(0);
    public void ReleaseUnreadFrame()
    {
        _mainMutex?.AcquireSync(1, 0);
        _mainMutex?.ReleaseSync(0);
    }

    public void Dispose()
    {
        MainReadableBitmap?.Dispose();
        Target?.Dispose();
        _mainMutex?.Dispose();
        _mainTexture?.Dispose();
        _workerMutex?.Dispose();
        _texture?.Dispose();
    }

    private static ID2D1Bitmap1 CreateBitmap(ID2D1DeviceContext context,
        ID3D11Texture2D texture, BitmapOptions options)
    {
        using var surface = texture.QueryInterface<IDXGISurface>();
        return context.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1
        {
            PixelFormat = new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            DpiX = 96, DpiY = 96, BitmapOptions = options
        });
    }
}
