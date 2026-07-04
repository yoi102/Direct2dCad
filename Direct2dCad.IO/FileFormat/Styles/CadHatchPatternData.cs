using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Styles;

[MessagePackObject]
public sealed class CadHatchPatternData
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public string Description { get; set; } = string.Empty;
    [Key(3)] public List<CadHatchLineData> Lines { get; set; } = [];
}

[MessagePackObject]
public sealed class CadHatchLineData
{
    [Key(0)] public double Angle { get; set; }
    [Key(1)] public CadPointData Origin { get; set; }
    [Key(2)] public CadPointData Offset { get; set; }
    [Key(3)] public List<double> DashPattern { get; set; } = [];
}
