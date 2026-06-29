using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadSplinesSection
{
    [Key(0)] public List<CadSplineData> Splines { get; set; } = [];
}
