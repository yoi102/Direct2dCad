using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.IO.FileFormat.Common;
using MessagePack;

namespace Direct2dCad.IO.FileFormat.Sections;


[MessagePackObject]
public sealed class CadSettingsSection
{
    [Key(0)] public CadUnit Unit { get; set; }
    [Key(1)] public int LengthPrecision { get; set; }
    [Key(2)] public int AnglePrecision { get; set; }
    [Key(3)] public CadColorData BackgroundColor { get; set; }
    [Key(4)] public CadGridType GridType { get; set; }
    [Key(5)] public double GridSpacingX { get; set; }
    [Key(6)] public double GridSpacingY { get; set; }
    [Key(7)] public int GridSubdivision { get; set; }
    [Key(8)] public List<CadLayerDrawingPriorityData> LayerDrawingPriorities { get; set; } = [];
    [Key(9)] public int DefaultLayerDrawingPriority { get; set; }
    [Key(11)] public double GridSnapSpacingX { get; set; }
    [Key(12)] public double GridSnapSpacingY { get; set; }
    [Key(13)] public double GridMinimumScreenSpacing { get; set; }
    [Key(14)] public double? GridMinimumWorldSpacing { get; set; }
    [Key(15)] public CadColorData? GridMinorLineColor { get; set; }
    [Key(16)] public CadColorData? GridMajorLineColor { get; set; }
    [Key(17)] public CadColorData? OriginColor { get; set; }
    [Key(18)] public double? GridMinorLineWidth { get; set; }
    [Key(19)] public double? GridMajorLineWidth { get; set; }
    [Key(20)] public double? OriginStrokeWidth { get; set; }
    [Key(21)] public CadColorData? GridSnapMarkerColor { get; set; }
    [Key(22)] public double? GridSnapMarkerLength { get; set; }
    [Key(23)] public double? GridSnapMarkerStrokeWidth { get; set; }
    [Key(24)] public CadSnapMarkerType? GridSnapMarkerType { get; set; }
    [Key(25)] public CadOriginDisplayType? OriginDisplayType { get; set; }
    [Key(26)] public CadOriginMarkerType? OriginMarkerType { get; set; }
    [Key(27)] public CadOriginLinePattern? OriginLinePattern { get; set; }
    [Key(28)] public double? OriginSize { get; set; }
    [Key(29)] public CadPointData? OriginPosition { get; set; }
    [Key(30)] public double? GridMinorSpacingX { get; set; }
    [Key(31)] public double? GridMinorSpacingY { get; set; }
    [Key(32)] public List<CadGridSpacingPresetData>? GridSpacingPresets { get; set; }
    [Key(33)] public Guid? GridMajorSpacingPresetId { get; set; }
    [Key(34)] public Guid? GridMinorSpacingPresetId { get; set; }
}

[MessagePackObject]
public sealed class CadGridSpacingPresetData
{
    [Key(0)] public Guid Id { get; set; }
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public double SpacingX { get; set; }
    [Key(3)] public double SpacingY { get; set; }
    [Key(4)] public bool LinkAxes { get; set; }
}
