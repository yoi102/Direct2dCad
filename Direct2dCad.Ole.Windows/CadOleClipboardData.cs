namespace Direct2dCad.Ole.Windows;

public sealed record CadOleClipboardData(
    int PixelWidth,
    int PixelHeight,
    int Stride,
    byte[] Pixels,
    byte[] OleBytes,
    string ContentType,
    string SourceName);
