using Direct2dCad.Db;
using Direct2dCad.Db.Data.Styles;

namespace Direct2dCad.Db.Data.Styles.FillStyles;

public enum CadFillKind
{
    Hatch,
    Gradient
}

public abstract class CadFillStyle : CadStyle
{
    public override CadStyleKind Kind => CadStyleKind.Fill;
    public abstract CadFillKind FillKind { get; }

    protected CadFillStyle(StyleId id, string name)
        : base(id, name)
    {
    }
}
