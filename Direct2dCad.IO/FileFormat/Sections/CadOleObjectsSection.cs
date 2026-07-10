using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadOleObjectsSection
{
    [Key(0)] public List<CadOleObjectData> OleObjects { get; set; } = [];
}
