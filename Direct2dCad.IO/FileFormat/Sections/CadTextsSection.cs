using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadTextsSection
{
    [Key(0)] public List<CadTextData> Texts { get; set; } = [];
}
