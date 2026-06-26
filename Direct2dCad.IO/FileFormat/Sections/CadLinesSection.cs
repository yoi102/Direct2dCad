using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;


[MessagePackObject]
public sealed class CadLinesSection
{
    [Key(0)] public List<CadLineData> Lines { get; set; } = [];
}
