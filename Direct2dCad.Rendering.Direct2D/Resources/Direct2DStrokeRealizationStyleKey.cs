using Direct2dCad.Db.Data.Entities;
using Direct2dCad.Rendering.Transient;

namespace Direct2dCad.Rendering.Direct2D.Resources;

internal enum Direct2DStrokeRealizationStyleKind
{
    Default,
    Entity,
    LevelOfDetail,
    Transient
}

internal readonly record struct Direct2DStrokeRealizationStyleKey(
    Direct2DStrokeRealizationStyleKind Kind,
    CadStrokeStyle EntityStyle,
    CadTransientLinePattern TransientPattern)
{
    public static Direct2DStrokeRealizationStyleKey Default { get; } = new(
        Direct2DStrokeRealizationStyleKind.Default,
        CadStrokeStyle.Default,
        CadTransientLinePattern.Solid);

    public static Direct2DStrokeRealizationStyleKey ForEntity(CadStrokeStyle style) => new(
        Direct2DStrokeRealizationStyleKind.Entity,
        style,
        CadTransientLinePattern.Solid);

    public static Direct2DStrokeRealizationStyleKey ForLevelOfDetail(CadStrokeStyle style) => new(
        Direct2DStrokeRealizationStyleKind.LevelOfDetail,
        style,
        CadTransientLinePattern.Solid);

    public static Direct2DStrokeRealizationStyleKey ForTransient(CadTransientLinePattern pattern) => new(
        Direct2DStrokeRealizationStyleKind.Transient,
        CadStrokeStyle.Default,
        pattern);
}
