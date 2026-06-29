using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Entities;

[MessagePackObject]
public sealed class CadPolylineData
{
    [Key(0)] public CadEntityData Entity { get; set; } = new();
    [Key(1)] public List<CadPointData> Points { get; set; } = [];
    [Key(2)] public bool Closed { get; set; }
    [Key(3)] public long? GraphicStyleId { get; set; }
    [Key(4)] public long? FillStyleId { get; set; }
}
