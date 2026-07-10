using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Entities;

[MessagePackObject]
public sealed class CadImageData
{
    [Key(0)] public CadEntityData Entity { get; set; } = new();
    [Key(1)] public CadPointData Min { get; set; }
    [Key(2)] public CadPointData Max { get; set; }
    [Key(3)] public int PixelWidth { get; set; }
    [Key(4)] public int PixelHeight { get; set; }
    [Key(5)] public int Stride { get; set; }
    [Key(6)] public byte[] Pixels { get; set; } = [];
    [Key(7)] public string ContentType { get; set; } = "image/bgra32";
    [Key(8)] public string SourceName { get; set; } = string.Empty;
}
