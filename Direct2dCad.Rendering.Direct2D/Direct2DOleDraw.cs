using Direct2dCad.Db;

namespace Direct2dCad.Rendering.Direct2D;

public readonly record struct Direct2DOleRenderKey(
    EntityId? EntityId,
    Guid RenderId)
{
    public bool IsTransient => EntityId is null;

    public static Direct2DOleRenderKey ForEntity(EntityId entityId) => new(entityId, Guid.Empty);

    public static Direct2DOleRenderKey ForTransient(Guid renderId) => new(null, renderId);
}

public sealed record Direct2DOleDrawRequest(
    Direct2DOleRenderKey RenderKey,
    byte[] OleBytes,
    int FullPixelWidth,
    int FullPixelHeight,
    int RegionX,
    int RegionY,
    int PixelWidth,
    int PixelHeight);

public sealed record Direct2DOleDrawData(
    int PixelWidth,
    int PixelHeight,
    int Stride,
    byte[] Pixels);

public delegate Direct2DOleDrawData? Direct2DOleDrawCallback(Direct2DOleDrawRequest request);

public delegate void Direct2DOleReleaseCallback(Direct2DOleRenderKey renderKey);
