namespace Direct2dCad.Db.Data.Entities;

public enum CadCurveOrientation
{
    Unknown,
    Clockwise,
    CounterClockwise
}

public abstract class Curve : CadEntity
{
    public abstract bool IsClosed { get; }
    public abstract double Length { get; }
    public virtual CadCurveOrientation Orientation => CadCurveOrientation.Unknown;

    protected Curve(EntityId id, LayerId layerId, BlockId ownerBlockId, string name = "")
        : base(id, layerId, ownerBlockId, name)
    {
    }
}
