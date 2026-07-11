namespace Direct2dCad.ViewModels.Services.Platform;

public sealed record CadOleImportData(
    byte[] OleBytes,
    string ContentType,
    string SourceName,
    double NaturalAspectRatio);
