namespace Direct2dCad.ViewModels.Services.ViewServices;

public sealed record CadOleImportData(
    int PixelWidth,
    int PixelHeight,
    int Stride,
    byte[] Pixels,
    byte[] OleBytes,
    string ContentType,
    string SourceName);
