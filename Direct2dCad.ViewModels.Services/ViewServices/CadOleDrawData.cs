namespace Direct2dCad.ViewModels.Services.ViewServices;

public sealed record CadOleDrawData(
    int PixelWidth,
    int PixelHeight,
    int Stride,
    byte[] Pixels);
