using Direct2dCad.Db.Cad;
using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadBlocksSection
{
    [Key(0)] public List<CadBlockDefinitionData> Blocks { get; set; } = [];
}

[MessagePackObject]
public sealed class CadBlockDefinitionData
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public CadPointData BasePoint { get; set; }
    [Key(3)] public CadBlockKind Kind { get; set; } = CadBlockKind.User;
    [Key(4)] public bool IsReadOnly { get; set; }
}
