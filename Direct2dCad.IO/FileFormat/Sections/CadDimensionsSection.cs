using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;
namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadDimensionsSection
{
    [Key(0)] public List<CadDimensionData> Dimensions { get; set; } = [];
}
[MessagePackObject]
public sealed class CadDimensionData
{
    [Key(0)] public CadEntityData Entity { get; set; } = new();
    [Key(1)] public string DefinitionJson { get; set; } = "";
}
