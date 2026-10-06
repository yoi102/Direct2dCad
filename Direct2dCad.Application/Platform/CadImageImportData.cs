namespace Direct2dCad.Application.Platform;

public sealed record CadImageImportData(
    int PixelWidth,
    int PixelHeight,
    int Stride,
    byte[] Pixels,
    string ContentType,
    string SourceName);
