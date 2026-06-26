using Direct2dCad.IO.FileFormat.Styles;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadStylesSection
{
    [Key(0)] public List<CadStyleData> Styles { get; set; } = [];
}
