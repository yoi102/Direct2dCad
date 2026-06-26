using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Entities;

[MessagePackObject]
public sealed class CadLineData
{
    [Key(0)] public CadEntityData Entity { get; set; } = new();
    [Key(1)] public CadPointData Start { get; set; }
    [Key(2)] public CadPointData End { get; set; }
    [Key(3)] public long? GraphicStyleId { get; set; }
}
