using Direct2dCad.Db.Cad.Settings;
namespace Direct2dCad.Db.Data.Entities;

public static class CadDimensionStyles
{
    public static CadDimensionStyle Get(string name, CadUnit unit = CadUnit.Millimeter) => name switch
    {
        "Fine" => new(name, unit, 3, 2, 2, .8, 1, .13),
        "Large" => new(name, unit, 1, 3.5, 3.5, 1.5, 2, .25),
        _ => new("ISO", unit)
    };
}
