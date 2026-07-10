namespace Direct2dCad.ViewModels.Services.ViewServices;

public sealed record CadOleImportData(
    byte[] OleBytes,
    string ContentType,
    string SourceName,
    double NaturalAspectRatio);
