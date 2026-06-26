using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Entities;

[MessagePackObject]
public sealed class CadTextData
{
    [Key(0)] public CadEntityData Entity { get; set; } = new();
    [Key(1)] public string Text { get; set; } = string.Empty;
    [Key(2)] public CadPointData Position { get; set; }
    [Key(3)] public double Height { get; set; }
    [Key(4)] public double RotationRadians { get; set; }
    [Key(5)] public long? TextStyleId { get; set; }
    [Key(6)] public long? GraphicStyleId { get; set; }
}
