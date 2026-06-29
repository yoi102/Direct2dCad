using Direct2dCad.IO.FileFormat.Entities;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadPolylinesSection
{
    [Key(0)] public List<CadPolylineData> Polylines { get; set; } = [];
}
