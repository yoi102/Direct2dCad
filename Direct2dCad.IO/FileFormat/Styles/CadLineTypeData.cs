using MessagePack;

namespace Direct2dCad.IO.FileFormat.Styles;

[MessagePackObject]
public sealed class CadLineTypeData
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public string Description { get; set; } = string.Empty;
    [Key(3)] public List<double> DashPattern { get; set; } = [];
}
