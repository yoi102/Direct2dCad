using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Styles;

[MessagePackObject]
public sealed class CadHatchFillStyleData
{
    [Key(0)] public long PatternId { get; set; }
    [Key(1)] public CadColorData ForegroundColor { get; set; }
    [Key(2)] public CadColorData? BackgroundColor { get; set; }
    [Key(3)] public double HatchScale { get; set; }
    [Key(4)] public double HatchAngle { get; set; }
    [Key(5)] public CadPointData HatchOrigin { get; set; }
    [Key(6)] public bool IsAnnotative { get; set; }
}
