using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadImagesSection
{
    [Key(0)] public List<CadImageData> Images { get; set; } = [];
}
