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
    [Key(4)] public long? FillStyleId { get; set; }
    [Key(5)] public CadNurbsData? Nurbs { get; set; }
}

[MessagePackObject]
public sealed class CadNurbsData
{
    [Key(0)] public int Degree { get; set; }
    [Key(1)] public List<CadPointData> ControlPoints { get; set; } = [];
    [Key(2)] public List<double> Knots { get; set; } = [];
    [Key(3)] public List<double> Weights { get; set; } = [];
    [Key(4)] public bool Periodic { get; set; }
}
