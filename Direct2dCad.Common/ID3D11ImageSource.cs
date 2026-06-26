namespace Direct2dCad.Common;

public interface ID3D11ImageSource
{
    int SurfaceWidth {  get; }
    int SurfaceHeight { get; }
    void SetSize(int width, int height);
    void SetSurface(nint surface9Ptr);
    void Invalidate();
    void Invalidate(IntRect dirtyRect);

}
