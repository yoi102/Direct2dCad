using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Styles.FillStyles;

//var ansi31Lines = CadHatchPatternLines.Diagonal45(10.0);
//var gridLines = CadHatchPatternLines.Grid(10.0);
//var brickLines = CadHatchPatternLines.Brick(20.0, 10.0);

public static class CadHatchPatternLines
{
    public static IReadOnlyList<CadHatchLineDefinition> Horizontal(double spacing = 10.0)
    {
        return
        [
            new CadHatchLineDefinition(
                angle: 0,
                origin: CadPointD.Origin,
                offset: new CadVectorD(0, spacing))
        ];
    }

    public static IReadOnlyList<CadHatchLineDefinition> Vertical(double spacing = 10.0)
    {
        return
        [
            new CadHatchLineDefinition(
                angle: 90,
                origin: CadPointD.Origin,
                offset: new CadVectorD(spacing, 0))
        ];
    }

    public static IReadOnlyList<CadHatchLineDefinition> Diagonal45(double spacing = 10.0)
    {
        return
        [
            new CadHatchLineDefinition(
                angle: 45,
                origin: CadPointD.Origin,
                offset: OffsetBySpacing(45, spacing))
        ];
    }

    public static IReadOnlyList<CadHatchLineDefinition> Diagonal135(double spacing = 10.0)
    {
        return
        [
            new CadHatchLineDefinition(
                angle: 135,
                origin: CadPointD.Origin,
                offset: OffsetBySpacing(135, spacing))
        ];
    }

    public static IReadOnlyList<CadHatchLineDefinition> Grid(double spacing = 10.0)
    {
        return
        [
            new CadHatchLineDefinition(
                angle: 0,
                origin: CadPointD.Origin,
                offset: new CadVectorD(0, spacing)),

            new CadHatchLineDefinition(
                angle: 90,
                origin: CadPointD.Origin,
                offset: new CadVectorD(spacing, 0))
        ];
    }

    public static IReadOnlyList<CadHatchLineDefinition> Cross45(double spacing = 10.0)
    {
        return
        [
            new CadHatchLineDefinition(
                angle: 45,
                origin: CadPointD.Origin,
                offset: OffsetBySpacing(45, spacing)),

            new CadHatchLineDefinition(
                angle: 135,
                origin: CadPointD.Origin,
                offset: OffsetBySpacing(135, spacing))
        ];
    }

    public static IReadOnlyList<CadHatchLineDefinition> DashedHorizontal(
        double spacing = 10.0,
        double dashLength = 6.0,
        double gapLength = 4.0)
    {
        return
        [
            new CadHatchLineDefinition(
                angle: 0,
                origin: CadPointD.Origin,
                offset: new CadVectorD(0, spacing),
                dashPattern: [dashLength, -gapLength])
        ];
    }

    public static IReadOnlyList<CadHatchLineDefinition> Dotted(
        double spacing = 10.0,
        double gapLength = 4.0)
    {
        return
        [
            new CadHatchLineDefinition(
                angle: 0,
                origin: CadPointD.Origin,
                offset: new CadVectorD(0, spacing),
                dashPattern: [0, -gapLength])
        ];
    }

    public static IReadOnlyList<CadHatchLineDefinition> Brick(double width = 20.0, double height = 10.0)
    {
        return
        [
            // 横向连续线
            new CadHatchLineDefinition(
                angle: 0,
                origin: CadPointD.Origin,
                offset: new CadVectorD(0, height)),

            // 竖向短线，第一行
            new CadHatchLineDefinition(
                angle: 90,
                origin: CadPointD.Origin,
                offset: new CadVectorD(width, height * 2),
                dashPattern: [height, -height]),

            // 竖向短线，第二行错位半砖
            new CadHatchLineDefinition(
                angle: 90,
                origin: new CadPointD(width * 0.5, height),
                offset: new CadVectorD(width, height * 2),
                dashPattern: [height, -height])
        ];
    }

    private static CadVectorD OffsetBySpacing(double angleDegrees, double spacing)
    {
        var radians = angleDegrees * Math.PI / 180.0;

        // 线方向为 angle，Offset 使用垂直方向，表示平行线之间的间距。
        return new CadVectorD(
            -Math.Sin(radians) * spacing,
            Math.Cos(radians) * spacing);
    }
}
