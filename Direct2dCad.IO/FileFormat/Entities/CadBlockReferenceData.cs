using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Entities;

[MessagePackObject]
public sealed class CadBlockReferenceData
{
    [Key(0)] public CadEntityData Entity { get; set; } = new();
    [Key(1)] public long DefinitionBlockId { get; set; }
    [Key(2)] public CadPointData Position { get; set; }
    [Key(3)] public double RotationRadians { get; set; }
    [Key(4)] public double ScaleX { get; set; } = 1;
    [Key(5)] public double ScaleY { get; set; } = 1;
    [Key(6)] public long? GraphicStyleId { get; set; }
}
