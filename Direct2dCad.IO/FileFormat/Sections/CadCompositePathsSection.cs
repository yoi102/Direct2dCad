using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadCompositePathsSection
{
    [Key(0)] public List<CadCompositePathData> CompositePaths { get; set; } = [];
}
