using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;

[MessagePackObject]
public sealed class CadLayoutsSection
{
    [Key(0)] public List<CadLayoutData> Layouts { get; set; } = [];
}

[MessagePackObject]
public sealed class CadLayoutData
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public long PaperSpaceBlockId { get; set; }
    [Key(3)] public double PaperWidth { get; set; }
    [Key(4)] public double PaperHeight { get; set; }
    [Key(5)] public double MarginLeft { get; set; }
    [Key(6)] public double MarginTop { get; set; }
    [Key(7)] public double MarginRight { get; set; }
    [Key(8)] public double MarginBottom { get; set; }
    [Key(9)] public uint PaperColorArgb { get; set; }
    [Key(10)] public List<CadLayoutViewportData> Viewports { get; set; } = [];
}

[MessagePackObject]
public sealed class CadLayoutViewportData
{
    [Key(0)] public long Id { get; set; }
    [Key(1)] public double MinX { get; set; }
    [Key(2)] public double MinY { get; set; }
    [Key(3)] public double MaxX { get; set; }
    [Key(4)] public double MaxY { get; set; }
    [Key(5)] public double ModelCenterX { get; set; }
    [Key(6)] public double ModelCenterY { get; set; }
    [Key(7)] public double Scale { get; set; }
    [Key(8)] public double RotationRadians { get; set; }
    [Key(9)] public bool IsVisible { get; set; }
    [Key(10)] public bool IsLocked { get; set; }
}
