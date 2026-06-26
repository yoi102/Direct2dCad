using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadArcsSection
{
    [Key(0)] public List<CadArcData> Arcs { get; set; } = [];
}
