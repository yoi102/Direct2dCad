using Direct2dCad.Db.Data.Styles.FillStyles;
using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Styles;

[MessagePackObject]
public sealed class CadGradientFillStyleData
{
    [Key(0)] public CadGradientKind GradientKind { get; set; }
    [Key(1)] public List<CadGradientStopData> Stops { get; set; } = [];
    [Key(2)] public double GradientAngle { get; set; }
    [Key(3)] public double GradientScale { get; set; }
    [Key(4)] public CadPointData GradientOrigin { get; set; }
    [Key(5)] public bool IsCentered { get; set; }
}
