using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Entities;

[MessagePackObject]
public sealed class CadEllipseArcData
{
    [Key(0)] public CadEntityData Entity { get; set; } = new();
    [Key(1)] public CadPointData Center { get; set; }
    [Key(2)] public double RadiusX { get; set; }
    [Key(3)] public double RadiusY { get; set; }
    [Key(4)] public double StartAngleRadians { get; set; }
    [Key(5)] public double SweepAngleRadians { get; set; }
    [Key(6)] public long? GraphicStyleId { get; set; }
}
