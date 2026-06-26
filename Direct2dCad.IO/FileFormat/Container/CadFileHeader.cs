namespace Direct2dCad.IO.FileFormat.Container;

public readonly record struct CadFileHeader(
    int ContainerVersion,
    int SectionCount,
    long SectionTableOffset,
    int SectionTableLength);
