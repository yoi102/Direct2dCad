using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Styles;


[MessagePackObject]
public sealed class CadGraphicStyleData
{
    [Key(0)] public CadColorData StrokeColor { get; set; }
    [Key(1)] public double LineWeight { get; set; }
    [Key(2)] public long LineTypeId { get; set; }
}
