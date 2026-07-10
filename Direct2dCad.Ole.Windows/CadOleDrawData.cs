namespace Direct2dCad.Ole.Windows;

public sealed record CadOleDrawData(
    int PixelWidth,
    int PixelHeight,
    int Stride,
    byte[] Pixels);
