using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadEllipsesSection
{
    [Key(0)] public List<CadEllipseData> Ellipses { get; set; } = [];
    [Key(1)] public List<CadEllipseArcData> EllipseArcs { get; set; } = [];
}
