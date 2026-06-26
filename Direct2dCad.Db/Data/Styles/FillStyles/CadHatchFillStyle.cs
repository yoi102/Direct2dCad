using Direct2dCad.Db.Cad;
using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Styles.FillStyles;

/// <summary>
/// Hatch 填充样式。
/// 这里只保存“如何使用 HatchPattern”，不保存 HatchPattern 的线条定义。
/// </summary>
public sealed class CadHatchFillStyle : CadFillStyle
{
    public override CadFillKind FillKind => CadFillKind.Hatch;

    public HatchPatternId PatternId { get; private set; }
    public CadColor ForegroundColor { get; private set; }
    public CadColor? BackgroundColor { get; private set; }
    public double HatchScale { get; private set; }
    public double HatchAngle { get; private set; }
    public CadPointD HatchOrigin { get; private set; }

    /// <summary>
    /// 是否参与注释比例。注意：Annotative 不是“不跟随窗口缩放”，而是根据 AnnotationScale 修正基准比例。
    /// </summary>
    public bool IsAnnotative { get; private set; }

    internal CadHatchFillStyle(
        StyleId id,
        string name,
        HatchPatternId patternId,
        CadColor foregroundColor,
        CadColor? backgroundColor = null,
        double hatchScale = 1.0,
        double hatchAngle = 0.0,
        CadPointD? hatchOrigin = null,
        bool isAnnotative = false)
        : base(id, name)
    {
        PatternId = patternId;
        ForegroundColor = foregroundColor;
        BackgroundColor = backgroundColor;
        HatchScale = GuardPositive(hatchScale, nameof(hatchScale));
        HatchAngle = hatchAngle;
        HatchOrigin = hatchOrigin ?? CadPointD.Origin;
        IsAnnotative = isAnnotative;
    }

    internal void SetPatternInternal(HatchPatternId patternId) => PatternId = patternId;

    public void SetForegroundColor(CadColor color) => ForegroundColor = color;
    public void SetBackgroundColor(CadColor? color) => BackgroundColor = color;
    public void SetScale(double scale) => HatchScale = GuardPositive(scale, nameof(scale));
    public void SetAngle(double angle) => HatchAngle = angle;
    public void SetOrigin(CadPointD origin) => HatchOrigin = origin;
    public void SetAnnotative(bool value) => IsAnnotative = value;

    public double GetEffectiveScale(double annotationScale)
    {
        if (annotationScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(annotationScale));

        return IsAnnotative ? HatchScale * annotationScale : HatchScale;
    }

    private static double GuardPositive(double value, string paramName)
    {
        return value <= 0 || double.IsNaN(value) || double.IsInfinity(value)
            ? throw new ArgumentOutOfRangeException(paramName)
            : value;
    }
}
