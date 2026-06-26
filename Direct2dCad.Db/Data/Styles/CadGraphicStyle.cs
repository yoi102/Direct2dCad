using Direct2dCad.Db.Cad;

namespace Direct2dCad.Db.Data.Styles;

public sealed class CadGraphicStyle : CadStyle
{
    public override CadStyleKind Kind => CadStyleKind.Graphic;

    public CadColor StrokeColor { get; private set; }
    public CadLineWeight LineWeight { get; private set; }
    public LineTypeId LineTypeId { get; private set; }

    internal CadGraphicStyle(
        StyleId id,
        string name,
        CadColor strokeColor,
        CadLineWeight lineWeight,
        LineTypeId lineTypeId)
        : base(id, name)
    {
        StrokeColor = strokeColor;
        LineWeight = lineWeight;
        LineTypeId = lineTypeId;
    }

    public void SetStrokeColor(CadColor color) => StrokeColor = color;
    public void SetLineWeight(CadLineWeight lineWeight) => LineWeight = lineWeight;
    public void SetLineType(LineTypeId lineTypeId) => LineTypeId = lineTypeId;
}
