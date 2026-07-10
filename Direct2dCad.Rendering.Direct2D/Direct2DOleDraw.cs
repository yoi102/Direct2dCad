using Direct2dCad.Db;

namespace Direct2dCad.Rendering.Direct2D;

public sealed record Direct2DOleDrawRequest(
    EntityId EntityId,
    byte[] OleBytes,
    int PixelWidth,
    int PixelHeight);

public sealed record Direct2DOleDrawData(
    int PixelWidth,
    int PixelHeight,
    int Stride,
    byte[] Pixels);

public delegate Direct2DOleDrawData? Direct2DOleDrawCallback(Direct2DOleDrawRequest request);
