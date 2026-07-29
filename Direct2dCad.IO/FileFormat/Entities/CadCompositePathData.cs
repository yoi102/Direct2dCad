using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Entities;

public enum CadCompositePathSegmentKindData : byte
{
    Line = 1,
    Arc = 2,
    Spline = 3,
    CubicBezier = 4
}

[MessagePackObject]
public sealed class CadCompositePathSegmentData
{
    [Key(0)] public CadCompositePathSegmentKindData Kind { get; set; }
    [Key(1)] public CadPointData Point { get; set; }
    [Key(2)] public double SweepAngleRadians { get; set; }
    [Key(3)] public List<CadPointData> FitPoints { get; set; } = [];
    [Key(4)] public CadPointData Control1 { get; set; }
    [Key(5)] public CadPointData Control2 { get; set; }
}

[MessagePackObject]
public sealed class CadCompositePathData
{
    [Key(0)] public CadEntityData Entity { get; set; } = new();
    [Key(1)] public CadPointData StartPoint { get; set; }
    [Key(2)] public List<CadCompositePathSegmentData> Segments { get; set; } = [];
    [Key(3)] public bool Closed { get; set; }
    [Key(4)] public long? GraphicStyleId { get; set; }
    [Key(5)] public long? FillStyleId { get; set; }
}
