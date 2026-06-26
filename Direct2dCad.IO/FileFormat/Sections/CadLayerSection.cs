using Direct2dCad.IO.FileFormat.Layers;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;


[MessagePackObject]
public sealed class CadLayerSection
{
    [Key(0)] public List<CadLayerData> Layers { get; set; } = [];
}
