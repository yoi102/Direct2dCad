using Direct2dCad.Db;

namespace Direct2dCad.ViewModels.Services.ViewServices;

public sealed record CadOleDrawRequest(
    EntityId? EntityId,
    Guid RenderId,
    byte[] OleBytes,
    int FullPixelWidth,
    int FullPixelHeight,
    int RegionX,
    int RegionY,
    int PixelWidth,
    int PixelHeight);
