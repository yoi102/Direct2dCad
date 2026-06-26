namespace Direct2dCad.IO.FileFormat.Container;

public readonly record struct CadSectionEntry(
    CadSectionKind Kind,
    int Version,
    CadCompressionKind Compression,
    long PayloadOffset,
    int PayloadLength);
