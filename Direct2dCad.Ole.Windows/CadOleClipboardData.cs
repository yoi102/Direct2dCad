namespace Direct2dCad.Ole.Windows;

public sealed record CadOleClipboardData(
    byte[] OleBytes,
    string ContentType,
    string SourceName,
    double NaturalAspectRatio);
