using Direct2dCad.IO.FileFormat.Common;
using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadRegionsSection
{
    [Key(0)] public List<CadRegionData> Regions { get; set; } = [];
}
[MessagePackObject]
public sealed class CadRegionData
{
    [Key(0)] public CadEntityData Entity { get; set; } = new();
    [Key(1)] public List<List<CadRegionEdgeData>> Contours { get; set; } = [];
    [Key(2)] public long? GraphicStyleId { get; set; }
    [Key(3)] public long? FillStyleId { get; set; }
}
[MessagePackObject]
public sealed class CadRegionEdgeData
{
    [Key(0)] public CadPointData Start { get; set; }
    [Key(1)] public CadPointData End { get; set; }
    [Key(2)] public CadPointData Center { get; set; }
    [Key(3)] public double Radius { get; set; }
    [Key(4)] public double StartAngle { get; set; }
    [Key(5)] public double Sweep { get; set; }
}
