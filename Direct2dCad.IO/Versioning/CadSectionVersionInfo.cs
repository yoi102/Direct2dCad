using Direct2dCad.IO.FileFormat.Container;

namespace Direct2dCad.IO.Versioning;

public readonly record struct CadSectionVersionInfo(
    CadSectionKind Kind,
    int CurrentVersion,
    string CurrentModelType);
