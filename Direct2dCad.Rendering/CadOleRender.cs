using Direct2dCad.Db;

namespace Direct2dCad.Rendering;

public readonly record struct CadOleRenderKey(
    EntityId? EntityId,
    Guid RenderId)
{
    public bool IsTransient => EntityId is null;

    public static CadOleRenderKey ForEntity(EntityId entityId) => new(entityId, Guid.Empty);

    public static CadOleRenderKey ForTransient(Guid renderId) => new(null, renderId);
}

public sealed record CadOleRenderRequest(
    CadOleRenderKey RenderKey,
    ReadOnlyMemory<byte> OleBytes,
    int FullPixelWidth,
    int FullPixelHeight,
    int RegionX,
    int RegionY,
    int PixelWidth,
    int PixelHeight);

public sealed record CadOleRenderData(
    int PixelWidth,
    int PixelHeight,
    int Stride,
    byte[] Pixels);

public delegate CadOleRenderData? CadOleRenderCallback(CadOleRenderRequest request);

public delegate void CadOleReleaseCallback(CadOleRenderKey renderKey);
