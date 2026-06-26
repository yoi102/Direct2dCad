using MessagePack;

namespace Direct2dCad.IO.FileFormat.Styles;

[MessagePackObject]
public sealed class CadTextStyleData
{
    [Key(0)] public string FontFamily { get; set; } = string.Empty;
    [Key(1)] public double TextHeight { get; set; }
    [Key(2)] public double WidthFactor { get; set; }
    [Key(3)] public double ObliqueAngle { get; set; }
    [Key(4)] public bool IsBold { get; set; }
    [Key(5)] public bool IsItalic { get; set; }
}
