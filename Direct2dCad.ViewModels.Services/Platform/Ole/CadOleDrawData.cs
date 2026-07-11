namespace Direct2dCad.ViewModels.Services.Platform;

public sealed record CadOleDrawData(
    int PixelWidth,
    int PixelHeight,
    int Stride,
    byte[] Pixels);
