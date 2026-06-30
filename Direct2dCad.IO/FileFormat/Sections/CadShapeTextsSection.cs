using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadShapeTextsSection
{
    [Key(0)] public List<CadShapeTextData> ShapeTexts { get; set; } = [];
}
