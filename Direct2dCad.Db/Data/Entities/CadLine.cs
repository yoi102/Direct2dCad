using Direct2dCad.Db.Geometry;

namespace Direct2dCad.Db.Data.Entities;

public sealed class CadLine : Curve
{
    public CadPointD Start { get; private set; }
    public CadPointD End { get; private set; }
    public StyleId? GraphicStyleId { get; private set; }
    public override bool IsClosed => false;
    public override double Length => Start.DistanceTo(End);
    public override CadRectD Bounds => CadRectD.Empty.ExpandToInclude(Start).ExpandToInclude(End);

    internal CadLine(
        EntityId id,
        LayerId layerId,
        BlockId ownerBlockId,
        CadPointD start,
        CadPointD end,
        string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
        Start = start;
        End = end;
    }

    public void SetStart(CadPointD start) => Start = start;

    public void SetEnd(CadPointD end) => End = end;

    public void SetGeometry(CadPointD start, CadPointD end)
    {
        Start = start;
        End = end;
    }

    public void SetGraphicStyleInternal(StyleId? styleId) => GraphicStyleId = styleId;
}
