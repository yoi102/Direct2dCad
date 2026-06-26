using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Layers;

[MessagePackObject]
public sealed class CadLayerData
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public bool IsVisible { get; set; }
    [Key(3)] public bool IsLocked { get; set; }
    [Key(4)] public bool IsFrozen { get; set; }
    [Key(5)] public CadColorData Color { get; set; }
    [Key(6)] public double LineWeight { get; set; }
    [Key(7)] public long? DefaultGraphicStyleId { get; set; }
}
