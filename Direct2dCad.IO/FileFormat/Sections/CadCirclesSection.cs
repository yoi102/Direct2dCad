using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadCirclesSection
{
    [Key(0)] public List<CadCircleData> Circles { get; set; } = [];
}
