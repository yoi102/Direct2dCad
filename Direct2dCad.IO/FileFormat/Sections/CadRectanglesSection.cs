using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadRectanglesSection
{
    [Key(0)] public List<CadRectangleData> Rectangles { get; set; } = [];
}
