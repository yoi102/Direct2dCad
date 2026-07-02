using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Entities;

[MessagePackObject]
public sealed class CadRectangleData
{
    [Key(0)] public CadEntityData Entity { get; set; } = new();
    [Key(1)] public CadPointData Min { get; set; }
    [Key(2)] public CadPointData Max { get; set; }
    [Key(3)] public long? GraphicStyleId { get; set; }
    [Key(4)] public long? FillStyleId { get; set; }
    [Key(5)] public double CornerRadiusX { get; set; }
    [Key(6)] public double CornerRadiusY { get; set; }
}
