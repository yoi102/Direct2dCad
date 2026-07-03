using MessagePack;

namespace Direct2dCad.IO.FileFormat.Entities;

[MessagePackObject]
public sealed class CadEntityData
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public long LayerId { get; set; }
    [Key(3)] public long OwnerBlockId { get; set; }
    [Key(4)] public bool IsLocked { get; set; }
    [Key(5)] public bool IsErased { get; set; }
    [Key(6)] public bool IsVisible { get; set; }
    [Key(7)] public double? LineWeight { get; set; }
    [Key(8)] public int ZIndex { get; set; }
    [Key(9)] public bool? UseLayerColor { get; set; }
    [Key(10)] public bool? UseLayerLineWeight { get; set; }
}
