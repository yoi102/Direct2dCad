using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Entities;

[MessagePackObject]
public sealed class CadCircleData
{
    [Key(0)] public CadEntityData Entity { get; set; } = new();
    [Key(1)] public CadPointData Center { get; set; }
    [Key(2)] public double Radius { get; set; }
    [Key(3)] public long? GraphicStyleId { get; set; }
    [Key(4)] public long? FillStyleId { get; set; }
}
