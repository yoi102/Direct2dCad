using Direct2dCad.IO.FileFormat.Styles;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadStylesSection
{
    [Key(0)] public List<CadStyleData> Styles { get; set; } = [];
    [Key(1)] public List<CadHatchPatternData> HatchPatterns { get; set; } = [];
    [Key(2)] public List<CadLineTypeData> LineTypes { get; set; } = [];
}
