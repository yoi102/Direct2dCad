using Direct2dCad.Rendering;

namespace Direct2dCad.Benchmarks;

internal sealed class BenchmarkImageSource(int width, int height) : ID3D11ImageSource
{
    public int SurfaceWidth { get; private set; } = width;
    public int SurfaceHeight { get; private set; } = height;
    public int PresentCount { get; private set; }
    public int DirtyRectCount { get; private set; }

    public void SetSize(int targetWidth, int targetHeight)
    {
        SurfaceWidth = targetWidth;
        SurfaceHeight = targetHeight;
    }

    public void SetSurface(nint surface9Ptr)
    {
    }

    public void Present(Action presentAction, IReadOnlyList<IntRect>? dirtyRects = null)
    {
        presentAction();
        PresentCount++;
        DirtyRectCount = dirtyRects?.Count ?? 0;
    }

    public void Invalidate()
    {
    }

    public void Invalidate(IntRect dirtyRect)
    {
    }

    public void Invalidate(IReadOnlyList<IntRect> dirtyRects)
    {
    }
}
