using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Entities;

[MessagePackObject]
public sealed class CadOleObjectData
{
    [Key(0)] public CadEntityData Entity { get; set; } = new();
    [Key(1)] public CadPointData Min { get; set; }
    [Key(2)] public CadPointData Max { get; set; }
    [Key(7)] public byte[] OleBytes { get; set; } = [];
    [Key(8)] public string ContentType { get; set; } = "application/x-ole-storage";
    [Key(9)] public string SourceName { get; set; } = string.Empty;
    [Key(10)] public double Opacity { get; set; } = 1.0;
}
