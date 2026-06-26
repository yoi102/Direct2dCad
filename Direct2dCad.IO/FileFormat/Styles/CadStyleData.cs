using Direct2dCad.Db.Data.Styles;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Styles;

[MessagePackObject]
public sealed class CadStyleData
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public CadStyleKind Kind { get; set; }
    [Key(3)] public CadGraphicStyleData? Graphic { get; set; }
    [Key(4)] public CadTextStyleData? Text { get; set; }
    [Key(5)] public CadGradientFillStyleData? GradientFill { get; set; }
}
