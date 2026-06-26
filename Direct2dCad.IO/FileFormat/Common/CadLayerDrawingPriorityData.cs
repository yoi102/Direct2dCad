using MessagePack;

namespace Direct2dCad.IO.FileFormat.Common;


[MessagePackObject]
public sealed class CadLayerDrawingPriorityData
{
    [Key(0)] public long LayerId { get; set; }
    [Key(1)] public int Priority { get; set; }
}
