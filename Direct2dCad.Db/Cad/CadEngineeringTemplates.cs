using Direct2dCad.Db.Cad.Settings;
using Direct2dCad.Db.Geometry;
namespace Direct2dCad.Db.Cad;

public static class CadEngineeringTemplates
{
    public static CadDocument Create(string key)
    {
        var (width,height,denominator,unit) = key switch
        {
            "A4" => (297d,210d,1d,CadUnit.Millimeter),
            "A3-100" => (420d,297d,100d,CadUnit.Millimeter),
            "Letter" => (279.4,215.9,1d,CadUnit.Inch),
            _ => throw new ArgumentException("Unknown engineering template.",nameof(key))
        };
        var document=CadDocument.Create(key);
        document.DocumentSettings.SetUnit(unit);
        var oldLayouts=document.Layouts.Values.ToArray();
        var id=document.CreateLayout(key,width,height,false);
        foreach(var layout in oldLayouts) document.DetachLayout(layout.Id);
        var paper=document.GetLayout(id);
        document.AddLayoutViewport(id,paper.PrintableBounds,CadPointD.Origin,1/denominator);
        return document;
    }
}
