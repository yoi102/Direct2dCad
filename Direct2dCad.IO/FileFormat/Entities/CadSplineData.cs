using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Entities;

[MessagePackObject]
public sealed class CadSplineData
{
    [Key(0)] public CadEntityData Entity { get; set; } = new();
    [Key(1)] public List<CadPointData> FitPoints { get; set; } = [];
    [Key(2)] public bool Closed { get; set; }
    [Key(3)] public long? GraphicStyleId { get; set; }
}
